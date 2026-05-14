using System;
using System.Collections.Concurrent;
using System.IO;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;

/// <summary>
/// Creates an in-process test host for Payment Orchestrator integration tests.
///
/// BRD implemented:
/// - Section 12, Payment Orchestration
///
/// SDD implemented:
/// - Provider webhook verification
/// - Verified payment outcome reporting to Central PMS
///
/// System invariants enforced:
/// - Required startup configuration must exist before the application entry point executes.
/// - Secrets must come from environment variables and must never be hard-coded in tests.
/// - Integration tests must fail closed when required provider credentials are missing.
/// - Provider-webhook ingress tests verify POA-owned callback verification and idempotency without letting POA bypass Central PMS payment finality.
/// </summary>
public sealed class PaymentOrchestratorWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string ExitPassDockerEnvRelativePath = "infra/docker/.env";

    private const string MainDatabaseConnectionStringConfigKey = "ConnectionStrings__MainDatabase";
    private const string CentralPmsBaseUrlConfigKey = "Integrations__CentralPms__BaseUrl";
    private const string PayMongoBaseUrlConfigKey = "Payments__Providers__PayMongo__BaseUrl";
    private const string PayMongoSecretKeyConfigKey = "Payments__Providers__PayMongo__SecretKey";
    private const string PayMongoPublicKeyConfigKey = "Payments__Providers__PayMongo__PublicKey";
    private const string PayMongoWebhookSecretKeyConfigKey = "Payments__Providers__PayMongo__WebhookSecretKey";
    private const string PayMongoIsLiveModeConfigKey = "Payments__Providers__PayMongo__IsLiveMode";

    private const string MainDatabaseEnvVar = "EXITPASS_TEST_MAIN_DB";
    private const string AlternateMainDatabaseEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string OptionalCentralPmsBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string PayMongoSecretKeyEnvVar = "PAYMONGO_SECRET_KEY";
    private const string PayMongoPublicKeyEnvVar = "PAYMONGO_PUBLIC_KEY";
    private const string PayMongoWebhookSecretKeyEnvVar = "PAYMONGO_WEBHOOK_SECRET_KEY";
    private const string OptionalPayMongoBaseUrlEnvVar = "PAYMONGO_BASE_URL";
    private const string OptionalPayMongoIsLiveModeEnvVar = "PAYMONGO_IS_LIVE_MODE";

    private readonly InMemoryProviderWebhookEventRepository _providerWebhookEventRepository = new();
    private readonly CapturingCentralPmsPaymentOutcomeReporter _centralPmsReporter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentOrchestratorWebApplicationFactory"/> class.
    /// </summary>
    public PaymentOrchestratorWebApplicationFactory()
    {
        BootstrapEnvironmentForTestHost();

        MainDatabaseConnectionString = GetRequiredEnvironmentValue(
            MainDatabaseConnectionStringConfigKey,
            MainDatabaseEnvVar,
            AlternateMainDatabaseEnvVar);

        CentralPmsBaseUrl = GetRequiredEnvironmentValue(
            CentralPmsBaseUrlConfigKey,
            OptionalCentralPmsBaseUrlEnvVar);

        PayMongoBaseUrl = GetRequiredEnvironmentValue(
            PayMongoBaseUrlConfigKey,
            OptionalPayMongoBaseUrlEnvVar);

        PayMongoSecretKey = GetRequiredEnvironmentValue(
            PayMongoSecretKeyConfigKey,
            PayMongoSecretKeyEnvVar);

        PayMongoPublicKey = GetRequiredEnvironmentValue(
            PayMongoPublicKeyConfigKey,
            PayMongoPublicKeyEnvVar);

        PayMongoWebhookSecretKey = GetRequiredEnvironmentValue(
            PayMongoWebhookSecretKeyConfigKey,
            PayMongoWebhookSecretKeyEnvVar);

        PayMongoIsLiveMode = GetRequiredEnvironmentValue(
            PayMongoIsLiveModeConfigKey,
            OptionalPayMongoIsLiveModeEnvVar);
    }

    /// <summary>
    /// Gets the resolved main database connection string used by the test host.
    /// </summary>
    public string MainDatabaseConnectionString { get; }

    /// <summary>
    /// Gets the resolved Central PMS base URL used by the test host.
    /// </summary>
    public string CentralPmsBaseUrl { get; }

    /// <summary>
    /// Gets the resolved PayMongo base URL used by the test host.
    /// </summary>
    public string PayMongoBaseUrl { get; }

    /// <summary>
    /// Gets the resolved PayMongo secret key used by the test host.
    /// </summary>
    public string PayMongoSecretKey { get; }

    /// <summary>
    /// Gets the resolved PayMongo public key used by the test host.
    /// </summary>
    public string PayMongoPublicKey { get; }

    /// <summary>
    /// Gets the resolved PayMongo webhook secret key used by the test host.
    /// </summary>
    public string PayMongoWebhookSecretKey { get; }

    /// <summary>
    /// Gets the resolved PayMongo live mode flag used by the test host.
    /// </summary>
    public string PayMongoIsLiveMode { get; }

    /// <summary>
    /// Gets the verified provider outcomes captured at the POA-to-Central PMS boundary.
    ///
    /// BRD implemented:
    /// - Section 9.10, Payment Processing and Confirmation
    /// - Section 9.13, Timeout, Retry, and Duplicate Handling
    /// - Section 12, Payment Orchestration
    ///
    /// SDD implemented:
    /// - Section 10.5.2, Payment Provider Webhook
    /// - Section 10.5.3, Report Verified Payment Outcome
    ///
    /// System invariant enforced:
    /// - POA integration tests may observe provider-neutral reports, but POA must not finalize
    ///   PaymentAttempt state or create ExitAuthorization directly.
    /// </summary>
    public IReadOnlyCollection<VerifiedPaymentOutcomeReport> CapturedCentralPmsReports =>
        _centralPmsReporter.Reports;

    /// <summary>
    /// Clears in-memory webhook evidence and captured Central PMS reports for an isolated boundary test.
    ///
    /// BRD implemented:
    /// - Section 9.13, Timeout, Retry, and Duplicate Handling
    ///
    /// SDD implemented:
    /// - Section 10.7, Idempotency and Concurrency Rules
    ///
    /// System invariant enforced:
    /// - Each integration test owns its provider-event idempotency state and captured boundary reports.
    /// </summary>
    public void ResetBoundaryState()
    {
        _providerWebhookEventRepository.Clear();
        _centralPmsReporter.Clear();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
            // ExitPass v1.2 BRD 9.10, 9.13, and 12; SDD 10.5.2, 10.5.3, and 10.7:
            // provider-webhook integration tests must exercise real callback verification while keeping
            // Central PMS as the sole owner of payment finality and exit-authorization decisions.
            services.RemoveAll<IProviderWebhookEventRepository>();
            services.RemoveAll<ICentralPmsPaymentOutcomeReporter>();
            services.AddSingleton<IProviderWebhookEventRepository>(_providerWebhookEventRepository);
            services.AddSingleton<ICentralPmsPaymentOutcomeReporter>(_centralPmsReporter);
        });
    }

    /// <summary>
    /// Loads the real Docker <c>.env</c> file when available and maps flat environment
    /// variables into the hierarchical configuration keys expected by the application.
    /// </summary>
    private static void BootstrapEnvironmentForTestHost()
    {
        LoadDotEnvFileIfPresent();

        string mainDatabaseConnectionString =
            GetRequiredEnvironmentValue(
                MainDatabaseConnectionStringConfigKey,
                MainDatabaseEnvVar,
                AlternateMainDatabaseEnvVar);

        string centralPmsBaseUrl =
            GetFirstNonEmptyEnvironmentValue(
                CentralPmsBaseUrlConfigKey,
                OptionalCentralPmsBaseUrlEnvVar)
            ?? "http://localhost:8080";

        string payMongoBaseUrl =
            GetFirstNonEmptyEnvironmentValue(
                PayMongoBaseUrlConfigKey,
                OptionalPayMongoBaseUrlEnvVar)
            ?? "https://api.paymongo.com";

        string payMongoSecretKey =
            GetRequiredEnvironmentValue(
                PayMongoSecretKeyConfigKey,
                PayMongoSecretKeyEnvVar);

        string payMongoPublicKey =
            GetRequiredEnvironmentValue(
                PayMongoPublicKeyConfigKey,
                PayMongoPublicKeyEnvVar);

        string payMongoWebhookSecretKey =
            GetRequiredEnvironmentValue(
                PayMongoWebhookSecretKeyConfigKey,
                PayMongoWebhookSecretKeyEnvVar);

        string payMongoIsLiveMode =
            GetFirstNonEmptyEnvironmentValue(
                PayMongoIsLiveModeConfigKey,
                OptionalPayMongoIsLiveModeEnvVar)
            ?? "false";

        Environment.SetEnvironmentVariable(
            MainDatabaseConnectionStringConfigKey,
            mainDatabaseConnectionString);

        Environment.SetEnvironmentVariable(
            CentralPmsBaseUrlConfigKey,
            centralPmsBaseUrl);

        Environment.SetEnvironmentVariable(
            PayMongoBaseUrlConfigKey,
            payMongoBaseUrl);

        Environment.SetEnvironmentVariable(
            PayMongoSecretKeyConfigKey,
            payMongoSecretKey);

        Environment.SetEnvironmentVariable(
            PayMongoPublicKeyConfigKey,
            payMongoPublicKey);

        Environment.SetEnvironmentVariable(
            PayMongoWebhookSecretKeyConfigKey,
            payMongoWebhookSecretKey);

        Environment.SetEnvironmentVariable(
            PayMongoIsLiveModeConfigKey,
            payMongoIsLiveMode);
    }

    /// <summary>
    /// Loads the repository Docker <c>.env</c> file into the current process when present.
    /// Existing environment variables are preserved and not overwritten.
    /// </summary>
    private static void LoadDotEnvFileIfPresent()
    {
        string? dotEnvPath = ResolveDotEnvPath();
        if (string.IsNullOrWhiteSpace(dotEnvPath) || !File.Exists(dotEnvPath))
        {
            return;
        }

        foreach (string rawLine in File.ReadAllLines(dotEnvPath))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            value = Unquote(value);

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// Resolves the repository-root-relative Docker <c>.env</c> file from the current test execution directory.
    /// </summary>
    /// <returns>
    /// The full path to <c>infra/docker/.env</c> when found, otherwise <see langword="null"/>.
    /// </returns>
    private static string? ResolveDotEnvPath()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, ExitPassDockerEnvRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Returns the first non-empty environment value from the provided keys.
    /// </summary>
    /// <param name="keys">The environment variable names to probe in order.</param>
    /// <returns>The first non-empty value, or <see langword="null"/> when none exists.</returns>
    private static string? GetFirstNonEmptyEnvironmentValue(params string[] keys)
    {
        foreach (string key in keys)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a required environment value by checking the provided keys in order.
    /// </summary>
    /// <param name="keys">The environment variable names to probe in order.</param>
    /// <returns>The resolved non-empty value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when none of the provided environment variables contains a value.
    /// </exception>
    private static string GetRequiredEnvironmentValue(params string[] keys)
    {
        string? value = GetFirstNonEmptyEnvironmentValue(keys);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Missing required environment variable. Checked: {string.Join(", ", keys)}.");
    }

    /// <summary>
    /// Removes matching single or double quotes from a dotenv value.
    /// </summary>
    /// <param name="value">The raw dotenv value.</param>
    /// <returns>The normalized value.</returns>
    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            bool isDoubleQuoted = value[0] == '"' && value[^1] == '"';
            bool isSingleQuoted = value[0] == '\'' && value[^1] == '\'';

            if (isDoubleQuoted || isSingleQuoted)
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private sealed class InMemoryProviderWebhookEventRepository : IProviderWebhookEventRepository
    {
        private readonly ConcurrentDictionary<string, ProviderWebhookEventRecord> _records = new(StringComparer.Ordinal);

        public Task<bool> ExistsByProviderEventIdAsync(
            string providerCode,
            string providerEventId,
            CancellationToken cancellationToken)
        {
            var key = CreateKey(providerCode, providerEventId);
            return Task.FromResult(_records.ContainsKey(key));
        }

        public Task AddAsync(
            ProviderWebhookEventRecord record,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);

            var key = CreateKey(record.ProviderCode, record.ProviderEventId);
            if (!_records.TryAdd(key, record))
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
