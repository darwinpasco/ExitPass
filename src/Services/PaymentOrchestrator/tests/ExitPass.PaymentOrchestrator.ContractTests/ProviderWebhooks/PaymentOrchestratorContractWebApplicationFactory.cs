using System.Collections.Concurrent;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExitPass.PaymentOrchestrator.ContractTests.ProviderWebhooks;

/// <summary>
/// Creates a Payment Orchestrator API host for v1.2 provider-webhook contract tests.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - PayMongo signature verification is preserved in contract tests.
/// - External provider callback persistence is test-local and deterministic.
/// - Central PMS remains the owner of PaymentAttempt finality and ExitAuthorization issuance.
/// </summary>
public sealed class PaymentOrchestratorContractWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryProviderWebhookEventRepository _providerWebhookEvents = new();
    private readonly CapturingCentralPmsPaymentOutcomeReporter _centralPmsReporter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentOrchestratorContractWebApplicationFactory"/> class.
    /// </summary>
    public PaymentOrchestratorContractWebApplicationFactory()
    {
        PayMongoWebhookSecretKey = $"whsec_contract_{Guid.NewGuid():N}";
        BootstrapContractEnvironment();
    }

    /// <summary>
    /// Gets the generated PayMongo webhook secret used by the in-process contract host.
    /// </summary>
    public string PayMongoWebhookSecretKey { get; }

    /// <summary>
    /// Gets the captured verified outcome reports sent to the Central PMS boundary.
    /// </summary>
    public IReadOnlyCollection<VerifiedPaymentOutcomeReport> ReportedOutcomes => _centralPmsReporter.Reports;

    /// <summary>
    /// Clears test-local captured state.
    /// </summary>
    public void ResetState()
    {
        _providerWebhookEvents.Clear();
        _centralPmsReporter.Clear();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("ContractTest");

        builder.ConfigureServices(services =>
        {
            // ExitPass v1.2 BRD 9.10, 9.13, and 12; SDD 10.5.2, 10.5.3, and 10.7:
            // contract tests verify the public provider-webhook API surface while preserving
            // POA's invariant that it reports evidence and never owns payment finality.
            services.RemoveAll<IProviderWebhookEventRepository>();
            services.RemoveAll<ICentralPmsPaymentOutcomeReporter>();
            services.AddSingleton<IProviderWebhookEventRepository>(_providerWebhookEvents);
            services.AddSingleton<ICentralPmsPaymentOutcomeReporter>(_centralPmsReporter);
        });
    }

    private void BootstrapContractEnvironment()
    {
        // ExitPass v1.2 BRD 12 and SDD 10.5.2: these generated test-only values
        // satisfy API startup configuration without storing or hard-coding provider secrets.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__MainDatabase",
            "Host=localhost;Database=exitpass_contract_tests;Username=exitpass;Password=unused");
        Environment.SetEnvironmentVariable("Integrations__CentralPms__BaseUrl", "http://central-pms.contract-test");
        Environment.SetEnvironmentVariable("Payments__Providers__PayMongo__BaseUrl", "https://api.paymongo.test");
        Environment.SetEnvironmentVariable("Payments__Providers__PayMongo__SecretKey", $"sk_contract_{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("Payments__Providers__PayMongo__PublicKey", $"pk_contract_{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("Payments__Providers__PayMongo__WebhookSecretKey", PayMongoWebhookSecretKey);
        Environment.SetEnvironmentVariable("Payments__Providers__PayMongo__IsLiveMode", "false");
    }

    private sealed class InMemoryProviderWebhookEventRepository : IProviderWebhookEventRepository
    {
        private readonly ConcurrentDictionary<string, ProviderWebhookEventRecord> _records = new(StringComparer.Ordinal);

        public Task<bool> ExistsByProviderEventIdAsync(
            string providerCode,
            string providerEventId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.ContainsKey(CreateKey(providerCode, providerEventId)));
        }

        public Task AddAsync(
            ProviderWebhookEventRecord record,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (!_records.TryAdd(CreateKey(record.ProviderCode, record.ProviderEventId), record))
            {
                throw new DuplicateProviderWebhookEventException(
                    $"Provider callback already exists for callback reference '{record.ProviderEventId}'.");
            }

            return Task.CompletedTask;
        }

        public void Clear()
        {
            _records.Clear();
        }

        private static string CreateKey(string providerCode, string providerEventId)
        {
            return $"{providerCode.Trim().ToUpperInvariant()}::{providerEventId.Trim()}";
        }
    }

    private sealed class CapturingCentralPmsPaymentOutcomeReporter : ICentralPmsPaymentOutcomeReporter
    {
        private readonly ConcurrentQueue<VerifiedPaymentOutcomeReport> _reports = new();

        public IReadOnlyCollection<VerifiedPaymentOutcomeReport> Reports => _reports.ToArray();

        public Task ReportVerifiedOutcomeAsync(
            VerifiedPaymentOutcomeReport report,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(report);
            _reports.Enqueue(report);
            return Task.CompletedTask;
        }

        public void Clear()
        {
            while (_reports.TryDequeue(out _))
            {
            }
        }
    }
}
