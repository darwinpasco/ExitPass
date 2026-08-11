using ExitPass.CentralPms.Application.VendorSessions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Environment-aware readiness check for vendor session projection.
/// </summary>
public sealed class VendorSessionProjectionReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<VendorSessionProjectionOptions> options) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        var baseData = new Dictionary<string, object>
        {
            ["scheduler_enabled"] = configured.SchedulerEnabled,
            ["required_for_environment"] = configured.RequiredForEnvironment,
            ["normal_freshness_target_seconds"] = configured.NormalFreshnessTargetSeconds,
            ["maximum_projection_age_minutes"] = configured.MaxProjectionAgeMinutes
        };

        if (!configured.SchedulerEnabled)
        {
            return configured.RequiredForEnvironment
                ? HealthCheckResult.Unhealthy(
                    "Projection is required but the scheduler is disabled.",
                    data: baseData)
                : HealthCheckResult.Healthy(
                    "Projection is explicitly disabled and optional for this environment.",
                    baseData);
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IVendorSessionProjectionHealthService>();
            var targets = await service.ListTargetsAsync(cancellationToken);
            var enabled = targets.Where(target => target.Enabled).ToArray();
            baseData["enabled_target_count"] = enabled.Length;
            baseData["freshness_classifications"] = string.Join(
                ",",
                enabled.Select(target => target.FreshnessClassification).Distinct().Order());
            baseData["last_attempt_at"] = Latest(enabled.Select(target => target.LastAttemptAt));
            baseData["last_success_at"] = Latest(enabled.Select(target => target.LastSuccessAt));
            baseData["last_failure_at"] = Latest(enabled.Select(target => target.LastFailureAt));
            baseData["last_failure_classifications"] = string.Join(
                ",",
                enabled.Select(target => target.LastErrorCode)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct()
                    .Order());
            baseData["lock_contention_count"] = enabled.Sum(target => target.LockContentionCount);

            var unhealthy = enabled.Length == 0 || enabled.Any(target =>
                target.FreshnessClassification is "FAILED" or "STALE" or "NEVER_SYNCHRONIZED");
            var delayed = enabled.Any(target =>
                target.FreshnessClassification is "DELAYED" or "LOCK_CONTENDED_DEFERRED");

            if (unhealthy)
            {
                return configured.RequiredForEnvironment
                    ? HealthCheckResult.Unhealthy("Required projection is not current.", data: baseData)
                    : HealthCheckResult.Degraded("Optional projection is not current.", data: baseData);
            }

            return delayed
                ? HealthCheckResult.Degraded("Projection is delayed or deferred.", data: baseData)
                : HealthCheckResult.Healthy("Projection is current.", baseData);
        }
        catch (Exception)
        {
            return configured.RequiredForEnvironment
                ? HealthCheckResult.Unhealthy("Required projection health is unavailable.", data: baseData)
                : HealthCheckResult.Degraded("Optional projection health is unavailable.", data: baseData);
        }
    }

    private static string Latest(IEnumerable<DateTimeOffset?> timestamps) =>
        timestamps.Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .DefaultIfEmpty()
            .Max() is { } latest && latest != default
                ? latest.ToString("O")
                : "NEVER";
}
