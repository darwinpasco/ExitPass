using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Thin APT-facing payable-basis readiness facade over shared Central PMS vendor parking resolution.
/// </summary>
public sealed class AptPayableBasisReadinessService : IAptPayableBasisReadinessService
{
    private const string ReferenceTypeTicket = "TICKET";
    private const string ReferenceTypePlate = "PLATE";
    private const string SupportedCurrency = "PHP";

    private readonly IResolveVendorParkingUseCase _vendorResolution;
    private readonly IParkingSessionReadRepository _parkingSessions;
    private readonly ITariffSnapshotReadRepository _tariffSnapshots;
    private readonly ITerminalCashPayableBasisEligibilityReader _terminalCashEligibility;
    private readonly ISalesInvoiceProfileAdministrationService _salesInvoiceReadiness;

    public AptPayableBasisReadinessService(
        IResolveVendorParkingUseCase vendorResolution,
        IParkingSessionReadRepository parkingSessions,
        ITariffSnapshotReadRepository tariffSnapshots,
        ITerminalCashPayableBasisEligibilityReader terminalCashEligibility,
        ISalesInvoiceProfileAdministrationService salesInvoiceReadiness)
    {
        _vendorResolution = vendorResolution;
        _parkingSessions = parkingSessions;
        _tariffSnapshots = tariffSnapshots;
        _terminalCashEligibility = terminalCashEligibility;
        _salesInvoiceReadiness = salesInvoiceReadiness;
    }

    public async Task<AptPayableBasisReadinessResult> ResolveAsync(
        AptPayableBasisResolveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var correlationId = ResolveCorrelationId(request.CorrelationId);
        var validation = ValidateResolveRequest(request, correlationId);
        if (validation is not null)
        {
            return validation;
        }

        var resolved = await _vendorResolution.ExecuteAsync(
            new ResolveVendorParkingCommand
            {
                SiteGroupId = request.SiteGroupId.Trim(),
                SiteId = request.SiteId.Trim(),
                VendorSystemId = request.VendorSystemId.Trim(),
                PlateNumber = Normalize(request.PlateNumber),
                TicketReference = Normalize(request.TicketReference),
                CorrelationId = correlationId
            },
            cancellationToken);

        return resolved.Outcome == ResolveVendorParkingOutcome.Resolved
            ? await BuildReadinessResultAsync(
                operation: "RESOLVE",
                revalidationOutcome: null,
                resolved,
                request.TerminalId,
                ParseRequiredGuid(request.SitePosServerId, nameof(request.SitePosServerId)),
                expectedAmountMinorUnits: null,
                expectedCurrency: null,
                forceNotReadyCode: null,
                cancellationToken)
            : MapResolutionFailure(resolved, correlationId);
    }

