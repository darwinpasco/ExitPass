using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Maps existing GateAuthorizationConsumed events into canonical processing and vendor-neutral command records.
/// </summary>
public sealed class GateCommandCreationService : IGateCommandCreationService
{
    /// <summary>
    /// Vendor-neutral command type for the consumed authorization handoff.
    /// </summary>
    public const string OpenGateCommandType = "OPEN_GATE";

    private readonly IGateCommandCreationRepository _repository;
    private readonly ISystemClock _systemClock;

    /// <summary>
    /// Creates a gate command creation service.
    /// </summary>
    public GateCommandCreationService(
        IGateCommandCreationRepository repository,
        ISystemClock systemClock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
    }

    /// <inheritdoc />
    public async Task<GateCommandCreationResult> CreateFromConsumedEventAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.EventType, IntegrationEventTypes.GateAuthorizationConsumed, StringComparison.Ordinal))
        {
            throw new GateCommandCreationRejectedException(
                "UNSUPPORTED_EVENT_TYPE",
                "Only GateAuthorizationConsumed events can create gate commands.");
        }

        if (envelope.EventId == Guid.Empty)
        {
            throw new GateCommandCreationRejectedException("EVENT_ID_REQUIRED", "Event id is required.");
        }

        if (envelope.Payload is not GateAuthorizationConsumedPayload payload)
        {
            throw new GateCommandCreationRejectedException(
                "INVALID_EVENT_PAYLOAD",
                "GateAuthorizationConsumed payload is required.");
        }

        if (payload.GateAuthorizationConsumptionId is not { } consumptionId || consumptionId == Guid.Empty)
        {
            throw new GateCommandCreationRejectedException(
                "GATE_AUTHORIZATION_CONSUMPTION_ID_REQUIRED",
                "Gate authorization consumption id is required.");
        }

        if (payload.ExitAuthorizationId == Guid.Empty)
        {
            throw new GateCommandCreationRejectedException(
                "EXIT_AUTHORIZATION_ID_REQUIRED",
                "Exit authorization id is required.");
        }

        var parkingSessionId = Require(payload.ParkingSessionId, "PARKING_SESSION_ID_REQUIRED", "Parking session id is required.");
        var paymentAttemptId = Require(payload.PaymentAttemptId, "PAYMENT_ATTEMPT_ID_REQUIRED", "Payment attempt id is required.");
        var tariffSnapshotId = Require(payload.TariffSnapshotId, "TARIFF_SNAPSHOT_ID_REQUIRED", "Tariff snapshot id is required.");

        if (payload.CorrelationId == Guid.Empty)
        {
            throw new GateCommandCreationRejectedException("CORRELATION_ID_REQUIRED", "Correlation id is required.");
        }

        if (payload.ConsumedAtUtc == default)
        {
            throw new GateCommandCreationRejectedException("CONSUMED_AT_REQUIRED", "Consumed-at timestamp is required.");
        }

        var request = new GateCommandCreationRequest(
            EventId: envelope.EventId,
            EventType: envelope.EventType,
            EventRef: $"central-pms://integration-events/{envelope.EventId:N}",
            ProcessingKey: consumptionId,
            GateAuthorizationConsumptionId: consumptionId,
            ExitAuthorizationId: payload.ExitAuthorizationId,
            ParkingSessionId: parkingSessionId,
            PaymentAttemptId: paymentAttemptId,
            TariffSnapshotId: tariffSnapshotId,
            GateDeviceId: payload.GateDeviceId,
            ServiceIdentityId: null,
            LaneId: payload.LaneId,
            SiteId: payload.SiteId,
            VendorSystemId: payload.VendorSystemId,
            ConsumedAt: payload.ConsumedAtUtc,
            CorrelationId: payload.CorrelationId,
            CommandType: OpenGateCommandType,
            RequestedAt: _systemClock.UtcNow);

        return await _repository.CreateOrReuseAsync(request, cancellationToken);
    }

    private static Guid Require(Guid? value, string errorCode, string message)
    {
        if (value is not { } required || required == Guid.Empty)
        {
            throw new GateCommandCreationRejectedException(errorCode, message);
        }

        return required;
    }
}
