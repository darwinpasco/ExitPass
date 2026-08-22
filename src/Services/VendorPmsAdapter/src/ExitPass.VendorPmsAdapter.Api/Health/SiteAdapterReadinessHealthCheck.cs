using ExitPass.VendorPmsAdapter.Api.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExitPass.VendorPmsAdapter.Api.Health;

public sealed class SiteAdapterReadinessHealthCheck(SiteAdapterRuntimeOptions options, IWebHostEnvironment environment)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var errors = options.Validate(environment.EnvironmentName);
        return Task.FromResult(errors.Count == 0 ? HealthCheckResult.Healthy("Site adapter is ready.")
            : HealthCheckResult.Unhealthy(errors[0]));
    }
}