    public async Task<AptPayableBasisReadinessResult> RevalidateAsync(
        AptPayableBasisRevalidateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var correlationId = ResolveCorrelationId(request.CorrelationId);
        var validation = ValidateRevalidateRequest(request, correlationId);
        if (validation is not null)
        {
            return validation;
        }

        var parkingSessionId = ParseRequiredGuid(request.ParkingSessionId, nameof(request.ParkingSessionId));
        var previousTariffSnapshotId = ParseRequiredGuid(request.TariffSnapshotId, nameof(request.TariffSnapshotId));
        var sitePosServerId = ParseRequiredGuid(request.SitePosServerId, nameof(request.SitePosServerId));

        var storedSession = await _parkingSessions.GetByIdAsync(parkingSessionId, cancellationToken);
        if (storedSession is null)
        {
            return Failure("SESSION_NOT_FOUND", "Parking session was not found.", 404, false, correlationId);
        }

        var ticketReference = Normalize(request.TicketReference) ?? storedSession.TicketNumber;
        var plateNumber = Normalize(request.PlateNumber) ?? storedSession.PlateNumber;
        if (string.IsNullOrWhiteSpace(ticketReference) && string.IsNullOrWhiteSpace(plateNumber))
        {
            return Failure(
                "REVALIDATION_REFERENCE_UNAVAILABLE",
                "Central PMS could not recover a safe ticket or plate reference for revalidation.",
                409,
                false,
                correlationId);
        }

        var previousTariff = await _tariffSnapshots.GetByIdAsync(previousTariffSnapshotId, cancellationToken);
        var previousTariffExpired = previousTariff is null ||
            previousTariff.SnapshotStatus != TariffSnapshotStatus.Active ||
            previousTariff.ExpiresAt <= DateTimeOffset.UtcNow ||
            previousTariff.ConsumedByPaymentAttemptId is not null;

        var resolved = await _vendorResolution.ExecuteAsync(
            new ResolveVendorParkingCommand
            {
                SiteGroupId = request.SiteGroupId.Trim(),
                SiteId = request.SiteId.Trim(),
                VendorSystemId = string.IsNullOrWhiteSpace(request.VendorSystemId)
                    ? storedSession.VendorSystemCode
                    : request.VendorSystemId.Trim(),
                PlateNumber = plateNumber,
                TicketReference = string.IsNullOrWhiteSpace(plateNumber) ? ticketReference : null,
                CorrelationId = correlationId
            },
            cancellationToken);

        if (resolved.Outcome != ResolveVendorParkingOutcome.Resolved)
        {
            return MapResolutionFailure(resolved, correlationId);
        }

        var currentAmount = ToMinorUnits(resolved.TariffSnapshot!.NetPayable);
        var currentCurrency = resolved.TariffSnapshot.CurrencyCode.Trim().ToUpperInvariant();
        var amountChanged = currentAmount != request.ExpectedAmountMinorUnits ||
            !string.Equals(currentCurrency, request.ExpectedCurrency.Trim(), StringComparison.OrdinalIgnoreCase) ||
            resolved.TariffSnapshot.TariffSnapshotId != previousTariffSnapshotId;

        var responseResult = await BuildReadinessResultAsync(
            operation: "REVALIDATE",
            revalidationOutcome: null,
            resolved,
            request.TerminalId,
            sitePosServerId,
            request.ExpectedAmountMinorUnits,
            request.ExpectedCurrency,
            forceNotReadyCode: null,
            cancellationToken);

        if (!responseResult.Succeeded || responseResult.Response is null)
        {
            return responseResult;
        }

        var outcome = ResolveRevalidationOutcome(
            responseResult.Response,
            amountChanged,
            previousTariffExpired);
        var forceNotReadyCode = outcome == AptPayableBasisRevalidationOutcomes.PassedUnchanged
            ? null
            : outcome;

        return await BuildReadinessResultAsync(
            operation: "REVALIDATE",
            revalidationOutcome: outcome,
            resolved,
            request.TerminalId,
            sitePosServerId,
            request.ExpectedAmountMinorUnits,
            request.ExpectedCurrency,
            forceNotReadyCode,
            cancellationToken);
    }

