using System.Diagnostics;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Maps provider-neutral Vendor PMS Adapter parking session and tariff data into Central PMS domain objects.
/// </summary>
public sealed class ResolveVendorParkingHandler : IResolveVendorParkingUseCase
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.VendorParking");

    private readonly IVendorPmsParkingResolutionClient _vendorClient;
    private readonly CentralPmsMetrics _metrics;
    private readonly ILogger<ResolveVendorParkingHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolveVendorParkingHandler"/> class.
    /// </summary>
    /// <param name="vendorClient">Provider-neutral Vendor PMS Adapter client.</param>
    /// <param name="metrics">Shared Central PMS business metrics publisher.</param>
    /// <param name="logger">Application logger.</param>
    public ResolveVendorParkingHandler(
        IVendorPmsParkingResolutionClient vendorClient,
        CentralPmsMetrics metrics,
        ILogger<ResolveVendorParkingHandler> logger)
    {
        _vendorClient = vendorClient;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResolveVendorParkingResult> ExecuteAsync(
        ResolveVendorParkingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("ResolveVendorParking", ActivityKind.Internal);
        activity?.SetTag("operation", "resolve_vendor_parking");
        activity?.SetTag("correlation_id", command.CorrelationId);
        activity?.SetTag("lookup.identifier_type", ResolveIdentifierType(command));

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = command.CorrelationId
        });

        if (string.IsNullOrWhiteSpace(command.SiteGroupId) ||
            string.IsNullOrWhiteSpace(command.SiteId) ||
            (string.IsNullOrWhiteSpace(command.PlateNumber) && string.IsNullOrWhiteSpace(command.TicketReference)))
        {
            return CompleteFailure(
                activity,
                ResolveVendorParkingOutcome.InvalidRequest,
                "INVALID_VENDOR_LOOKUP_REQUEST",
                retryable: false,
                command.CorrelationId,
                vendorSystemId: null);
        }

        var sessionResponse = await _vendorClient.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(
                Normalize(command.PlateNumber),
                Normalize(command.TicketReference),
                command.CorrelationId),
            cancellationToken);

        if (sessionResponse.Status != VendorParkingLookupStatus.Found)
        {
            return CompleteFailure(
                activity,
                MapOutcome(sessionResponse.Status),
                sessionResponse.ErrorCode ?? sessionResponse.Status.ToString().ToUpperInvariant(),
                sessionResponse.Retryable,
                sessionResponse.CorrelationId,
                sessionResponse.Session?.VendorProviderCode);
        }

        if (!TryValidateSession(sessionResponse.Session, out var session))
        {
            return CompleteFailure(
                activity,
                ResolveVendorParkingOutcome.MalformedVendorResponse,
                "MALFORMED_VENDOR_SESSION",
                retryable: false,
                sessionResponse.CorrelationId,
                sessionResponse.Session?.VendorProviderCode);
        }

        var quote = session.TariffQuote;
        if (quote is null)
        {
            var tariffResponse = await _vendorClient.ResolveTariffAsync(
                new VendorTariffQuoteRequest(
                    Normalize(command.PlateNumber),
                    Normalize(command.TicketReference),
                    command.CorrelationId),
                cancellationToken);

            if (tariffResponse.Status != VendorParkingLookupStatus.Found)
            {
                return CompleteFailure(
                    activity,
                    MapOutcome(tariffResponse.Status),
                    tariffResponse.ErrorCode ?? tariffResponse.Status.ToString().ToUpperInvariant(),
                    tariffResponse.Retryable,
                    tariffResponse.CorrelationId,
                    session.VendorProviderCode);
            }

            quote = tariffResponse.Quote;
        }

        if (!TryValidateQuote(quote))
        {
            return CompleteFailure(
                activity,
                ResolveVendorParkingOutcome.MalformedVendorResponse,
                "MALFORMED_VENDOR_TARIFF_QUOTE",
                retryable: false,
                sessionResponse.CorrelationId,
                session.VendorProviderCode);
        }

        var validQuote = quote!;
        var parkingSessionId = Guid.NewGuid();
        var tariffSnapshotId = Guid.NewGuid();
        var centralSession = ParkingSession.Rehydrate(
            parkingSessionId,
            command.SiteGroupId.Trim(),
            command.SiteId.Trim(),
            session.VendorProviderCode.Trim(),
            session.VendorSessionReference.Trim(),
            ResolveIdentifierType(command),
            Normalize(session.PlateNumber),
            Normalize(command.TicketReference),
            session.EntryTime,
            ParkingSessionStatus.PaymentRequired);

        var amount = decimal.Divide(validQuote.AmountMinor, 100m);
        var tariffSnapshot = TariffSnapshot.Rehydrate(
            tariffSnapshotId,
            parkingSessionId,
            TariffSnapshotSourceType.Base,
            amount,
            0m,
            0m,
            amount,
            validQuote.Currency.Trim().ToUpperInvariant(),
            amount,
            validQuote.TariffVersionReference,
            null,
            validQuote.CalculatedAt,
            validQuote.CalculatedAt.AddMinutes(15),
            TariffSnapshotStatus.Active,
            null,
            null);

        activity?.SetTag("vendor_system_id", session.VendorProviderCode);
        activity?.SetTag("parking_session_id", parkingSessionId);
        activity?.SetTag("tariff_snapshot_id", tariffSnapshotId);
        activity?.SetTag("lookup.outcome", ResolveVendorParkingOutcome.Resolved.ToString());
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "Vendor parking resolution succeeded. vendor_system_id={VendorSystemId} parking_session_id={ParkingSessionId} tariff_snapshot_id={TariffSnapshotId} lookup_outcome={LookupOutcome}",
            session.VendorProviderCode,
            parkingSessionId,
            tariffSnapshotId,
            ResolveVendorParkingOutcome.Resolved);

        return ResolveVendorParkingResult.Resolved(
            centralSession,
            tariffSnapshot,
            sessionResponse.CorrelationId,
            session.VendorProviderCode);
    }

    private ResolveVendorParkingResult CompleteFailure(
        Activity? activity,
        ResolveVendorParkingOutcome outcome,
        string errorCode,
        bool retryable,
        Guid correlationId,
        string? vendorSystemId)
    {
        activity?.SetTag("vendor_system_id", vendorSystemId);
        activity?.SetTag("lookup.outcome", outcome.ToString());
        activity?.SetTag("lookup.error_code", errorCode);
        activity?.SetTag("lookup.retryable", retryable);
        activity?.SetStatus(outcome == ResolveVendorParkingOutcome.SessionNotFound ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

        if (outcome is ResolveVendorParkingOutcome.MalformedVendorResponse or ResolveVendorParkingOutcome.RetryableUnavailable)
        {
            _metrics.ExceptionObserved(outcome.ToString(), "RESOLVE_VENDOR_PARKING");
        }

        _logger.LogWarning(
            "Vendor parking resolution completed without a payable session. vendor_system_id={VendorSystemId} lookup_outcome={LookupOutcome} error_code={ErrorCode} retryable={Retryable}",
            vendorSystemId,
            outcome,
            errorCode,
            retryable);

        return ResolveVendorParkingResult.Failed(outcome, errorCode, retryable, correlationId, vendorSystemId);
    }

    private static ResolveVendorParkingOutcome MapOutcome(VendorParkingLookupStatus status)
    {
        return status switch
        {
            VendorParkingLookupStatus.NotFound => ResolveVendorParkingOutcome.SessionNotFound,
            VendorParkingLookupStatus.UnavailableRetryable => ResolveVendorParkingOutcome.RetryableUnavailable,
            VendorParkingLookupStatus.AdapterError => ResolveVendorParkingOutcome.MalformedVendorResponse,
            VendorParkingLookupStatus.ValidationError => ResolveVendorParkingOutcome.InvalidRequest,
            VendorParkingLookupStatus.VendorRejected => ResolveVendorParkingOutcome.VendorRejected,
            _ => ResolveVendorParkingOutcome.MalformedVendorResponse
        };
    }

    private static bool TryValidateSession(VendorParkingSessionDto? session, out VendorParkingSessionDto validSession)
    {
        validSession = session!;
        return session is not null &&
            !string.IsNullOrWhiteSpace(session.VendorProviderCode) &&
            !string.IsNullOrWhiteSpace(session.VendorSessionReference) &&
            !string.IsNullOrWhiteSpace(session.PlateNumber) &&
            session.EntryTime != default;
    }

    private static bool TryValidateQuote(VendorTariffQuoteDto? quote)
    {
        return quote is not null &&
            quote.AmountMinor >= 0 &&
            !string.IsNullOrWhiteSpace(quote.Currency) &&
            quote.CalculatedAt != default;
    }

    private static string ResolveIdentifierType(ResolveVendorParkingCommand command)
    {
        return string.IsNullOrWhiteSpace(command.PlateNumber) ? "TICKET" : "PLATE";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
