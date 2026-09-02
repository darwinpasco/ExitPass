using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

public sealed class StatutoryDiscountCanonicalDatabaseFixture : IAsyncLifetime
{
    public const string CanonicalDbRepoEnvVar = "EXITPASS_STATUTORY_CANONICAL_DB_REPO";
    public const string CanonicalGeneratedSqlEnvVar = "EXITPASS_STATUTORY_CANONICAL_GENERATED_SQL";
    public const string CanonicalAlignmentValidatorEnvVar = "EXITPASS_STATUTORY_CANONICAL_ALIGNMENT_VALIDATOR";
    public const string AdminConnectionStringEnvVar = "EXITPASS_STATUTORY_DB_FIXTURE_ADMIN_CONNECTION";
    public const string DatabasePrefixEnvVar = "EXITPASS_STATUTORY_DB_FIXTURE_PREFIX";
    public const string DockerContainerEnvVar = "EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_CONTAINER";
    public const string DockerUserEnvVar = "EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_USER";
    public const string ApplicationPatchRootEnvVar = "EXITPASS_CENTRAL_PMS_INTEGRATION_PATCH_ROOT";
    public const string TaskOwnedContainerIdEnvVar = "EXITPASS_CENTRAL_PMS_INTEGRATION_POSTGRES_ID";
    public const string TaskOwnedRunIdEnvVar = "EXITPASS_CENTRAL_PMS_INTEGRATION_RUN_ID";

    private const string DefaultCanonicalDbRepo = @"D:\SourceCodes\exitpassdb_v1.2";
    private const string RequiredDatabasePrefix = "exitpass_central_pms_it_";

    private static readonly Regex SafeDatabaseNamePattern = new("^[a-z][a-z0-9_]{0,62}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ProtectedDatabaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "postgres",
        "template0",
        "template1",
        "exitpass_v12_dev"
    };

    private readonly Dictionary<string, string?> _previousEnvironmentValues = new();
    private readonly bool _suiteOwner;
    private string? _databaseName;
    private string? _connectionString;
    private bool _borrowedSuiteDatabase;

    public StatutoryDiscountCanonicalDatabaseFixture()
        : this(suiteOwner: false)
    {
    }

    internal StatutoryDiscountCanonicalDatabaseFixture(bool suiteOwner) => _suiteOwner = suiteOwner;

    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("The statutory discount canonical database fixture is not initialized.");

    public string DatabaseName =>
        _databaseName ?? throw new InvalidOperationException("The statutory discount canonical database fixture is not initialized.");