    private async Task<AptPayableBasisReadinessResult> BuildReadinessResultAsync(
        string operation,
        string? revalidationOutcome,
        ResolveVendorParkingResult resolved,
        string terminalId,
        Guid sitePosServerId,
        long? expectedAmountMinorUnits,
        string? expectedCurrency,
        string? forceNotReadyCode,
        CancellationToken cancellationToken)
    {
        var session = resolved.ParkingSession!;
        var tariff = resolved.TariffSnapshot!;
        var correlationId = resolved.CorrelationId;
        var siteGroupId = Guid.Parse(session.SiteGroupId);
        var siteId = Guid.Parse(session.SiteId);
        var amountMinorUnits = ToMinorUnits(tariff.NetPayable);
        var currency = tariff.CurrencyCode.Trim().ToUpperInvariant();

        var sessionDimension = SessionReadiness(session);
        var tariffDimension = TariffReadiness(tariff);
        var terminalCash = await _terminalCashEligibility.EvaluateAsync(
            new TerminalCashPayableBasisEligibilityRequest(
                session.ParkingSessionId,
                tariff.TariffSnapshotId,
                siteGroupId,
                siteId,
                terminalId,
                expectedAmountMinorUnits ?? amountMinorUnits,
                expectedCurrency ?? currency,
                DateTimeOffset.UtcNow),
            cancellationToken);
        var terminalCashDimension = ToDimension("terminalCashAvailability", terminalCash);
        var paymentDimension = PaymentReadiness(terminalCash);
        var salesInvoiceDimension = await SalesInvoiceReadinessAsync(
            siteId,
            sitePosServerId,
            correlationId,
            cancellationToken);
        var fiscalDimension = FiscalReadiness(salesInvoiceDimension);

        var dimensions = new[]
        {
            sessionDimension,
            tariffDimension,
            paymentDimension,
            terminalCashDimension,
            salesInvoiceDimension,
            fiscalDimension
        };

        var blockingCodes = dimensions
            .Select(dimension => dimension.BlockingReasonCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(forceNotReadyCode))
        {
            blockingCodes.Add(forceNotReadyCode);
        }

        var ready = dimensions.All(dimension => dimension.Ready) &&
            string.IsNullOrWhiteSpace(forceNotReadyCode);
        var retryable = dimensions.Any(dimension => dimension.Retryable);
        var cashReadiness = ready
            ? AptPayableBasisReadinessStatuses.Ready
            : AptPayableBasisReadinessStatuses.Blocked;

        var response = new AptPayableBasisReadinessResponse(
            operation,
            revalidationOutcome,
            session.ParkingSessionId,
            tariff.TariffSnapshotId,
            siteGroupId,
            siteId,
            sitePosServerId,
            terminalId.Trim(),
            resolved.SiteGroupName,
            resolved.SiteName,
            session.TicketNumber,
            session.PlateNumber,
            session.EntryTimestamp,
            session.SessionStatus.ToString(),
            resolved.PaymentStatus ?? "Unknown",
            amountMinorUnits,
            currency,
            tariff.CalculatedAt,
            tariff.ExpiresAt,
            tariff.ExpiresAt,
            resolved.VendorSystemId ?? session.VendorSystemCode,
            dimensions,
            sessionDimension.Status,
            tariffDimension.Status,
            paymentDimension.Status,
            terminalCashDimension.Status,
            fiscalDimension.Status,
            salesInvoiceDimension.Status,
            cashReadiness,
            ready,
            blockingCodes,
            retryable,
            ready ? "READY_FOR_CASH_ACCEPTANCE" : "CASH_ACCEPTANCE_BLOCKED",
            correlationId);

        return new AptPayableBasisReadinessResult(true, response, null, null, 200, retryable, correlationId);
    }

    private static AptReadinessDimensionDto SessionReadiness(ParkingSession session)
    {
        return session.IsEligibleForPaymentAttempt()
            ? Ready("sessionReadiness", "Session is active and payable.")
            : Blocked("sessionReadiness", "SESSION_NOT_PAYABLE", "Session is not payable.", retryable: false);
    }

    private static AptReadinessDimensionDto TariffReadiness(TariffSnapshot tariff)
    {
        if (tariff.SnapshotStatus != TariffSnapshotStatus.Active ||
            tariff.ExpiresAt <= DateTimeOffset.UtcNow ||
            tariff.ConsumedByPaymentAttemptId is not null)
        {
            return Blocked("tariffReadiness", "STALE_TARIFF", "Tariff snapshot is stale or expired.", retryable: false);
        }

        return tariff.NetPayable >= 0 && string.Equals(tariff.CurrencyCode.Trim(), SupportedCurrency, StringComparison.OrdinalIgnoreCase)
            ? Ready("tariffReadiness", "Tariff is current.")
            : Blocked("tariffReadiness", "UNSUPPORTED_CURRENCY", "Currency is not supported for terminal cash.", retryable: false);
    }

