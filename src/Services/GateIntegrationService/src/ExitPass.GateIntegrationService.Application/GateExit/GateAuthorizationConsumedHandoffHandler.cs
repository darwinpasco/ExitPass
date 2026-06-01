using System.Diagnostics;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Processes Central PMS GateAuthorizationConsumed handoffs at the Gate Integration Service boundary.
/// </summary>
public sealed class GateAuthorizationConsumedHandoffHandler : IGateAuthorizationConsumedHandoffHandler
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.GateIntegrationService.Application.GateExit");

    private readonly IConsumedAuthorizationGateActionAdapter _adapter;
    private readonly IGateCommandLifecycleRecorder _commandRecorder;
    private readonly IGateAuthorizationConsumedProcessingRecorder _recorder;
    private readonly IGateAuthorizationConsumedScopeValidator _scopeValidator;

    /// <summary>
    /// Creates a handler for consumed authorization handoffs.
    /// </summary>
    public GateAuthorizationConsumedHandoffHandler(
        IConsumedAuthorizationGateActionAdapter adapter,
        IGateCommandLifecycleRecorder commandRecorder,
        IGateAuthorizationConsumedProcessingRecorder recorder,
        IGateAuthorizationConsumedScopeValidator scopeValidator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _commandRecorder = commandRecorder ?? throw new ArgumentNullException(nameof(commandRecorder));
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

        var processing = await _recorder.BeginProcessingAsync(command.Handoff, cancellationToken);
        if (processing.AlreadyProcessed)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("result_code", "GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED");
            activity?.SetTag("adapter_invoked", false);
            activity?.SetTag("already_processed", true);

            return new GateAuthorizationConsumedProcessingResult(
                processing.Record.EventId,
                processing.Record.ExitAuthorizationId,
                processing.Record.GateAuthorizationConsumptionId,
                processing.Record.TariffSnapshotId,
                "GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED",
                AdapterInvoked: false,
                AlreadyProcessed: true,
                processing.Record.ProcessedAtUtc);
        }

        if (processing.AlreadyInProgress || !processing.CanInvokeAdapter)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("result_code", "GATE_AUTHORIZATION_CONSUMED_PROCESSING_IN_PROGRESS");
            activity?.SetTag("adapter_invoked", false);
            activity?.SetTag("already_processed", false);

            return new GateAuthorizationConsumedProcessingResult(
                processing.Record.EventId,
                processing.Record.ExitAuthorizationId,
                processing.Record.GateAuthorizationConsumptionId,
                processing.Record.TariffSnapshotId,
                "GATE_AUTHORIZATION_CONSUMED_PROCESSING_IN_PROGRESS",
                AdapterInvoked: false,
                AlreadyProcessed: false,
                processing.Record.ProcessedAtUtc);
        }

        var scope = await _scopeValidator.ValidateAsync(command.Handoff, cancellationToken);
        if (!scope.IsValid)
        {
            activity?.SetStatus(ActivityStatusCode.Error, scope.Message);
            activity?.SetTag("result_code", scope.ResultCode);
            activity?.SetTag("adapter_invoked", false);
            await _recorder.RecordFailedAsync(
                command.Handoff,
                scope.ResultCode,
                scope.Message,
                cancellationToken);
            throw new GateAuthorizationConsumedHandoffException(scope.ResultCode, scope.Message);
        }

        var gateCommand = await _commandRecorder.BeginCommandAsync(command.Handoff, cancellationToken);
        activity?.SetTag("gate_command_id", gateCommand.Command.CommandId);
        activity?.SetTag("gate_command_status", gateCommand.Command.CommandStatus);
        activity?.SetTag("gate_command_attempt_count", gateCommand.Command.AttemptCount);

        if (!gateCommand.CanInvokeAdapter)
        {
            var resultCode = gateCommand.Command.CommandStatus switch
            {
                GateCommandStatus.Succeeded => "GATE_AUTHORIZATION_CONSUMED_COMMAND_ALREADY_SUCCEEDED",
                GateCommandStatus.TerminalFailure => "GATE_AUTHORIZATION_CONSUMED_COMMAND_TERMINAL_FAILURE",
                GateCommandStatus.Failed => "GATE_AUTHORIZATION_CONSUMED_COMMAND_FAILED",
                GateCommandStatus.Retryable => "GATE_AUTHORIZATION_CONSUMED_COMMAND_RETRY_NOT_READY",
                _ => "GATE_AUTHORIZATION_CONSUMED_COMMAND_IN_PROGRESS"
            };
            if (gateCommand.Command.CommandStatus is GateCommandStatus.TerminalFailure or GateCommandStatus.Failed)
            {
                await _recorder.RecordFailedAsync(
                    command.Handoff,
                    resultCode,
                    gateCommand.Command.LastFailureReason
                        ?? gateCommand.Command.FailureReason
                        ?? "Gate command cannot be retried by policy.",
                    cancellationToken);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("result_code", resultCode);
            activity?.SetTag("adapter_invoked", false);
            activity?.SetTag("already_processed", false);

            return new GateAuthorizationConsumedProcessingResult(
                processing.Record.EventId,
                processing.Record.ExitAuthorizationId,
                processing.Record.GateAuthorizationConsumptionId,
                processing.Record.TariffSnapshotId,
                resultCode,
                AdapterInvoked: false,
                AlreadyProcessed: false,
                processing.Record.ProcessedAtUtc);
        }

        try
        {
            await _adapter.ProcessConsumedAuthorizationAsync(command.Handoff, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failureCode = exception is ConsumedAuthorizationGateActionAdapterException adapterException
                ? adapterException.ResultCode
                : "GATE_HANDOFF_ADAPTER_FAILED";
            var retryable = exception is not ConsumedAuthorizationGateActionAdapterException typedException
                || typedException.Retryable;

            await _commandRecorder.RecordFailedAsync(
                gateCommand.Command.CommandId,
                failureCode,
                exception.Message,
                retryable,
                cancellationToken);
            await _recorder.RecordFailedAsync(
                command.Handoff,
                failureCode,
                exception.Message,
                cancellationToken);
            throw;
        }

        var processedAtUtc = DateTimeOffset.UtcNow;
        await _commandRecorder.RecordSucceededAsync(
            gateCommand.Command.CommandId,
            processedAtUtc,
            cancellationToken);

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
