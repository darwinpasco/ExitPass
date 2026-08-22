namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Resolves shared Central PMS integration-test configuration.
///
/// BRD:
/// - 10.7.13 End-to-End Traceability Invariant
///
/// SDD:
/// - 9.6 Integrity Constraints and Concurrency Rules
/// - 10 API Architecture
///
/// Invariants Enforced:
/// - API hosts and database seed helpers must use the same effective database target.
/// - Integration tests must use the assembly-owned disposable canonical database.
/// - Missing suite configuration must fail explicitly instead of reaching a standing database.
/// </summary>
public static class CentralPmsIntegrationTestConfiguration
{
    /// <summary>
    /// Environment variable containing the preferred current Central PMS test database connection string.
    /// </summary>
    public const string MainDbConnectionStringEnvVar = "EXITPASS_TEST_MAIN_DB";

    /// <summary>
    /// Environment variable containing the Central PMS integration database connection string.
    /// </summary>
    public const string IntegrationDbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    /// <summary>
    /// Environment variable containing the alternate Central PMS test database connection string.
    /// </summary>
    public const string TestDbConnectionStringEnvVar = "EXITPASS_TEST_DB_CONNECTION_STRING";

    /// <summary>
    /// Environment variable used by ASP.NET Core configuration for the Central PMS main database.
    /// </summary>
    public const string MainDatabaseConfigEnvVar = "ConnectionStrings__MainDatabase";

    /// <summary>
    /// Ordered Central PMS database connection string environment variables used by integration tests.
    /// </summary>
    public static readonly IReadOnlyList<string> DatabaseConnectionStringEnvVars =
    [
        MainDbConnectionStringEnvVar,
        IntegrationDbConnectionStringEnvVar,
        TestDbConnectionStringEnvVar,
        MainDatabaseConfigEnvVar
    ];

    /// <summary>
    /// Returns the effective Central PMS integration-test database connection string.
    /// </summary>
    public static string GetDatabaseConnectionString()
    {
        return DatabaseConnectionStringEnvVars
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException(
                $"Integration test database connection string is missing. The assembly test framework must publish one of: " +
                $"{MainDbConnectionStringEnvVar}, {IntegrationDbConnectionStringEnvVar}, " +
                $"{TestDbConnectionStringEnvVar}, or {MainDatabaseConfigEnvVar}.");
    }

    /// <summary>
    /// Returns the effective Central PMS integration-test database connection string or throws when none is configured.
    /// </summary>
    public static string RequireDatabaseConnectionString()
    {
        return GetDatabaseConnectionString();
    }

    /// <summary>
    /// Publishes the resolved database connection string to the ASP.NET Core configuration environment variable.
    /// </summary>
    public static string PublishResolvedDatabaseConnectionString()
    {
        var connectionString = RequireDatabaseConnectionString();
        Environment.SetEnvironmentVariable(MainDatabaseConfigEnvVar, connectionString);
        return connectionString;
    }
}
