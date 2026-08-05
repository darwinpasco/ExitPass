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

    private const string DefaultCanonicalDbRepo = @"D:\SourceCodes\exitpassdb_v1.2";
    private const string DefaultDatabasePrefix = "exitpass_statutory_fixture_";
    private const string DefaultDockerContainer = "exitpass-postgres";
    private const string DefaultDockerUser = "exitpass";

    private static readonly Regex SafeDatabaseNamePattern = new("^[a-z][a-z0-9_]{0,62}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ProtectedDatabaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "postgres",
        "template0",
        "template1",
        "exitpass_v12_dev"
    };

    private readonly Dictionary<string, string?> _previousEnvironmentValues = new();
    private string? _databaseName;
    private string? _connectionString;

    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("The statutory discount canonical database fixture is not initialized.");

    public string DatabaseName =>
        _databaseName ?? throw new InvalidOperationException("The statutory discount canonical database fixture is not initialized.");

    public async Task InitializeAsync()
    {
        try
        {
            var options = StatutoryDiscountCanonicalDatabaseFixtureOptions.Load();
            _databaseName = CreateDatabaseName(options.DatabasePrefix);
            var adminConnectionString = BuildAdminConnectionString(options.AdminConnectionString);
            _connectionString = BuildDatabaseConnectionString(options.AdminConnectionString, _databaseName);

            await CreateDatabaseAsync(adminConnectionString, _databaseName);
            await RunPsqlFileAsync(options, _databaseName, options.CanonicalGeneratedSqlPath, "canonical SQL apply");
            await RunPsqlFileAsync(options, _databaseName, options.CanonicalAlignmentValidatorPath, "canonical alignment validation");
            await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(_connectionString);
            PublishConnectionString(_connectionString);
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

        if (cleanupException is not null)
        {
            throw new InvalidOperationException(
                $"Statutory discount canonical database fixture cleanup failed for disposable database '{_databaseName}'.",
                cleanupException);
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
            prefix = DefaultDatabasePrefix;
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
        string DatabasePrefix,
        string DockerContainer,
        string DockerUser)
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
                adminConnectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
            }

            return new StatutoryDiscountCanonicalDatabaseFixtureOptions(
                adminConnectionString,
                generatedSql,
                validator,
                Environment.GetEnvironmentVariable(DatabasePrefixEnvVar) ?? DefaultDatabasePrefix,
                Environment.GetEnvironmentVariable(DockerContainerEnvVar) ?? DefaultDockerContainer,
                Environment.GetEnvironmentVariable(DockerUserEnvVar) ?? DefaultDockerUser);
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
}
