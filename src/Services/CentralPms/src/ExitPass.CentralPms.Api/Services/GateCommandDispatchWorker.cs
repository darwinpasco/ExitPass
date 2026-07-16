using ExitPass.CentralPms.Application.Gates;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Disabled-by-default hosted loop around the single-cycle gate command dispatcher.
/// </summary>
public sealed class GateCommandDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GateCommandDispatchWorkerOptions> options,
    IGateCommandDispatchWorkerDelay delay,
    ILogger<GateCommandDispatchWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        workerOptions.ThrowIfInvalid();

        if (!workerOptions.Enabled)
        {
            logger.LogInformation("Gate command dispatch worker is disabled by configuration.");
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
            await RunOneCycleAsync(stoppingToken);
            await delay.DelayAsync(interval, stoppingToken);
        }
    }

    private async Task RunOneCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IGateCommandDispatchCycleService>();
            var result = await dispatcher.RunOnceAsync(stoppingToken);

            switch (result.Outcome)
            {
                case GateCommandDispatchCycleOutcome.NoWork:
                    logger.LogDebug("Gate command dispatch worker cycle completed with no eligible command.");
                    break;

                case GateCommandDispatchCycleOutcome.Dispatched:
                    logger.LogInformation(
                        "Gate command dispatch worker dispatched command. gate_command_id={GateCommandId} candidate_status={CandidateStatus} final_status={FinalCommandStatus} audit_id={AuditId}",
                        result.GateCommandId,
                        result.CandidateStatus,
                        result.FinalCommandStatus,
                        result.HikCentralGateActionAuditId);
                    break;

                case GateCommandDispatchCycleOutcome.LostRaceOrIneligible:
                    logger.LogInformation(
                        "Gate command dispatch worker selected command was not dispatched. gate_command_id={GateCommandId} candidate_status={CandidateStatus} final_status={FinalCommandStatus} error_code={ErrorCode}",
                        result.GateCommandId,
                        result.CandidateStatus,
                        result.FinalCommandStatus,
                        result.ErrorCode);
                    break;

                default:
                    logger.LogWarning(
                        "Gate command dispatch worker received an unrecognized cycle outcome. outcome={Outcome}",
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
            logger.LogError(ex, "Gate command dispatch worker cycle failed.");
        }
    }
}

/// <summary>
/// Delay abstraction used by the gate command dispatch worker.
/// </summary>
public interface IGateCommandDispatchWorkerDelay
{
    /// <summary>
    /// Delays the worker loop.
    /// </summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>
/// Production delay implementation for the gate command dispatch worker.
/// </summary>
public sealed class GateCommandDispatchWorkerDelay : IGateCommandDispatchWorkerDelay
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
