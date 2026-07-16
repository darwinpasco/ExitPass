using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Controlled single-command executor for canonical gate command lifecycle testing.
/// </summary>
public sealed class GateCommandExecutionService : IGateCommandExecutionService
{
    private readonly IGateCommandExecutionRepository _repository;
    private readonly IHikCentralGateActionAdapter _adapter;
    private readonly ISystemClock _clock;
    private readonly GateCommandExecutionOptions _options;

    /// <summary>
    /// Creates a controlled gate command execution service.
    /// </summary>
    public GateCommandExecutionService(
        IGateCommandExecutionRepository repository,
        IHikCentralGateActionAdapter adapter,
        ISystemClock clock,
        GateCommandExecutionOptions? options = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? GateCommandExecutionOptions.Default;

        if (_options.RetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("Retry delay must be positive.", nameof(options));
        }
    }

    /// <inheritdoc />
    public async Task<GateCommandExecutionResult> ExecuteAsync(
        Guid gateCommandId,
        CancellationToken cancellationToken)
    {
        return await ExecuteClaimedAsync(
            gateCommandId,
            static (repository, commandId, now, token) => repository.ClaimAsync(commandId, now, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GateCommandExecutionResult> RetryAsync(
        Guid gateCommandId,
        CancellationToken cancellationToken)
    {
        return await ExecuteClaimedAsync(
            gateCommandId,
            static (repository, commandId, now, token) => repository.ClaimRetryAsync(commandId, now, token),
            cancellationToken);
    }

    private async Task<GateCommandExecutionResult> ExecuteClaimedAsync(
        Guid gateCommandId,
        Func<IGateCommandExecutionRepository, Guid, DateTimeOffset, CancellationToken, Task<GateCommandClaimResult>> claimCommand,
        CancellationToken cancellationToken)
    {
        if (gateCommandId == Guid.Empty)
        {
            return Rejected(gateCommandId, "GATE_COMMAND_ID_REQUIRED", "Gate command id is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var claimResult = await claimCommand(_repository, gateCommandId, _clock.UtcNow, cancellationToken);
        if (claimResult.Outcome == GateCommandClaimOutcome.AlreadyCompleted)
        {
            return new GateCommandExecutionResult(
                gateCommandId,
                GateCommandExecutionOutcome.AlreadyCompleted,
                claimResult.CommandStatus ?? "SUCCEEDED",
                HikCentralGateActionAuditId: null,
                AdapterInvoked: false,
                claimResult.ErrorCode,
                claimResult.Message);
        }

        if (claimResult.Outcome != GateCommandClaimOutcome.Claimed || claimResult.Claim is null)
        {
            return Rejected(
                gateCommandId,
                claimResult.ErrorCode ?? "GATE_COMMAND_NOT_ELIGIBLE",
                claimResult.Message ?? "Gate command is not eligible for execution.",
                claimResult.CommandStatus);
        }

        var claim = claimResult.Claim;
        var actionRequest = new HikCentralGateActionRequest(
            claim.CommandId,
            claim.GateAuthorizationConsumptionId,
            claim.ExitAuthorizationId,
            claim.GateDeviceId,
            claim.VendorSystemId,
            claim.SiteId,
            claim.LaneId,
            claim.TargetResourceCode,
            HikCentralGateActionConstants.OpenGateOperation,
            claim.CorrelationId,
            claim.LastAttemptedAt);

        var actionResult = await _adapter.ExecuteAsync(actionRequest, cancellationToken);
        var finalized = await _repository.FinalizeAsync(
            claim,
            actionResult,
            _clock.UtcNow,
            _options.RetryDelay,
            cancellationToken);

        return new GateCommandExecutionResult(
            finalized.GateCommandId,
            GateCommandExecutionOutcome.Executed,
            finalized.CommandStatus,
            finalized.HikCentralGateActionAuditId,
            AdapterInvoked: true,
            ErrorCode: null,
            Message: null);
    }

    private static GateCommandExecutionResult Rejected(
        Guid gateCommandId,
        string errorCode,
        string message,
        string? commandStatus = null) =>
        new(
            gateCommandId,
            GateCommandExecutionOutcome.Rejected,
            commandStatus ?? string.Empty,
            HikCentralGateActionAuditId: null,
            AdapterInvoked: false,
            errorCode,
            message);
}
