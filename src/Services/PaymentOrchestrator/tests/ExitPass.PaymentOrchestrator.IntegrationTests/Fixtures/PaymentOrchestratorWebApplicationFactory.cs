using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;

/// <summary>
/// Test host factory for Payment Orchestrator API integration tests.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Integration tests must boot with deterministic test configuration.
/// - Test hosts must not depend on missing external appsettings values.
/// - Secrets must be sourced from environment/configuration, not hard-coded in test source.
/// </summary>
public sealed class PaymentOrchestratorWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var bootstrap = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var webhookSecret =
                bootstrap["PAYMONGO_WEBHOOK_SECRET_KEY"] ??
                bootstrap["Providers:PayMongo:WebhookSecretKey"] ??
                bootstrap["Providers__PayMongo__WebhookSecretKey"];

            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                throw new InvalidOperationException(
                    "Missing required PayMongo webhook secret for integration tests. " +
                    "Expose PAYMONGO_WEBHOOK_SECRET_KEY to the test runner process.");
            }

            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:CentralPms:BaseUrl"] = "http://central-pms:8080",
                ["Providers:PayMongo:WebhookSecretKey"] = webhookSecret,
                ["Providers:PayMongo:IsLiveMode"] = "false"
            });
        });
    }
}
