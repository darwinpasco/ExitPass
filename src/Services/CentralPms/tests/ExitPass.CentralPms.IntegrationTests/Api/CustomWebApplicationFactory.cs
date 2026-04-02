using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Hosts the Central PMS API in-memory for API integration tests.
///
/// BRD:
/// - 9.16 Monitoring and Administration
///
/// SDD:
/// - 10 API Architecture
/// - 13 Deployment Architecture
///
/// Invariants Enforced:
/// - API integration tests exercise the real ASP.NET Core pipeline
/// - Test hosting must not bypass production endpoint wiring
/// - The API host must receive an explicit MainDatabase connection string before startup
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string IntegrationDbEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string MainDatabaseConfigEnvVar = "ConnectionStrings__MainDatabase";

    /// <summary>
    /// Configures the in-memory API host for integration testing.
    /// </summary>
    /// <param name="builder">Web host builder used to compose the test host.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var integrationDbConnectionString =
            Environment.GetEnvironmentVariable(IntegrationDbEnvVar)
            ?? throw new InvalidOperationException(
                $"Missing environment variable '{IntegrationDbEnvVar}'. " +
                "Point it at the ExitPass integration database.");

        // Program.cs reads configuration during startup, and default config already includes env vars.
        Environment.SetEnvironmentVariable(MainDatabaseConfigEnvVar, integrationDbConnectionString);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = integrationDbConnectionString
            });
        });
    }
}
