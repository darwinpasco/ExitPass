using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.Operations;

/// <summary>
/// Composes read-only ticket session summary data from Vendor PMS Adapter and Central PMS local status.
///
/// Invariants:
/// - Does not call vendor parking fee confirmation.
/// - Does not create or mutate payment attempts, confirmations, exit authorizations, gate actions, coupons, or reconciliation state.
/// - Does not expose raw vendor payloads.
/// </summary>
public sealed class TicketSessionSummaryService : ITicketSessionSummaryService
{
    private readonly IVendorPmsParkingResolutionClient _vendorClient;
    private readonly ITicketSessionSummaryReadRepository _readRepository;
    private readonly ILogger<TicketSessionSummaryService> _logger;

    /// <summary>
    /// Creates a ticket session summary service.
    /// </summary>
    public TicketSessionSummaryService(
        IVendorPmsParkingResolutionClient vendorClient,
        ITicketSessionSummaryReadRepository readRepository,
        ILogger<TicketSessionSummaryService> logger)
    {
        _vendorClient = vendorClient ?? throw new ArgumentNullException(nameof(vendorClient));
        _readRepository = readRepository ?? throw new ArgumentNullException(nameof(readRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TicketSessionSummaryResult> GetAsync(
        TicketSessionSummaryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var correlationId = ResolveCorrelationId(command.CorrelationId);
        var diagnostics = new List<TicketSessionSummaryDiagnostic>();
        if (!TryNormalizeTicket(command, diagnostics, correlationId, out var ticketNumber, out var cardNum))
        {
            return TicketSessionSummaryResult.Failed(
                TicketSessionSummaryOutcome.InvalidRequest,
                "INVALID_TICKET_SESSION_SUMMARY_REQUEST",
                retryable: false,
                diagnostics,
                correlationId);
        }

        VendorParkingSessionLookupResponse sessionResponse;
        try
        {
            sessionResponse = await _vendorClient.ResolveSessionAsync(
                new VendorParkingSessionLookupRequest(
                    PlateNumber: null,
                    TicketReference: ticketNumber,
                    correlationId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new TicketSessionSummaryDiagnostic(
                "VENDOR_ADAPTER_UNAVAILABLE",
                "Vendor adapter session lookup could not be completed.",
                "vendor-session-lookup",
                Retryable: true,
                VendorConfirmationCode: "VENDOR_ADAPTER_UNAVAILABLE",
                VendorMessage: "Vendor adapter session lookup could not be completed.",
                CorrelationId: correlationId));

            _logger.LogWarning(
                ex,
                "Ticket session summary vendor lookup failed before a provider-neutral response was returned. correlation_id={CorrelationId}",
                correlationId);

            return TicketSessionSummaryResult.Failed(
                TicketSessionSummaryOutcome.AdapterUnavailable,
                "VENDOR_ADAPTER_UNAVAILABLE",
                retryable: true,
                diagnostics,
                correlationId);
        }

        if (sessionResponse.Status != VendorParkingLookupStatus.Found)
        {
            return MapVendorFailure(
                sessionResponse.Status,
                sessionResponse.ErrorCode,
                sessionResponse.Retryable,
                sessionResponse.CorrelationId,
                "vendor-session-lookup",
                diagnostics);
        }

        if (!TryValidateSession(sessionResponse.Session, diagnostics, sessionResponse.CorrelationId, out var session))
        {
            return TicketSessionSummaryResult.Failed(
                TicketSessionSummaryOutcome.VendorError,
                "MALFORMED_VENDOR_SESSION",
                retryable: false,
                diagnostics,
                sessionResponse.CorrelationId);
        }

        var quote = session.TariffQuote;
        if (quote is null)
        {
            VendorTariffQuoteResponse tariffResponse;
            try
            {
                tariffResponse = await _vendorClient.ResolveTariffAsync(
                    new VendorTariffQuoteRequest(
                        PlateNumber: null,
                        TicketReference: ticketNumber,
                        sessionResponse.CorrelationId),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(new TicketSessionSummaryDiagnostic(
                    "VENDOR_ADAPTER_UNAVAILABLE",
                    "Vendor adapter tariff calculation could not be completed.",
                    "vendor-tariff-calculation",
                    Retryable: true,
                    VendorConfirmationCode: "VENDOR_ADAPTER_UNAVAILABLE",
                    VendorMessage: "Vendor adapter tariff calculation could not be completed.",
                    CorrelationId: sessionResponse.CorrelationId));

                _logger.LogWarning(
                    ex,
                    "Ticket session summary vendor tariff calculation failed before a provider-neutral response was returned. correlation_id={CorrelationId}",
                    sessionResponse.CorrelationId);

                return TicketSessionSummaryResult.Failed(
                    TicketSessionSummaryOutcome.AdapterUnavailable,
                    "VENDOR_ADAPTER_UNAVAILABLE",
                    retryable: true,
                    diagnostics,
                    sessionResponse.CorrelationId);
            }

            if (tariffResponse.Status != VendorParkingLookupStatus.Found)
            {
                return MapVendorFailure(
                    tariffResponse.Status,
                    tariffResponse.ErrorCode,
                    tariffResponse.Retryable,
                    tariffResponse.CorrelationId,
                    "vendor-tariff-calculation",
                    diagnostics);
            }

            quote = tariffResponse.Quote;
        }

        if (!TryValidateQuote(quote, diagnostics, sessionResponse.CorrelationId))
        {
            return TicketSessionSummaryResult.Failed(
                TicketSessionSummaryOutcome.VendorError,
                "MALFORMED_VENDOR_TARIFF_QUOTE",
                retryable: false,
                diagnostics,
                sessionResponse.CorrelationId);
        }

        var localStatus = await _readRepository.FindLocalStatusAsync(
            ticketNumber,
            command.SiteId,
            command.SiteGroupId,
            cancellationToken);

        if (localStatus.Outcome == TicketSessionLocalStatusOutcome.Ambiguous)
        {
            diagnostics.Add(new TicketSessionSummaryDiagnostic(
                "LOCAL_TICKET_AMBIGUOUS",
                "Central PMS found multiple local sessions for the ticket and scope.",
                "central-pms-read-model",
                Retryable: false,
                CorrelationId: sessionResponse.CorrelationId));

            return TicketSessionSummaryResult.Failed(
                TicketSessionSummaryOutcome.Ambiguous,
                "AMBIGUOUS_TICKET_SESSION",
                retryable: false,
                diagnostics,
                sessionResponse.CorrelationId);
        }

        if (localStatus.Outcome == TicketSessionLocalStatusOutcome.NotFound)
        {
            diagnostics.Add(new TicketSessionSummaryDiagnostic(
                "LOCAL_PAYMENT_STATUS_UNAVAILABLE",
                "Central PMS has no local payment attempt status for this ticket and scope.",
                "central-pms-read-model",
                Retryable: false,
                CorrelationId: sessionResponse.CorrelationId));
        }

        var vendorSystemCode = NormalizeSystemCode(session.VendorProviderCode);
        const string vendorConfirmationCode = "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE";
        const string vendorMessage = "Vendor session and tariff summary resolved.";
        diagnostics.Add(new TicketSessionSummaryDiagnostic(
            vendorConfirmationCode,
            "Vendor payment confirmation status is not available from the current read-only adapter contract.",
            "central-pms-read-model",
            Retryable: false,
            VendorSystemCode: vendorSystemCode,
            VendorConfirmationCode: vendorConfirmationCode,
            VendorMessage: "Vendor confirmation status is not available from the current read-only adapter contract.",
            CorrelationId: sessionResponse.CorrelationId));

        var summary = new TicketSessionSummaryReadModel(
            TicketNumber: ticketNumber,
            CardNum: cardNum,
            PlateLicense: string.IsNullOrWhiteSpace(session.PlateNumber) ? "Unknown" : session.PlateNumber.Trim(),
            ParkingInTime: session.EntryTime,
            ParkingDurationSeconds: session.ParkingDurationSeconds,
            FeeMinorUnits: quote!.AmountMinor,
            CurrencyCode: NormalizeUpper(quote.Currency),
            FeeRuleType: null,
            FeeRuleIndexCode: NormalizeOptional(quote.TariffVersionReference),
            FeeRuleName: NormalizeOptional(quote.TariffName),
            VendorSessionStatus: NormalizeOptional(session.Status),
            VendorSystemCode: vendorSystemCode,
            VendorConfirmationCode: vendorConfirmationCode,
            VendorMessage: vendorMessage,
            ParkingSessionId: localStatus.Status?.ParkingSessionId,
            PaymentAttemptId: localStatus.Status?.PaymentAttemptId,
            PaymentAttemptStatus: localStatus.Status?.PaymentAttemptStatus,
            PaymentStatus: localStatus.Status?.PaymentStatus,
            PaymentConfirmationStatus: localStatus.Status?.PaymentConfirmationStatus,
            VendorConfirmationStatus: localStatus.Status?.VendorConfirmationStatus,
            VendorConfirmationTimestamp: localStatus.Status?.VendorConfirmationTimestamp);

        return TicketSessionSummaryResult.Resolved(summary, diagnostics, sessionResponse.CorrelationId);
    }

    private static bool TryNormalizeTicket(
        TicketSessionSummaryCommand command,
        List<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId,
        out string ticketNumber,
        out string? cardNum)
    {
        ticketNumber = NormalizeOptional(command.TicketNumber) ?? string.Empty;
        cardNum = NormalizeOptional(command.CardNum);
        var resolved = ticketNumber.Length > 0 ? ticketNumber : cardNum;

        if (string.IsNullOrWhiteSpace(resolved))
        {
            diagnostics.Add(new TicketSessionSummaryDiagnostic(
                "TICKET_IDENTIFIER_REQUIRED",
                "TicketNumber or CardNum is required.",
                "request-validation",
                Retryable: false,
                CorrelationId: correlationId));
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ticketNumber) &&
            !string.IsNullOrWhiteSpace(cardNum) &&
            !string.Equals(ticketNumber, cardNum, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new TicketSessionSummaryDiagnostic(
                "TICKET_IDENTIFIER_CONFLICT",
                "TicketNumber and CardNum must refer to the same ticket identifier.",
                "request-validation",
                Retryable: false,
                CorrelationId: correlationId));
            return false;
        }

        ticketNumber = resolved!;
        return true;
    }

    private static TicketSessionSummaryResult MapVendorFailure(
        VendorParkingLookupStatus status,
        string? adapterErrorCode,
        bool retryable,
        Guid correlationId,
        string source,
        List<TicketSessionSummaryDiagnostic> diagnostics)
    {
        var outcome = status switch
        {
            VendorParkingLookupStatus.NotFound => TicketSessionSummaryOutcome.NotFound,
            VendorParkingLookupStatus.Ambiguous => TicketSessionSummaryOutcome.Ambiguous,
            VendorParkingLookupStatus.UnavailableRetryable => TicketSessionSummaryOutcome.AdapterUnavailable,
            VendorParkingLookupStatus.AdapterError => TicketSessionSummaryOutcome.VendorError,
            VendorParkingLookupStatus.ValidationError => TicketSessionSummaryOutcome.InvalidRequest,
            _ => TicketSessionSummaryOutcome.VendorError
        };

        var errorCode = outcome switch
        {
            TicketSessionSummaryOutcome.NotFound => "TICKET_SESSION_NOT_FOUND",
            TicketSessionSummaryOutcome.Ambiguous => "AMBIGUOUS_TICKET_SESSION",
            TicketSessionSummaryOutcome.AdapterUnavailable => "VENDOR_LOOKUP_UNAVAILABLE",
            TicketSessionSummaryOutcome.InvalidRequest => "INVALID_TICKET_SESSION_SUMMARY_REQUEST",
            _ => source == "vendor-tariff-calculation" ? "VENDOR_TARIFF_CALCULATION_FAILED" : "VENDOR_LOOKUP_FAILED"
        };

        diagnostics.Add(new TicketSessionSummaryDiagnostic(
            adapterErrorCode ?? errorCode,
            ResolveVendorFailureMessage(status, source),
            source,
            retryable,
            VendorConfirmationCode: adapterErrorCode ?? errorCode,
            VendorMessage: ResolveVendorFailureMessage(status, source),
            CorrelationId: correlationId));

        return TicketSessionSummaryResult.Failed(
            outcome,
            errorCode,
            retryable,
            diagnostics,
            correlationId);
    }

    private static bool TryValidateSession(
        VendorParkingSessionDto? session,
        List<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId,
        out VendorParkingSessionDto validSession)
    {
        validSession = session!;
        if (session is not null &&
            !string.IsNullOrWhiteSpace(session.VendorProviderCode) &&
            !string.IsNullOrWhiteSpace(session.VendorSessionReference) &&
            session.EntryTime != default)
        {
            return true;
        }

        diagnostics.Add(new TicketSessionSummaryDiagnostic(
            "MALFORMED_VENDOR_SESSION",
            "Vendor session response could not be mapped to the summary contract.",
            "vendor-session-lookup",
            Retryable: false,
            VendorConfirmationCode: "MALFORMED_VENDOR_SESSION",
            VendorMessage: "Vendor session response could not be mapped to the summary contract.",
            CorrelationId: correlationId));
        return false;
    }

    private static bool TryValidateQuote(
        VendorTariffQuoteDto? quote,
        List<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId)
    {
        if (quote is not null &&
            quote.AmountMinor >= 0 &&
            !string.IsNullOrWhiteSpace(quote.Currency) &&
            quote.CalculatedAt != default)
        {
            return true;
        }

        diagnostics.Add(new TicketSessionSummaryDiagnostic(
            "MALFORMED_VENDOR_TARIFF_QUOTE",
            "Vendor tariff response could not be mapped to the summary contract.",
            "vendor-tariff-calculation",
            Retryable: false,
            VendorConfirmationCode: "MALFORMED_VENDOR_TARIFF_QUOTE",
            VendorMessage: "Vendor tariff response could not be mapped to the summary contract.",
            CorrelationId: correlationId));
        return false;
    }

    private static string ResolveVendorFailureMessage(VendorParkingLookupStatus status, string source)
    {
        var subject = source == "vendor-tariff-calculation" ? "tariff calculation" : "session lookup";
        return status switch
        {
            VendorParkingLookupStatus.NotFound => $"Vendor {subject} did not find a matching ticket.",
            VendorParkingLookupStatus.Ambiguous => $"Vendor {subject} returned multiple matching sessions.",
            VendorParkingLookupStatus.UnavailableRetryable => $"Vendor {subject} is temporarily unavailable.",
            VendorParkingLookupStatus.ValidationError => $"Vendor {subject} rejected the request as invalid.",
            VendorParkingLookupStatus.VendorRejected => $"Vendor {subject} was rejected by the vendor PMS.",
            _ => $"Vendor {subject} failed."
        };
    }

    private static Guid ResolveCorrelationId(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeUpper(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeSystemCode(string? value)
    {
        var normalized = NormalizeUpper(value);
        return normalized?.Replace('-', '_');
    }
}