    private static AptReadinessDimensionDto PaymentReadiness(TerminalCashPayableBasisEligibility terminalCash)
    {
        return string.Equals(terminalCash.BlockingReasonCode, "PAYMENT_ALREADY_FINAL", StringComparison.OrdinalIgnoreCase)
            ? Blocked("paymentEligibility", "PAYMENT_ALREADY_FINAL", "Parking session already has a final payment.", retryable: false)
            : Ready("paymentEligibility", "No final payment is recorded for this session.");
    }

    private async Task<AptReadinessDimensionDto> SalesInvoiceReadinessAsync(
        Guid siteId,
        Guid sitePosServerId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (sitePosServerId == Guid.Empty)
        {
            return Blocked(
                "salesInvoiceConfigurationReadiness",
                "SITE_POS_SERVER_NOT_CONFIGURED",
                "Site POS Server is not configured.",
                retryable: false);
        }

        var result = await _salesInvoiceReadiness.GetEffectiveReadinessAsync(
            new ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest(siteId, sitePosServerId, DateTimeOffset.UtcNow),
            new ManagementPlatformPosServerAdminRequestContext(correlationId),
            cancellationToken);

        if (result.Succeeded && result.Value is not null)
        {
            return result.Value.IsComplete &&
                string.Equals(result.Value.ResolutionStatus, ManagementPlatformSalesInvoiceProfileReadinessStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                ? Ready("salesInvoiceConfigurationReadiness", "Sales Invoice configuration is ready.")
                : Blocked(
                    "salesInvoiceConfigurationReadiness",
                    "SALES_INVOICE_CONFIGURATION_NOT_READY",
                    "Sales Invoice configuration is incomplete or unsupported.",
                    retryable: false);
        }

        var retryable = result.Outcome is PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable
            or PosServerSalesInvoiceProfileAdminOutcome.Timeout
            or PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure
            or PosServerSalesInvoiceProfileAdminOutcome.Throttled;

        var code = result.Outcome switch
        {
            PosServerSalesInvoiceProfileAdminOutcome.Disabled => "SALES_INVOICE_CONFIGURATION_UNAVAILABLE",
            PosServerSalesInvoiceProfileAdminOutcome.InvalidConfiguration => "UNSUPPORTED_CONFIGURATION",
            PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest => "UNSUPPORTED_CONFIGURATION",
            _ when retryable => "FISCAL_PATH_UNAVAILABLE",
            _ => "SALES_INVOICE_CONFIGURATION_NOT_READY"
        };

        return Blocked(
            "salesInvoiceConfigurationReadiness",
            code,
            "Sales Invoice configuration readiness could not be verified.",
            retryable);
    }

    private static AptReadinessDimensionDto FiscalReadiness(AptReadinessDimensionDto salesInvoice)
    {
        return salesInvoice.Ready
            ? Ready("fiscalReadiness", "Fiscal path readiness is acceptable for pre-cash validation.")
            : new AptReadinessDimensionDto(
                "fiscalReadiness",
                salesInvoice.Status,
                false,
                salesInvoice.BlockingReasonCode switch
                {
                    "SITE_POS_SERVER_NOT_CONFIGURED" => "SITE_POS_SERVER_NOT_CONFIGURED",
                    "SALES_INVOICE_CONFIGURATION_NOT_READY" => "SALES_INVOICE_CONFIGURATION_INCOMPLETE",
                    "FISCAL_PATH_UNAVAILABLE" => "FISCAL_PATH_UNAVAILABLE",
                    "UNSUPPORTED_CONFIGURATION" => "UNSUPPORTED_CONFIGURATION",
                    _ => "UNKNOWN"
                },
                salesInvoice.Retryable,
                "Fiscal path is not ready for cash acceptance.");
    }

    private static string ResolveRevalidationOutcome(
        AptPayableBasisReadinessResponse response,
        bool amountChanged,
        bool previousTariffExpired)
    {
        if (amountChanged)
        {
            return AptPayableBasisRevalidationOutcomes.AmountChanged;
        }

        if (previousTariffExpired)
        {
            return AptPayableBasisRevalidationOutcomes.TariffExpired;
        }

        if (response.BlockingReasonCodes.Contains("PAYMENT_ALREADY_FINAL", StringComparer.OrdinalIgnoreCase))
        {
            return AptPayableBasisRevalidationOutcomes.SessionAlreadyPaid;
        }

        if (response.BlockingReasonCodes.Contains("SESSION_NOT_PAYABLE", StringComparer.OrdinalIgnoreCase))
        {
            return AptPayableBasisRevalidationOutcomes.SessionNotPayable;
        }

        if (response.BlockingReasonCodes.Contains("CASH_PAYMENT_RAIL_NOT_CONFIGURED", StringComparer.OrdinalIgnoreCase))
        {
            return AptPayableBasisRevalidationOutcomes.TerminalCashUnavailable;
        }

        if (response.BlockingReasonCodes.Any(code => code.Contains("FISCAL", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("SALES_INVOICE", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("SITE_POS_SERVER", StringComparison.OrdinalIgnoreCase)))
        {
            return AptPayableBasisRevalidationOutcomes.FiscalReadinessFailed;
        }

        return response.ReadyForCashAcceptance
            ? AptPayableBasisRevalidationOutcomes.PassedUnchanged
            : AptPayableBasisRevalidationOutcomes.RevalidationFailed;
    }

    private static AptPayableBasisReadinessResult? ValidateResolveRequest(
        AptPayableBasisResolveRequest request,
        Guid correlationId)
    {
        if (HasUnsafeText(request.SiteGroupId, request.SiteId, request.SitePosServerId, request.TerminalId, request.VendorSystemId, request.ReferenceType, request.TicketReference, request.PlateNumber))
        {
            return Failure("INVALID_REQUEST", "APT payable-basis request contains invalid text.", 400, false, correlationId);
        }

        if (!TryParseGuid(request.SiteGroupId, out _) ||
            !TryParseGuid(request.SiteId, out _) ||
            !TryParseGuid(request.SitePosServerId, out _))
        {
            return Failure("INVALID_REQUEST", "APT payable-basis request contains malformed identifiers.", 400, false, correlationId);
        }

        var type = NormalizeCode(request.ReferenceType);
        var hasTicket = !string.IsNullOrWhiteSpace(request.TicketReference);
        var hasPlate = !string.IsNullOrWhiteSpace(request.PlateNumber);
        if (type is not ReferenceTypeTicket and not ReferenceTypePlate ||
            type == ReferenceTypeTicket && (!hasTicket || hasPlate) ||
            type == ReferenceTypePlate && (!hasPlate || hasTicket))
        {
            return Failure("INVALID_REQUEST", "Exactly one supported ticket or plate reference is required.", 400, false, correlationId);
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId) || string.IsNullOrWhiteSpace(request.VendorSystemId))
        {
            return Failure("INVALID_REQUEST", "Terminal and vendor context are required.", 400, false, correlationId);
        }

        return null;
    }

    private static AptPayableBasisReadinessResult? ValidateRevalidateRequest(
        AptPayableBasisRevalidateRequest request,
        Guid correlationId)
    {
        if (HasUnsafeText(request.ParkingSessionId, request.TariffSnapshotId, request.SiteGroupId, request.SiteId, request.SitePosServerId, request.TerminalId, request.VendorSystemId, request.TicketReference, request.PlateNumber, request.ExpectedCurrency))
        {
            return Failure("INVALID_REQUEST", "APT payable-basis revalidation request contains invalid text.", 400, false, correlationId);
        }

        if (!TryParseGuid(request.ParkingSessionId, out _) ||
            !TryParseGuid(request.TariffSnapshotId, out _) ||
            !TryParseGuid(request.SiteGroupId, out _) ||
            !TryParseGuid(request.SiteId, out _) ||
            !TryParseGuid(request.SitePosServerId, out _))
        {
            return Failure("INVALID_REQUEST", "APT payable-basis revalidation request contains malformed identifiers.", 400, false, correlationId);
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId) ||
            request.ExpectedAmountMinorUnits < 0 ||
            string.IsNullOrWhiteSpace(request.ExpectedCurrency))
        {
            return Failure("INVALID_REQUEST", "APT payable-basis revalidation request is incomplete.", 400, false, correlationId);
        }

        return null;
    }

    private static AptPayableBasisReadinessResult MapResolutionFailure(
        ResolveVendorParkingResult result,
        Guid fallbackCorrelationId)
    {
        var status = result.Outcome switch
        {
            ResolveVendorParkingOutcome.InvalidRequest => 400,
            ResolveVendorParkingOutcome.SessionNotFound => 404,
            ResolveVendorParkingOutcome.AmbiguousMatch => 409,
            ResolveVendorParkingOutcome.VendorRejected => 409,
            ResolveVendorParkingOutcome.MalformedVendorResponse => 502,
            ResolveVendorParkingOutcome.RetryableUnavailable => 503,
            ResolveVendorParkingOutcome.ProjectionSnapshotAvailable => 503,
            _ => 502
        };

        var code = result.ErrorCode ?? result.Outcome.ToString().ToUpperInvariant();
        return Failure(code, MapResolutionMessage(result.Outcome), status, result.Retryable, result.CorrelationId == Guid.Empty ? fallbackCorrelationId : result.CorrelationId);
    }

    private static string MapResolutionMessage(ResolveVendorParkingOutcome outcome) =>
        outcome switch
        {
            ResolveVendorParkingOutcome.SessionNotFound => "Parking session was not found.",
            ResolveVendorParkingOutcome.AmbiguousMatch => "Parking session lookup returned multiple matches.",
            ResolveVendorParkingOutcome.RetryableUnavailable => "Vendor PMS is temporarily unavailable.",
            ResolveVendorParkingOutcome.ProjectionSnapshotAvailable => "Live vendor lookup is temporarily unavailable.",
            ResolveVendorParkingOutcome.MalformedVendorResponse => "Vendor parking response could not be safely mapped.",
            ResolveVendorParkingOutcome.InvalidRequest => "APT payable-basis request is invalid.",
            ResolveVendorParkingOutcome.VendorRejected => "Parking session is not payable.",
            _ => "APT payable-basis resolution failed."
        };

    private static AptReadinessDimensionDto ToDimension(
        string name,
        TerminalCashPayableBasisEligibility eligibility) =>
        eligibility.Ready
            ? Ready(name, eligibility.Message)
            : Blocked(name, eligibility.BlockingReasonCode ?? eligibility.Status, eligibility.Message, eligibility.Retryable);

    private static AptReadinessDimensionDto Ready(string name, string message) =>
        new(name, AptPayableBasisReadinessStatuses.Ready, true, null, false, message);

    private static AptReadinessDimensionDto Blocked(
        string name,
        string code,
        string message,
        bool retryable) =>
        new(name, AptPayableBasisReadinessStatuses.Blocked, false, code, retryable, message);

    private static AptPayableBasisReadinessResult Failure(
        string code,
        string message,
        int httpStatus,
        bool retryable,
        Guid correlationId) =>
        new(false, null, code, message, httpStatus, retryable, correlationId);

    private static Guid ResolveCorrelationId(Guid candidate) =>
        candidate == Guid.Empty ? Guid.NewGuid() : candidate;

    private static bool TryParseGuid(string? value, out Guid parsed) =>
        Guid.TryParse(value, out parsed) && parsed != Guid.Empty;

    private static Guid ParseRequiredGuid(string value, string name) =>
        TryParseGuid(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"{name} must be a valid GUID.", name);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static bool HasUnsafeText(params string?[] values) =>
        values.Any(value => value is not null && value.Any(char.IsControl));

    private static long ToMinorUnits(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
