using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ExitPass.CentralPms.Api.Security;

public sealed class HumanAuthenticationHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly ITotpSecretProtector _totpProtector;

    public HumanAuthenticationHealthCheck(string connectionString, ITotpSecretProtector totpProtector)
    {
        _connectionString = connectionString;
        _totpProtector = totpProtector;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT to_regclass('identity.human_sessions') IS NOT NULL AND to_regclass('identity.local_credentials') IS NOT NULL;", connection);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false) is false)
            {
                return HealthCheckResult.Unhealthy("Canonical human authentication persistence is unavailable.");
            }
            return _totpProtector.IsConfigured
                ? HealthCheckResult.Healthy("Human authentication persistence and TOTP protection are available.")
                : HealthCheckResult.Degraded("Human authentication persistence is available; TOTP operations are disabled until key protection is configured.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Canonical human authentication persistence is unavailable.");
        }
    }
}
