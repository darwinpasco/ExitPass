using ExitPass.CentralPms.Application.Gates;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Disabled-by-default hosted loop around the single-cycle stale gate command recovery coordinator.
/// </summary>
public sealed class GateCommandRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GateCommandRecoveryWorkerOptions> options,
    IGateCommandRecoveryWorkerDelay delay,
    TimeProvider timeProvider,
    ILogger<GateCommandRecoveryWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        workerOptions.ThrowIfInvalid();

        if (!workerOptions.Enabled)
        {
            logger.LogInformation("Gate command recovery worker is disabled by configuration.");
            return;
        }

        var initialDelay = workerOptions.EffectiveInitialDelay();
        if (initialDelay > TimeSpan.Zero)
        {
            await delay.DelayAsync(initialDelay, stoppingToken);
        }

        var interval = workerOptions.EffectiveInterval();
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneCycleAsync(workerOptions, stoppingToken);
            await delay.DelayAsync(interval, stoppingToken);
        }
    }

    private async Task RunOneCycleAsync(
        GateCommandRecoveryWorkerOptions workerOptions,
        CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var recoveryCycle = scope.ServiceProvider.GetRequiredService<IGateCommandRecoveryCycleService>();
            var currentUtc = timeProvider.GetUtcNow();
            var staleBefore = currentUtc.Subtract(workerOptions.EffectiveStaleAfter());
            var retryDelay = workerOptions.EffectiveRetryDelay();
            var result = await recoveryCycle.RunOnceAsync(staleBefore, retryDelay, stoppingToken);

            switch (result.Outcome)
            {
                case GateCommandRecoveryCycleOutcome.NoWork:
                    logger.LogDebug("Gate command recovery worker cycle completed with no stale command.");
                    break;

                case GateCommandRecoveryCycleOutcome.RecoveredRetryable:
                    logger.LogInformation(
                        "Gate command recovery worker recovered command to retryable. gate_command_id={GateCommandId} final_status={FinalCommandStatus} next_attempt_at={NextAttemptAt}",
                        result.GateCommandId,
                        result.FinalCommandStatus,
                        result.NextAttemptAt);
                    break;

                case GateCommandRecoveryCycleOutcome.RecoveredTerminalFailure:
                    logger.LogWarning(
                        "Gate command recovery worker recovered command to terminal failure. gate_command_id={GateCommandId} final_status={FinalCommandStatus} terminal_failure_at={TerminalFailureAt}",
                        result.GateCommandId,
                        result.FinalCommandStatus,
                        result.TerminalFailureAt);
                    break;

                case GateCommandRecoveryCycleOutcome.LostRaceOrIneligible:
                    logger.LogInformation(
                        "Gate command recovery worker selected command was not recovered. gate_command_id={GateCommandId} final_status={FinalCommandStatus} error_code={ErrorCode}",
                        result.GateCommandId,
                        result.FinalCommandStatus,
                        result.ErrorCode);
                    break;

                default:
                    logger.LogWarning(
                        "Gate command recovery worker received an unrecognized cycle outcome. outcome={Outcome}",
                        result.Outcome);
                    break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gate command recovery worker cycle failed.");
        }
    }
}

/// <summary>
/// Delay abstraction used by the gate command recovery worker.
/// </summary>
public interface IGateCommandRecoveryWorkerDelay
{
    /// <summary>
    /// Delays the worker loop.
    /// </summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>
/// Production delay implementation for the gate command recovery worker.
/// </summary>
public sealed class GateCommandRecoveryWorkerDelay : IGateCommandRecoveryWorkerDelay
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
