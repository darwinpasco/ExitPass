using System.Diagnostics;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IVendorParkingResolutionPersistence _persistence;
    private readonly IIntegrationEventPublisher _eventPublisher;
    private readonly CentralPmsMetrics _metrics;
    private readonly ILogger<ResolveVendorParkingHandler> _logger;
    private readonly IVendorSessionProjectionLookupService? _projectionLookupService;
    private readonly VendorSessionProjectionOptions _projectionOptions;
    private readonly ISystemClock? _clock;
    private readonly IContinuityTariffCalculator? _continuityTariffCalculator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolveVendorParkingHandler"/> class.
    /// </summary>
    /// <param name="vendorClient">Provider-neutral Vendor PMS Adapter client.</param>
    /// <param name="persistence">Central PMS persistence boundary for resolved parking data.</param>
    /// <param name="eventPublisher">Integration event publisher for successful Central PMS state changes.</param>
    /// <param name="metrics">Shared Central PMS business metrics publisher.</param>
    /// <param name="logger">Application logger.</param>
    /// <param name="projectionLookupService">Optional projection lookup service for degraded-mode visibility.</param>
    /// <param name="projectionOptions">Projection scheduler/fallback options.</param>
    /// <param name="clock">Optional clock used for projection freshness checks.</param>
    public ResolveVendorParkingHandler(
        IVendorPmsParkingResolutionClient vendorClient,
        IVendorParkingResolutionPersistence persistence,
        IIntegrationEventPublisher eventPublisher,
        CentralPmsMetrics metrics,
        ILogger<ResolveVendorParkingHandler> logger,
        IVendorSessionProjectionLookupService? projectionLookupService = null,
        IOptions<VendorSessionProjectionOptions>? projectionOptions = null,
        ISystemClock? clock = null,
        IContinuityTariffCalculator? continuityTariffCalculator = null)
    {
        _vendorClient = vendorClient;
        _persistence = persistence;
        _eventPublisher = eventPublisher;
        _metrics = metrics;
        _logger = logger;
        _projectionLookupService = projectionLookupService;
        _projectionOptions = projectionOptions?.Value ?? new VendorSessionProjectionOptions();
        _clock = clock;
        _continuityTariffCalculator = continuityTariffCalculator;
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

        if (!Guid.TryParse(command.SiteId, out var routedSiteId) ||
            !Guid.TryParse(command.SiteGroupId, out var routedSiteGroupId) ||
            !Guid.TryParse(command.VendorSystemId, out var routedVendorSystemId))
        {
            return CompleteFailure(activity, ResolveVendorParkingOutcome.InvalidRequest,
                "INVALID_VENDOR_ROUTING_SCOPE", false, command.CorrelationId, command.VendorSystemId);
        }
        var routeContext = new VendorAdapterRequestContext(
            routedSiteId, routedSiteGroupId, routedVendorSystemId, Guid.Empty);

        var sessionResponse = await _vendorClient.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(
                Normalize(command.PlateNumber),
                Normalize(command.TicketReference),
                command.CorrelationId,
                routeContext),
            cancellationToken);

        if (sessionResponse.Status != VendorParkingLookupStatus.Found)
        {
            if (sessionResponse.Status is VendorParkingLookupStatus.UnavailableRetryable or VendorParkingLookupStatus.AdapterError)
            {
                var projectionFallback = await TryResolveProjectionFallbackAsync(
                    command,
                    sessionResponse,
                    cancellationToken);

                if (projectionFallback is not null)
                {
                    return projectionFallback;
                }
            }

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
                    command.CorrelationId,
                    routeContext),
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
        if (sessionResponse.AdapterContext is null ||
            sessionResponse.AdapterContext.SiteId != routedSiteId ||
            sessionResponse.AdapterContext.SiteGroupId != routedSiteGroupId ||
            sessionResponse.AdapterContext.VendorSystemId != routedVendorSystemId ||
            sessionResponse.AdapterContext.AdapterIdentityId == Guid.Empty)
        {
            return CompleteFailure(activity, ResolveVendorParkingOutcome.MalformedVendorResponse,
                "SITE_ADAPTER_RESPONSE_BINDING_MISMATCH", false, command.CorrelationId, command.VendorSystemId);
        }
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
            Normalize(session.TicketReference) ?? Normalize(command.TicketReference),
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

        PersistVendorParkingResolutionResult persisted;
        try
        {
            persisted = await _persistence.PersistAsync(
                new PersistVendorParkingResolutionRequest
                {
                    ParkingSession = centralSession,
                    TariffSnapshot = tariffSnapshot,
                    RequestedVendorSystemId = ParseOptionalGuid(command.VendorSystemId),
                    SourceAdapterIdentityId = sessionResponse.AdapterContext.AdapterIdentityId,
                    CorrelationId = sessionResponse.CorrelationId
                },
                cancellationToken);
        }
        catch (VendorParkingResolutionPersistenceException ex)
        {
            return CompleteFailure(
                activity,
                ResolveVendorParkingOutcome.MalformedVendorResponse,
                ex.ErrorCode,
                retryable: false,
                sessionResponse.CorrelationId,
                session.VendorProviderCode);
        }

        activity?.SetTag("vendor_system_id", session.VendorProviderCode);
        activity?.SetTag("parking_session_id", persisted.ParkingSession.ParkingSessionId);
        activity?.SetTag("tariff_snapshot_id", persisted.TariffSnapshot.TariffSnapshotId);
        activity?.SetTag("parking_session_reused", persisted.ParkingSessionWasReused);
        activity?.SetTag("tariff_snapshot_reused", persisted.TariffSnapshotWasReused);
        activity?.SetTag("lookup.outcome", ResolveVendorParkingOutcome.Resolved.ToString());
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "Vendor parking resolution succeeded. vendor_system_id={VendorSystemId} parking_session_id={ParkingSessionId} tariff_snapshot_id={TariffSnapshotId} lookup_outcome={LookupOutcome}",
            session.VendorProviderCode,
            persisted.ParkingSession.ParkingSessionId,
            persisted.TariffSnapshot.TariffSnapshotId,
            ResolveVendorParkingOutcome.Resolved);

        await _eventPublisher.PublishAsync(
            new IntegrationEventEnvelope
            {
                EventType = IntegrationEventTypes.VendorParkingResolved,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = sessionResponse.CorrelationId,
                AggregateId = persisted.ParkingSession.ParkingSessionId.ToString(),
                AggregateType = nameof(ParkingSession),
                Payload = new VendorParkingResolvedPayload
                {
                    ParkingSessionId = persisted.ParkingSession.ParkingSessionId,
                    TariffSnapshotId = persisted.TariffSnapshot.TariffSnapshotId,
                    SiteId = persisted.ParkingSession.SiteId,
                    SiteGroupId = persisted.ParkingSession.SiteGroupId,
                    VendorSystemId = persisted.VendorSystemId,
                    LookupReferenceType = ResolveIdentifierType(command).ToLowerInvariant(),
                    LookupOutcome = ResolveVendorParkingOutcome.Resolved.ToString(),
                    NetPayableMinorUnits = ToMinorUnits(persisted.TariffSnapshot.NetPayable),
                    Currency = persisted.TariffSnapshot.CurrencyCode,
                    TariffExpiresAt = persisted.TariffSnapshot.ExpiresAt
                }
            },
            cancellationToken);

        return ResolveVendorParkingResult.Resolved(
            persisted.ParkingSession,
            persisted.TariffSnapshot,
            sessionResponse.CorrelationId,
            persisted.VendorSystemId,
            ParkingDisplayNameSanitizer.ResolveSiteGroupName(persisted.SiteGroupName),
            ParkingDisplayNameSanitizer.ResolveSiteName(persisted.SiteName),
            persisted.PaymentStatus,
            persisted.EffectivePayableBasis);
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

    private async Task<ResolveVendorParkingResult?> TryResolveProjectionFallbackAsync(
        ResolveVendorParkingCommand command,
        VendorParkingSessionLookupResponse sessionResponse,
        CancellationToken cancellationToken)
    {
        if (!_projectionOptions.DegradedResolveFallbackEnabled || _projectionLookupService is null)
        {
            return null;
        }

        var requestedAt = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        VendorSessionProjectionLookupResult lookup;
        try
        {
            lookup = await _projectionLookupService.LookupAsync(
                new VendorSessionProjectionLookupQuery(
                    CardNum: Normalize(command.TicketReference),
                    PlateLicense: Normalize(command.PlateNumber),
                    SiteId: ParseOptionalGuid(command.SiteId),
                    SiteGroupId: ParseOptionalGuid(command.SiteGroupId),
                    ParkingLotIndexCode: null,
                    requestedAt,
                    sessionResponse.CorrelationId,
                    VendorSystemId: ParseOptionalGuid(command.VendorSystemId)),
                cancellationToken);
        }
        catch (VendorSessionProjectionLookupException exception)
        {
            _logger.LogWarning(
                "Vendor projection fallback was rejected safely. projection_error_code={ProjectionErrorCode} live_error_code={LiveErrorCode}",
                exception.ErrorCode,
                sessionResponse.ErrorCode);
            return null;
        }

        if (!lookup.Found || lookup.Projection is null)
        {
            return null;
        }

        var maxAge = _projectionOptions.EffectiveMaxProjectionAge();
        if (lookup.FreshnessAge is null || lookup.FreshnessAge > maxAge)
        {
            _logger.LogWarning(
                "Vendor projection fallback found stale snapshot and will not return it as usable continuity data. freshness_age_seconds={FreshnessAgeSeconds} max_age_seconds={MaxAgeSeconds}",
                lookup.FreshnessAge?.TotalSeconds,
                maxAge.TotalSeconds);
            return null;
        }

        _logger.LogWarning(
            "Vendor parking live lookup unavailable; projection fallback is eligible. projection_id={ProjectionId} freshness_age_seconds={FreshnessAgeSeconds}",
            lookup.Projection.VendorSessionProjectionId,
            lookup.FreshnessAge.Value.TotalSeconds);

        var continuity = await TryCreateContinuityPayableBasisAsync(
            command,
            sessionResponse,
            lookup,
            requestedAt,
            cancellationToken);
        if (continuity is not null)
        {
            return continuity;
        }

        return ResolveVendorParkingResult.ProjectionSession(
            lookup,
            sessionResponse.ErrorCode ?? sessionResponse.Status.ToString().ToUpperInvariant(),
            sessionResponse.CorrelationId,
            lookup.Projection.VendorSystemId?.ToString("D") ?? command.VendorSystemId);
    }

    private async Task<ResolveVendorParkingResult?> TryCreateContinuityPayableBasisAsync(
        ResolveVendorParkingCommand command,
        VendorParkingSessionLookupResponse liveResponse,
        VendorSessionProjectionLookupResult lookup,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken)
    {
        var projection = lookup.Projection;
        if (_continuityTariffCalculator is null || projection is null ||
            projection.ProjectionStatus != VendorSessionProjectionStatus.Active ||
            projection.SiteId is null || projection.SiteGroupId is null ||
            projection.VendorSystemId is null || projection.SourceAdapterIdentityId is null ||
            projection.EnterTime is null)
        {
            return null;
        }

        var calculation = await _continuityTariffCalculator.CalculateAsync(
            projection.SiteId.Value,
            projection.CanonicalVehicleTypeCode,
            projection.EnterTime.Value,
            calculatedAt,
            cancellationToken);
        if (!calculation.Success || calculation.AmountMinorUnits is null ||
            calculation.Currency is null || calculation.TariffVersionReference is null ||
            calculation.CalculatedAt is null || calculation.ExpiresAt is null)
        {
            _logger.LogWarning(
                "Continuity tariff was unavailable; returning projection-only session. projection_id={ProjectionId} failure_code={FailureCode}",
                projection.VendorSessionProjectionId,
                calculation.FailureCode);
            return null;
        }

        var vendorSessionReference = Normalize(projection.VendorRecordGuid) ??
            Normalize(projection.StableIdentityKey);
        if (vendorSessionReference is null)
        {
            return null;
        }

        var parkingSessionId = Guid.NewGuid();
        var parkingSession = ParkingSession.Rehydrate(
            parkingSessionId,
            projection.SiteGroupId.Value.ToString("D"),
            projection.SiteId.Value.ToString("D"),
            projection.VendorSystemId.Value.ToString("D"),
            vendorSessionReference,
            ResolveIdentifierType(command),
            Normalize(projection.PlateLicense),
            Normalize(projection.CardNum),
            projection.EnterTime.Value,
            ParkingSessionStatus.PaymentRequired);
        var amount = decimal.Divide(calculation.AmountMinorUnits.Value, 100m);
        var tariff = TariffSnapshot.Rehydrate(
            Guid.NewGuid(),
            parkingSessionId,
            TariffSnapshotSourceType.Base,
            amount,
            0m,
            0m,
            amount,
            calculation.Currency,
            amount,
            calculation.TariffVersionReference,
            null,
            calculation.CalculatedAt.Value,
            calculation.ExpiresAt.Value,
            TariffSnapshotStatus.Active,
            null,
            null);

        try
        {
            var persisted = await _persistence.PersistAsync(
                new PersistVendorParkingResolutionRequest
                {
                    ParkingSession = parkingSession,
                    TariffSnapshot = tariff,
                    RequestedVendorSystemId = projection.VendorSystemId,
                    SourceAdapterIdentityId = projection.SourceAdapterIdentityId,
                    TariffOrigin = VendorParkingTariffOrigin.ExitPassContinuity,
                    ReferencesAlreadyExist = true,
                    CanonicalVehicleTypeCode = calculation.VehicleTypeCode,
                    CorrelationId = liveResponse.CorrelationId
                },
                cancellationToken);

            await _eventPublisher.PublishAsync(
                new IntegrationEventEnvelope
                {
                    EventType = IntegrationEventTypes.VendorParkingResolved,
                    OccurredAtUtc = calculatedAt,
                    CorrelationId = liveResponse.CorrelationId,
                    AggregateId = persisted.ParkingSession.ParkingSessionId.ToString(),
                    AggregateType = nameof(ParkingSession),
                    Payload = new VendorParkingResolvedPayload
                    {
                        ParkingSessionId = persisted.ParkingSession.ParkingSessionId,
                        TariffSnapshotId = persisted.TariffSnapshot.TariffSnapshotId,
                        SiteId = persisted.ParkingSession.SiteId,
                        SiteGroupId = persisted.ParkingSession.SiteGroupId,
                        VendorSystemId = persisted.VendorSystemId,
                        LookupReferenceType = ResolveIdentifierType(command).ToLowerInvariant(),
                        LookupOutcome = ResolveVendorParkingOutcome.Resolved.ToString(),
                        NetPayableMinorUnits = ToMinorUnits(persisted.TariffSnapshot.NetPayable),
                        Currency = persisted.TariffSnapshot.CurrencyCode,
                        TariffExpiresAt = persisted.TariffSnapshot.ExpiresAt
                    }
                },
                cancellationToken);

            return ResolveVendorParkingResult.ContinuityResolved(
                persisted,
                lookup,
                liveResponse.ErrorCode ?? liveResponse.Status.ToString().ToUpperInvariant(),
                liveResponse.CorrelationId);
        }
        catch (VendorParkingResolutionPersistenceException exception)
        {
            _logger.LogWarning(
                "Continuity payable basis persistence failed safely. projection_id={ProjectionId} error_code={ErrorCode}",
                projection.VendorSessionProjectionId,
                exception.ErrorCode);
            return null;
        }
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
            VendorParkingLookupStatus.Ambiguous => ResolveVendorParkingOutcome.AmbiguousMatch,
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

    private static Guid? ParseOptionalGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
