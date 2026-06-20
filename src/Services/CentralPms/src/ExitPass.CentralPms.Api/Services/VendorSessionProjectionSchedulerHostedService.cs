using ExitPass.CentralPms.Application.VendorSessions;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Single Central PMS hosted scheduler for many site-scoped vendor session projection jobs.
/// </summary>
public sealed class VendorSessionProjectionSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<VendorSessionProjectionOptions> options,
    ILogger<VendorSessionProjectionSchedulerHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SchedulerEnabled)
        {
            logger.LogInformation("Vendor session projection scheduler is disabled by configuration.");
            return;
        }

        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, options.Value.StartupDelaySeconds));
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        var scanInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.SchedulerScanIntervalSeconds));
        using var timer = new PeriodicTimer(scanInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IVendorSessionProjectionSyncOrchestrator>();
            var result = await orchestrator.RunDueTargetsOnceAsync(stoppingToken);

            logger.LogInformation(
                "Vendor session projection scheduler pass completed. targets_loaded={TargetsLoaded} targets_run={TargetsRun} targets_succeeded={TargetsSucceeded} targets_failed={TargetsFailed}",
                result.TargetsLoaded,
                result.TargetsRun,
                result.TargetsSucceeded,
                result.TargetsFailed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vendor session projection scheduler pass failed before target isolation.");
        }
    }
}
