using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ExitPass.AuditEventService.Api.Health;

public sealed class AuditDatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT to_regclass('audit.audit_events') IS NOT NULL;");
            var present = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return present
                ? HealthCheckResult.Healthy("Canonical audit event persistence is available.")
                : HealthCheckResult.Unhealthy("AUDIT_EVENT_SCHEMA_MISSING");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("AUDIT_EVENT_DATABASE_UNAVAILABLE");
        }
    }
}