    public async Task InitializeAsync()
    {
        if (!_suiteOwner)
        {
            if (!CentralPmsIntegrationSuiteDatabase.TryBorrow(out _databaseName, out _connectionString))
            {
                throw new InvalidOperationException(
                    "Central PMS integration suite database is not initialized. The assembly test framework must create the task-owned canonical database before collection fixtures start.");
            }

            _borrowedSuiteDatabase = true;
            await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(_connectionString!);
            return;
        }

        try
        {
            var options = StatutoryDiscountCanonicalDatabaseFixtureOptions.Load();
            await VerifyTaskOwnedPostgresAsync(options);
            _databaseName = CreateDatabaseName(options.DatabasePrefix);
            var adminConnectionString = BuildAdminConnectionString(options.AdminConnectionString);
            _connectionString = BuildDatabaseConnectionString(options.AdminConnectionString, _databaseName);

            await CreateDatabaseAsync(adminConnectionString, _databaseName);
            await RunPsqlFileAsync(options, _databaseName, options.CanonicalGeneratedSqlPath, "canonical SQL apply");
            await RunPsqlFileAsync(options, _databaseName, options.CanonicalAlignmentValidatorPath, "canonical alignment validation");
            await ApplyApplicationPatchesAsync(options, _databaseName, replay: false);
            await ApplyApplicationPatchesAsync(options, _databaseName, replay: true);
            await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(_connectionString);
            PublishConnectionString(_connectionString);
            CentralPmsIntegrationSuiteDatabase.Publish(_databaseName, _connectionString);
        }
        catch (Exception exception)
        {
            if (_databaseName is not null)
            {
                await TryDropDatabaseAsync(exception);
            }

            throw new InvalidOperationException(
                $"Statutory discount canonical database fixture failed during setup phase. {exception.Message}",
                exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_borrowedSuiteDatabase)
        {
            return;
        }

        Exception? cleanupException = null;
        if (_databaseName is not null)
        {
            try
            {
                var options = StatutoryDiscountCanonicalDatabaseFixtureOptions.Load(validateFiles: false);
                var adminConnectionString = BuildAdminConnectionString(options.AdminConnectionString);
                await DropDatabaseAsync(adminConnectionString, _databaseName);
            }
            catch (Exception exception)
            {
                cleanupException = exception;
            }
        }

        RestoreEnvironment();
        CentralPmsIntegrationSuiteDatabase.Clear();

        if (cleanupException is not null)
        {
            throw new InvalidOperationException(
                $"Statutory discount canonical database fixture cleanup failed for disposable database '{_databaseName}'.",
                cleanupException);
        }
    }

    private static async Task ApplyApplicationPatchesAsync(
        StatutoryDiscountCanonicalDatabaseFixtureOptions options,
        string databaseName,
        bool replay)
    {
        var pass = replay ? "idempotency replay" : "initial apply";
        foreach (var source in options.ApplicationSchemaSources)
        {
            await RunPsqlFileAsync(options, databaseName, source.PatchPath, $"{source.Name} patch {pass}");
            if (source.ValidatorPath is not null)
            {
                await RunPsqlFileAsync(options, databaseName, source.ValidatorPath, $"{source.Name} validation {pass}");
            }
        }
    }

    private static async Task VerifyTaskOwnedPostgresAsync(StatutoryDiscountCanonicalDatabaseFixtureOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder(options.AdminConnectionString);
        if (!string.Equals(builder.Host, "127.0.0.1", StringComparison.Ordinal) ||
            !string.Equals(builder.Database, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration phase failed: integration PostgreSQL must use the task-owned loopback endpoint and postgres maintenance database.");
        }

        if (!options.DatabasePrefix.StartsWith(RequiredDatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration phase failed: database prefix must start with '{RequiredDatabasePrefix}'.");
        }

        var inspection = await RunProcessForOutputAsync(
            "docker",
            [
                "inspect",
                "--format",
                "{{.Id}}|{{index .Config.Labels \"exitpass.central-pms.integration-test\"}}|{{index .Config.Labels \"exitpass.central-pms.integration-run-id\"}}|{{(index (index .NetworkSettings.Ports \"5432/tcp\") 0).HostIp}}|{{(index (index .NetworkSettings.Ports \"5432/tcp\") 0).HostPort}}",
                options.DockerContainer
            ],
            "task-owned PostgreSQL verification",
            "inspect immutable container identity");
        var parts = inspection.Trim().Split('|');
        if (parts.Length != 5 ||
            !string.Equals(parts[0], options.TaskOwnedContainerId, StringComparison.Ordinal) ||
            !string.Equals(parts[1], "true", StringComparison.Ordinal) ||
            !string.Equals(parts[2], options.TaskOwnedRunId, StringComparison.Ordinal) ||
            !string.Equals(parts[3], "127.0.0.1", StringComparison.Ordinal) ||
            !int.TryParse(parts[4], out var mappedPort) || mappedPort != builder.Port)
        {
            throw new InvalidOperationException(
                "Configuration phase failed: PostgreSQL immutable identity, ownership labels, or loopback port mapping did not match the task-owned fixture configuration.");
        }
    }

    internal static string CreateDatabaseName(string prefix)
    {
        var normalizedPrefix = NormalizeDatabaseNamePrefix(prefix);
        var raw = $"{normalizedPrefix}{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
        var candidate = (raw.Length > 63 ? raw[..63] : raw).TrimEnd('_');
        EnsureSafeDatabaseName(candidate);
        return candidate;
    }

    internal static void EnsureSafeDatabaseName(string databaseName)
    {
        if (!SafeDatabaseNamePattern.IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                $"Configuration phase failed: disposable database name '{databaseName}' is not a safe PostgreSQL identifier.");
        }

        if (ProtectedDatabaseNames.Contains(databaseName))
        {
            throw new InvalidOperationException(
                $"Configuration phase failed: refusing to target protected database '{databaseName}'.");
        }
    }

    private static string NormalizeDatabaseNamePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = RequiredDatabasePrefix;
        }

        var lower = prefix.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var character in lower)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        var normalized = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(normalized) || !char.IsAsciiLetter(normalized[0]))
        {
            normalized = $"exitpass_{normalized}";
        }

        if (!normalized.EndsWith('_'))
        {
            normalized += "_";
        }

        return normalized.Length > 40 ? normalized[..40].TrimEnd('_') + "_" : normalized;
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        EnsureSafeDatabaseName(databaseName);
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            CREATE DATABASE {QuoteIdentifier(databaseName)};
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        EnsureSafeDatabaseName(databaseName);
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @database_name
              AND pid <> pg_backend_pid();
            """,
            connection);
        terminate.Parameters.AddWithValue("database_name", databaseName);
        await terminate.ExecuteNonQueryAsync();

        await using var drop = new NpgsqlCommand(
            $"""
            DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);
            """,
            connection);
        await drop.ExecuteNonQueryAsync();
    }

    private async Task TryDropDatabaseAsync(Exception setupException)
    {
        try
        {
            var options = StatutoryDiscountCanonicalDatabaseFixtureOptions.Load(validateFiles: false);
            await DropDatabaseAsync(BuildAdminConnectionString(options.AdminConnectionString), _databaseName!);
        }
        catch (Exception cleanupException)
        {
            throw new InvalidOperationException(
                $"Statutory discount canonical database fixture failed during setup and cleanup also failed. Setup: {setupException.Message}",
                cleanupException);
        }
    }

    private static async Task RunPsqlFileAsync(
        StatutoryDiscountCanonicalDatabaseFixtureOptions options,
        string databaseName,
        string sqlPath,
        string phase)
    {
        var containerPath = $"/tmp/exitpass-fixture-{Guid.NewGuid():N}.sql";
        await RunProcessAsync(
            "docker",
            [
                "cp",
                sqlPath,
                $"{options.DockerContainer}:{containerPath}"
            ],
            phase,
            "copy SQL into PostgreSQL container");

        try
        {
            await RunProcessAsync(
                "docker",
                [
                    "exec",
                    options.DockerContainer,
                    "psql",
                    "-X",
                    "-q",
                    "-v",
                    "ON_ERROR_STOP=1",
                    "-U",
                    options.DockerUser,
                    "-d",
                    databaseName,
                    "-f",
                    containerPath
                ],
                phase,
                "execute SQL inside PostgreSQL container");
        }
        finally
        {
            await RunProcessAsync(
                "docker",
                [
                    "exec",
                    options.DockerContainer,
                    "rm",
                    "-f",
                    containerPath
                ],
                phase,
                "remove temporary SQL from PostgreSQL container",
                throwOnFailure: false);
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string phase,
        string operation,
        bool throwOnFailure = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Configuration phase failed: unable to start {operation} for {phase}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 && throwOnFailure)
        {
            throw new InvalidOperationException(
                $"{phase} phase failed while trying to {operation}; exit code {process.ExitCode}. Output: {TrimForError(stdout)} {TrimForError(stderr)}");
        }
    }

    private static async Task<string> RunProcessForOutputAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string phase,
        string operation)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Configuration phase failed: unable to start {operation} for {phase}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{phase} phase failed while trying to {operation}; exit code {process.ExitCode}. Output: {TrimForError(stdout)} {TrimForError(stderr)}");
        }

        return stdout;
    }

    private void PublishConnectionString(string connectionString)
    {
        foreach (var variable in CentralPmsIntegrationTestConfiguration.DatabaseConnectionStringEnvVars)
        {
            _previousEnvironmentValues[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, connectionString);
        }
    }

    private void RestoreEnvironment()
    {
        foreach (var pair in _previousEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static string BuildAdminConnectionString(string configuredConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            Database = "postgres"
        };
        return builder.ConnectionString;
    }

    private static string BuildDatabaseConnectionString(string configuredConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string TrimForError(string value)
    {
        value = value.Trim();
        return value.Length <= 2000 ? value : value[..2000] + "...";
    }

    internal sealed record StatutoryDiscountCanonicalDatabaseFixtureOptions(
        string AdminConnectionString,
        string CanonicalGeneratedSqlPath,
        string CanonicalAlignmentValidatorPath,
        IReadOnlyList<ApplicationSchemaSource> ApplicationSchemaSources,
        string DatabasePrefix,
        string DockerContainer,
        string DockerUser,
        string TaskOwnedContainerId,
        string TaskOwnedRunId)
    {
        public static StatutoryDiscountCanonicalDatabaseFixtureOptions Load(bool validateFiles = true)
        {
            var canonicalRepo = Environment.GetEnvironmentVariable(CanonicalDbRepoEnvVar);
            if (string.IsNullOrWhiteSpace(canonicalRepo))
            {
                canonicalRepo = DefaultCanonicalDbRepo;
            }

            var generatedSql = Environment.GetEnvironmentVariable(CanonicalGeneratedSqlEnvVar);
            if (string.IsNullOrWhiteSpace(generatedSql))
            {
                generatedSql = Path.Combine(canonicalRepo, "build", "generated", "exitpass-full-object.generated.sql");
            }

            var validator = Environment.GetEnvironmentVariable(CanonicalAlignmentValidatorEnvVar);
            if (string.IsNullOrWhiteSpace(validator))
            {
                validator = Path.Combine(canonicalRepo, "scripts", "validation", "Validate-V13CentralPmsAlignment.sql");
            }

            if (validateFiles)
            {
                RequireExistingFile(generatedSql, "canonical SQL source");
                RequireExistingFile(validator, "canonical alignment validator");
            }

            var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(adminConnectionString))
            {
                throw new InvalidOperationException(
                    $"Configuration phase failed: {AdminConnectionStringEnvVar} must be supplied by the task-owned integration test framework.");
            }


            var patchRoot = Environment.GetEnvironmentVariable(ApplicationPatchRootEnvVar);
            if (string.IsNullOrWhiteSpace(patchRoot))
            {
                patchRoot = FindRepositoryRoot();
            }

            var applicationSchemaSources = new[]
            {
                new ApplicationSchemaSource(
                    "HikCentral projection schema",
                    Path.Combine(patchRoot, "docs", "sql", "HikCentralProjectionSchemaPatch.sql"),
                    ValidatorPath: null),
                new ApplicationSchemaSource(
                    "HikCentral projection safety",
                    Path.Combine(patchRoot, "infra", "db", "patches", "ExitPass_HikCentralProjectionSafety_v1.3.sql"),
                    Path.Combine(patchRoot, "infra", "db", "patches", "validation", "Validate_HikCentralProjectionSafety_v1.3.sql")),
                new ApplicationSchemaSource(
                    "multi-site vendor adapter routing",
                    Path.Combine(patchRoot, "infra", "db", "patches", "ExitPass_MultiSiteVendorAdapterRouting_v1.3.sql"),
                    Path.Combine(patchRoot, "infra", "db", "patches", "validation", "Validate_MultiSiteVendorAdapterRouting_v1.3.sql")),
                new ApplicationSchemaSource(
                    "payment-attempt customer method",
                    Path.Combine(patchRoot, "infra", "db", "patches", "ExitPass_Core_PaymentAttemptPaymentMethod_v1.3.sql"),
                    ValidatorPath: null),
                new ApplicationSchemaSource(
                    "Operator Console server-owned operating context",
                    Path.Combine(patchRoot, "infra", "db", "patches", "ExitPass_OperatorConsoleOperatingContext_v1.3.sql"),
                    Path.Combine(patchRoot, "infra", "db", "patches", "validation", "Validate_OperatorConsoleOperatingContext_v1.3.sql"))
            };

            var container = RequireEnvironmentValue(DockerContainerEnvVar);
            var user = RequireEnvironmentValue(DockerUserEnvVar);
            var containerId = RequireEnvironmentValue(TaskOwnedContainerIdEnvVar);
            var runId = RequireEnvironmentValue(TaskOwnedRunIdEnvVar);

            if (validateFiles)
            {
                foreach (var source in applicationSchemaSources)
                {
                    RequireExistingFile(source.PatchPath, $"{source.Name} patch");
                    if (source.ValidatorPath is not null)
                    {
                        RequireExistingFile(source.ValidatorPath, $"{source.Name} validator");
                    }
                }
            }

            return new StatutoryDiscountCanonicalDatabaseFixtureOptions(
                adminConnectionString,
                generatedSql,
                validator,
                applicationSchemaSources,
                Environment.GetEnvironmentVariable(DatabasePrefixEnvVar) ?? RequiredDatabasePrefix,
                container,
                user,
                containerId,
                runId);

            static string RequireEnvironmentValue(string variable)
            {
                var value = Environment.GetEnvironmentVariable(variable);
                return !string.IsNullOrWhiteSpace(value)
                    ? value
                    : throw new InvalidOperationException(
                        $"Configuration phase failed: {variable} must be supplied by the task-owned integration test framework.");
            }

            static string FindRepositoryRoot()
            {
                var current = new DirectoryInfo(AppContext.BaseDirectory);
                while (current is not null)
                {
                    if (File.Exists(Path.Combine(current.FullName, "ExitPass.sln")) &&
                        Directory.Exists(Path.Combine(current.FullName, "infra", "db", "patches")))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }

                throw new InvalidOperationException(
                    $"Configuration phase failed: repository root could not be resolved. Set {ApplicationPatchRootEnvVar}.");
            }
        }

        private static void RequireExistingFile(string path, string description)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Configuration phase failed: {description} was not found at '{path}'. Set the appropriate statutory fixture environment variable.");
            }
        }
    }

    internal sealed record ApplicationSchemaSource(string Name, string PatchPath, string? ValidatorPath);
}

internal static class CentralPmsIntegrationSuiteDatabase
{
    private static readonly object Sync = new();
    private static string? _databaseName;
    private static string? _connectionString;

    public static void Publish(string databaseName, string connectionString)
    {
        lock (Sync)
        {
            if (_databaseName is not null || _connectionString is not null)
            {
                throw new InvalidOperationException("Central PMS integration suite database was initialized more than once.");
            }

            _databaseName = databaseName;
            _connectionString = connectionString;
        }
    }

    public static bool TryBorrow(out string? databaseName, out string? connectionString)
    {
        lock (Sync)
        {
            databaseName = _databaseName;
            connectionString = _connectionString;
            return databaseName is not null && connectionString is not null;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _databaseName = null;
            _connectionString = null;
        }
    }
}
