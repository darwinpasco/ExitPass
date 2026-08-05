using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

public sealed class StatutoryEvidenceScanWorkerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<StatutoryEvidenceScanWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<StatutoryEvidenceScanWorkerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        if (!workerOptions.Enabled)
        {
            logger.LogInformation("Statutory evidence scan worker is disabled.");
            return;
        }

        if (!workerOptions.HasCriticalConfiguration())
        {
            logger.LogWarning("Statutory evidence scan worker is not configured and will not start.");
            return;
        }

        using var timer = new PeriodicTimer(workerOptions.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStatutoryEvidenceScanWorkerService>();
            var processed = await service.RunOnceAsync(cancellationToken);
            logger.LogInformation(
                "Statutory evidence scan worker cycle completed. processed_count={ProcessedCount} evaluated_at={EvaluatedAt}",
                processed,
                timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statutory evidence scan worker cycle failed.");
        }
    }
}
