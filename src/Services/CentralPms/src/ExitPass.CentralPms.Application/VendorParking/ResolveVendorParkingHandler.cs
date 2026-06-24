using System.Diagnostics;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
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
        ISystemClock? clock = null)
    {
        _vendorClient = vendorClient;
        _persistence = persistence;
        _eventPublisher = eventPublisher;
        _metrics = metrics;
        _logger = logger;
        _projectionLookupService = projectionLookupService;
        _projectionOptions = projectionOptions?.Value ?? new VendorSessionProjectionOptions();
        _clock = clock;
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
            if (sessionResponse.Status == VendorParkingLookupStatus.UnavailableRetryable)
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

        PersistVendorParkingResolutionResult persisted;
        try
        {
            persisted = await _persistence.PersistAsync(
                new PersistVendorParkingResolutionRequest
                {
                    ParkingSession = centralSession,
                    TariffSnapshot = tariffSnapshot,
                    RequestedVendorSystemId = ParseOptionalGuid(command.VendorSystemId),
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
        var lookup = await _projectionLookupService.LookupAsync(
            new VendorSessionProjectionLookupQuery(
                CardNum: Normalize(command.TicketReference),
                PlateLicense: Normalize(command.PlateNumber),
                SiteId: ParseOptionalGuid(command.SiteId),
                SiteGroupId: ParseOptionalGuid(command.SiteGroupId),
                ParkingLotIndexCode: null,
                requestedAt,
                sessionResponse.CorrelationId),
            cancellationToken);

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
            "Vendor parking live lookup unavailable; returning non-authoritative projection snapshot metadata. projection_id={ProjectionId} freshness_age_seconds={FreshnessAgeSeconds}",
            lookup.Projection.VendorSessionProjectionId,
            lookup.FreshnessAge.Value.TotalSeconds);

        return ResolveVendorParkingResult.ProjectionSnapshot(
            lookup,
            "VENDOR_UNAVAILABLE_PROJECTION_SNAPSHOT_AVAILABLE",
            sessionResponse.CorrelationId,
            sessionResponse.Session?.VendorProviderCode ?? command.VendorSystemId);
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
