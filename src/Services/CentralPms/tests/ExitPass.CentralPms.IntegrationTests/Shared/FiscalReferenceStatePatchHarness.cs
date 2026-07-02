using Npgsql;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Applies and validates the Central PMS fiscal reference state persistence patch in the disposable DB harness.
/// </summary>
public static class FiscalReferenceStatePatchHarness
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    public static async Task EnsureAppliedAndValidatedAsync(string connectionString)
    {
        await PatchLock.WaitAsync();
        try
        {
            if (!await FiscalReferenceTableExistsAsync(connectionString))
            {
                await ExecuteSqlFileAsync(
                    connectionString,
                    ResolveRepoPath("infra", "db", "patches", "ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql"));
            }

            await ExecuteSqlFileAsync(
                connectionString,
                ResolveRepoPath("infra", "db", "patches", "validation", "Validate_CentralPmsFiscalReferenceStatePersistence_v1.3.sql"));
        }
        finally
        {
            PatchLock.Release();
        }
    }

    private static async Task<bool> FiscalReferenceTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_references') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task ExecuteSqlFileAsync(string connectionString, string path)
    {
        var sql = await File.ReadAllTextAsync(path);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };

        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveRepoPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not resolve repository path: {Path.Combine(segments)}");
    }
}
