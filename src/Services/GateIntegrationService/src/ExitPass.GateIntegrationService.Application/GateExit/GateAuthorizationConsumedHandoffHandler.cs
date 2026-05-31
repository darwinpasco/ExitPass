using System.Diagnostics;

namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Processes Central PMS GateAuthorizationConsumed handoffs at the Gate Integration Service boundary.
/// </summary>
public sealed class GateAuthorizationConsumedHandoffHandler : IGateAuthorizationConsumedHandoffHandler
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.GateIntegrationService.Application.GateExit");

    private readonly IConsumedAuthorizationGateActionAdapter _adapter;
    private readonly IGateAuthorizationConsumedProcessingRecorder _recorder;
    private readonly IGateAuthorizationConsumedScopeValidator _scopeValidator;

    /// <summary>
    /// Creates a handler for consumed authorization handoffs.
    /// </summary>
    public GateAuthorizationConsumedHandoffHandler(
        IConsumedAuthorizationGateActionAdapter adapter,
        IGateAuthorizationConsumedProcessingRecorder recorder,
        IGateAuthorizationConsumedScopeValidator scopeValidator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _scopeValidator = scopeValidator ?? throw new ArgumentNullException(nameof(scopeValidator));
    }

    /// <inheritdoc />
    public async Task<GateAuthorizationConsumedProcessingResult> HandleAsync(
        ProcessGateAuthorizationConsumedCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Handoff);

        using var activity = ActivitySource.StartActivity(
            "ProcessGateAuthorizationConsumedHandoff",
            ActivityKind.Consumer);

        Validate(command.Handoff);

        activity?.SetTag("event_id", command.Handoff.EventId);
        activity?.SetTag("exit_authorization_id", command.Handoff.ExitAuthorizationId);
        activity?.SetTag("gate_authorization_consumption_id", command.Handoff.GateAuthorizationConsumptionId);
        activity?.SetTag("parking_session_id", command.Handoff.ParkingSessionId);
        activity?.SetTag("payment_attempt_id", command.Handoff.PaymentAttemptId);
        activity?.SetTag("tariff_snapshot_id", command.Handoff.TariffSnapshotId);
        activity?.SetTag("gate_device_id", command.Handoff.GateDeviceId);
        activity?.SetTag("gate_device_identifier", command.Handoff.GateDeviceIdentifier);
        activity?.SetTag("lane_id", command.Handoff.LaneId);
        activity?.SetTag("site_id", command.Handoff.SiteId);
        activity?.SetTag("vendor_system_id", command.Handoff.VendorSystemId);
        activity?.SetTag("correlation_id", command.Handoff.CorrelationId);

        var existing = await _recorder.GetProcessedAsync(command.Handoff.EventId, cancellationToken);
        if (existing is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("result_code", "GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED");
            activity?.SetTag("adapter_invoked", false);
            activity?.SetTag("already_processed", true);

            return new GateAuthorizationConsumedProcessingResult(
                existing.EventId,
                existing.ExitAuthorizationId,
                existing.GateAuthorizationConsumptionId,
                existing.TariffSnapshotId,
                "GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED",
                AdapterInvoked: false,
                AlreadyProcessed: true,
                existing.ProcessedAtUtc);
        }

        var scope = await _scopeValidator.ValidateAsync(command.Handoff, cancellationToken);
        if (!scope.IsValid)
        {
            activity?.SetStatus(ActivityStatusCode.Error, scope.Message);
            activity?.SetTag("result_code", scope.ResultCode);
            activity?.SetTag("adapter_invoked", false);
            throw new GateAuthorizationConsumedHandoffException(scope.ResultCode, scope.Message);
        }

        await _adapter.ProcessConsumedAuthorizationAsync(command.Handoff, cancellationToken);

        var processedAtUtc = DateTimeOffset.UtcNow;
        var record = new GateAuthorizationConsumedProcessingRecord(
            command.Handoff.EventId,
            command.Handoff.ExitAuthorizationId,
            command.Handoff.GateAuthorizationConsumptionId,
            command.Handoff.TariffSnapshotId,
            "GATE_AUTHORIZATION_CONSUMED_PROCESSED",
            processedAtUtc);

        await _recorder.RecordProcessedAsync(record, cancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("result_code", record.ResultCode);
        activity?.SetTag("adapter_invoked", true);
        activity?.SetTag("already_processed", false);

        return new GateAuthorizationConsumedProcessingResult(
            record.EventId,
            record.ExitAuthorizationId,
            record.GateAuthorizationConsumptionId,
            record.TariffSnapshotId,
            record.ResultCode,
            AdapterInvoked: true,
            AlreadyProcessed: false,
            record.ProcessedAtUtc);
    }

    private static void Validate(GateAuthorizationConsumedHandoff handoff)
    {
        if (handoff.EventId == Guid.Empty && string.IsNullOrWhiteSpace(handoff.SourceEventRef))
        {
            throw Invalid("GATE_HANDOFF_EVENT_ID_REQUIRED", "EventId or SourceEventRef is required.");
        }

        if (handoff.ExitAuthorizationId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_EXIT_AUTHORIZATION_ID_REQUIRED", "ExitAuthorizationId is required.");
        }

        if (handoff.GateAuthorizationConsumptionId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_CONSUMPTION_ID_REQUIRED", "GateAuthorizationConsumptionId is required.");
        }

        if (handoff.ParkingSessionId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_PARKING_SESSION_ID_REQUIRED", "ParkingSessionId is required.");
        }

        if (handoff.PaymentAttemptId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_PAYMENT_ATTEMPT_ID_REQUIRED", "PaymentAttemptId is required.");
        }

        if (handoff.TariffSnapshotId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_TARIFF_SNAPSHOT_ID_REQUIRED", "TariffSnapshotId is required.");
        }

        if (!handoff.GateDeviceId.HasValue && string.IsNullOrWhiteSpace(handoff.GateDeviceIdentifier))
        {
            throw Invalid("GATE_HANDOFF_GATE_DEVICE_REQUIRED", "GateDeviceId or GateDeviceIdentifier is required.");
        }

        if (handoff.ConsumedAtUtc == default)
        {
            throw Invalid("GATE_HANDOFF_CONSUMED_AT_REQUIRED", "ConsumedAtUtc is required.");
        }

        if (handoff.CorrelationId == Guid.Empty)
        {
            throw Invalid("GATE_HANDOFF_CORRELATION_ID_REQUIRED", "CorrelationId is required.");
        }
    }

    private static GateAuthorizationConsumedHandoffException Invalid(string errorCode, string message) =>
        new(errorCode, message);
}
