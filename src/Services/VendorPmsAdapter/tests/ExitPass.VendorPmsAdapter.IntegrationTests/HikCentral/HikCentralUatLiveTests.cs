using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.VendorPmsAdapter.IntegrationTests.HikCentral;

/// <summary>
/// Gated live UAT tests for HikCentral Professional OpenAPI V3.1.0 session lookup and fee calculation.
/// </summary>
/// <remarks>
/// BRD v1.2 validation: parking session lookup and tariff calculation against a vendor PMS.
/// SDD v1.2 validation: Vendor PMS Adapter boundary maps vendor payloads into canonical ExitPass contracts.
/// ExitPass v1.2 invariant: live vendor validation must not mutate payment, exit, gate, settlement, or payout truth.
/// </remarks>
public sealed class HikCentralUatLiveTests
{
    private const string CalculatePath = "/artemis/api/vehicle/v1/parkingfee/calculate";
    private const string FakeDisabledModeBaseUrl = "https://hikcentral-uat.example.invalid";
    private const string FakeDisabledModeAppKey = "fake-app-key";
    private const string FakeDisabledModeAppSecret = "fake-app-secret";
    private const string FakeDisabledModePlateNumber = "TEST-PLATE-123";
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the HikCentral UAT live test suite.
    /// </summary>
    /// <param name="output">xUnit output helper for sanitized diagnostics.</param>
    public HikCentralUatLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies the UAT gate remains closed unless explicitly enabled.
    /// </summary>
    [Fact]
    public void HikCentralUat_WhenDisabled_SkipsLiveVendorCalls()
    {
        // These constants are synthetic test inputs used only to prove that disabled UAT mode does not make live
        // vendor calls. They are not credentials and must never be replaced with real HikCentral values.
        var env = new Dictionary<string, string?>
        {
            [HikCentralUatEnvironment.EnabledName] = "false",
            [HikCentralUatEnvironment.BaseUrlName] = FakeDisabledModeBaseUrl,
            [HikCentralUatEnvironment.AppKeyName] = FakeDisabledModeAppKey,
            [HikCentralUatEnvironment.AppSecretName] = FakeDisabledModeAppSecret,
            [HikCentralUatEnvironment.TestPlateNumberName] = FakeDisabledModePlateNumber
        };

        var result = HikCentralUatEnvironment.Evaluate(
            name => env.TryGetValue(name, out var value) ? value : null,
            requireTestIdentifier: false);

        Assert.False(result.CanRun);
        Assert.Equal(
            "Set EXITPASS_HIKCENTRAL_UAT_ENABLED=true to run live HikCentral UAT tests.",
            result.SkipReason);
    }

    /// <summary>
    /// Verifies environment values bind into safe HikCentral adapter options.
    /// </summary>
    [Fact]
    public void HikCentralUat_WhenEnabledAndConfigured_BindsOptionsFromEnvironment()
    {
        var env = new Dictionary<string, string?>
        {
            [HikCentralUatEnvironment.EnabledName] = "true",
            [HikCentralUatEnvironment.BaseUrlName] = "https://hikcentral-uat-options.example.invalid",
            [HikCentralUatEnvironment.AppKeyName] = "fake-options-app-key",
            [HikCentralUatEnvironment.AppSecretName] = "fake-options-app-secret",
            [HikCentralUatEnvironment.TestPlateNumberName] = "OPTIONS-PLATE-123"
        };

        var settings = HikCentralUatEnvironment.GetRequired(
            name => env.TryGetValue(name, out var value) ? value : null,
            requireTestIdentifier: true);

        var errors = settings.ToOptions().Validate();

        Assert.Empty(errors);
        Assert.Equal(new Uri("https://hikcentral-uat-options.example.invalid"), settings.BaseUri);
        Assert.Equal("OPTIONS-PLATE-123", settings.TestPlateNumber);
        Assert.Null(settings.TestTicketNumber);
    }

    /// <summary>
    /// Verifies UAT configuration diagnostics never include credential secret values.
    /// </summary>
    [Fact]
    public void HikCentralUat_WhenConfigurationInvalid_DoesNotLeakSecretInSkipReason()
    {
        const string secret = "do-not-log-this-hikcentral-secret";
        var env = new Dictionary<string, string?>
        {
            [HikCentralUatEnvironment.EnabledName] = "true",
            [HikCentralUatEnvironment.BaseUrlName] = "not-a-url",
            [HikCentralUatEnvironment.AppKeyName] = "fake-invalid-app-key",
            [HikCentralUatEnvironment.AppSecretName] = secret,
            [HikCentralUatEnvironment.TestPlateNumberName] = "INVALID-PLATE-123"
        };

        var result = HikCentralUatEnvironment.Evaluate(
            name => env.TryGetValue(name, out var value) ? value : null,
            requireTestIdentifier: true);

        Assert.False(result.CanRun);
        Assert.DoesNotContain(secret, result.SkipReason, StringComparison.Ordinal);
        Assert.DoesNotContain("APP_SECRET", result.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends a low-risk signed request to verify live UAT connectivity and authentication wiring.
    /// </summary>
    [HikCentralUatFact]
    public async Task HikCentralUat_WhenEnabled_CanReachVendorWithSignedRequest()
    {
        var settings = HikCentralUatEnvironment.GetRequired(Environment.GetEnvironmentVariable, requireTestIdentifier: false);
        var correlationId = Guid.NewGuid();
        using var client = CreateHttpClient(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
        request.Headers.TryAddWithoutValidation("userId", settings.UserId);

        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions(settings.AppKey, settings.AppSecret));
        await signer.SignAsync(request, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, CancellationToken.None);
        stopwatch.Stop();

        var diagnostics = await HikCentralUatDiagnostics.FromResponseAsync(
            "/",
            correlationId,
            stopwatch.Elapsed,
            response,
            CancellationToken.None);

        _output.WriteLine(diagnostics.ToSafeMessage());
        Assert.NotEqual(0, (int)response.StatusCode);
    }

    /// <summary>
    /// Validates live session lookup with configured UAT plate or ticket test data.
    /// </summary>
    [HikCentralUatFact(requireTestIdentifier: true)]
    public async Task HikCentralUat_SessionLookup_WithConfiguredIdentifier_ReturnsCanonicalSession()
    {
        var settings = HikCentralUatEnvironment.GetRequired(Environment.GetEnvironmentVariable, requireTestIdentifier: true);
        using var diagnosticsHandler = new HikCentralUatDiagnosticsHandler(new HttpClientHandler());
        using var httpClient = CreateHttpClient(settings, diagnosticsHandler);
        var client = CreateParkingClient(settings, httpClient);
        var correlationId = Guid.NewGuid();

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(settings.TestPlateNumber, settings.TestTicketNumber, correlationId),
            CancellationToken.None);

        _output.WriteLine(diagnosticsHandler.Diagnostics?.ToSafeMessage() ?? "No HikCentral response diagnostics were captured.");

        AssertLiveLookupSucceeded(result.Status, result.ErrorCode, diagnosticsHandler.Diagnostics);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.NotNull(result.Session);
        Assert.Equal(HikCentralParkingClient.ProviderCode, result.Session.VendorProviderCode);
        Assert.NotNull(result.Session.TariffQuote);
    }

    /// <summary>
    /// Validates live tariff calculation with configured UAT plate or ticket test data.
    /// </summary>
    [HikCentralUatFact(requireTestIdentifier: true)]
    public async Task HikCentralUat_ParkingFeeCalculation_WithConfiguredIdentifier_ReturnsCanonicalTariff()
    {
        var settings = HikCentralUatEnvironment.GetRequired(Environment.GetEnvironmentVariable, requireTestIdentifier: true);
        using var diagnosticsHandler = new HikCentralUatDiagnosticsHandler(new HttpClientHandler());
        using var httpClient = CreateHttpClient(settings, diagnosticsHandler);
        var client = CreateParkingClient(settings, httpClient);
        var correlationId = Guid.NewGuid();

        var result = await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest(settings.TestPlateNumber, settings.TestTicketNumber, correlationId),
            CancellationToken.None);

        _output.WriteLine(diagnosticsHandler.Diagnostics?.ToSafeMessage() ?? "No HikCentral response diagnostics were captured.");

        AssertLiveLookupSucceeded(result.Status, result.ErrorCode, diagnosticsHandler.Diagnostics);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.NotNull(result.Quote);
        Assert.Equal("PHP", result.Quote.Currency);
        Assert.True(result.Quote.AmountMinor >= 0);
    }

    private static void AssertLiveLookupSucceeded(
        VendorParkingLookupStatus status,
        string? errorCode,
        HikCentralUatDiagnostics? diagnostics)
    {
        if (status is VendorParkingLookupStatus.Found)
        {
            return;
        }

        var safeDiagnostics = diagnostics?.ToSafeMessage() ?? "no vendor diagnostics";
        throw new Xunit.Sdk.XunitException(
            $"HikCentral UAT test data did not produce a canonical match. status={status}, errorCode={errorCode ?? "n/a"}, diagnostics={safeDiagnostics}");
    }

    private static HikCentralParkingClient CreateParkingClient(
        HikCentralUatSettings settings,
        HttpClient httpClient)
    {
        return new HikCentralParkingClient(
            httpClient,
            new HikCentralRequestSigner(
                new HikCentralCredentialOptions(settings.AppKey, settings.AppSecret)),
            settings.UserId);
    }

    private static HttpClient CreateHttpClient(
        HikCentralUatSettings settings,
        HttpMessageHandler? handler = null)
    {
        return new HttpClient(handler ?? new HttpClientHandler())
        {
            BaseAddress = settings.BaseUri,
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private sealed class HikCentralUatDiagnosticsHandler : DelegatingHandler
    {
        public HikCentralUatDiagnosticsHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public HikCentralUatDiagnostics? Diagnostics { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            var body = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            Diagnostics = HikCentralUatDiagnostics.FromBody(
                request.RequestUri?.AbsolutePath ?? CalculatePath,
                ReadCorrelationId(request),
                response.StatusCode,
                body,
                stopwatch.Elapsed);

            var clone = new HttpResponseMessage(response.StatusCode)
            {
                Content = body is null ? null : new StringContent(body),
                ReasonPhrase = response.ReasonPhrase,
                RequestMessage = request,
                Version = response.Version
            };

            foreach (var header in response.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (response.Content is null)
            {
                return clone;
            }

            foreach (var header in response.Content.Headers)
            {
                clone.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        private static Guid ReadCorrelationId(HttpRequestMessage request)
        {
            return request.Headers.TryGetValues("X-Correlation-Id", out var values) &&
                Guid.TryParse(values.SingleOrDefault(), out var correlationId)
                    ? correlationId
                    : Guid.Empty;
        }
    }

    private sealed record HikCentralEnvelope(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("msg")] string? Message);

    private sealed record HikCentralUatDiagnostics(
        string EndpointPath,
        Guid CorrelationId,
        HttpStatusCode? HttpStatusCode,
        string? HikCentralCode,
        string? HikCentralMessage,
        TimeSpan Elapsed)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<HikCentralUatDiagnostics> FromResponseAsync(
            string endpointPath,
            Guid correlationId,
            TimeSpan elapsed,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            var body = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            return FromBody(endpointPath, correlationId, response.StatusCode, body, elapsed);
        }

        public static HikCentralUatDiagnostics FromBody(
            string endpointPath,
            Guid correlationId,
            HttpStatusCode? httpStatusCode,
            string? body,
            TimeSpan elapsed)
        {
            var envelope = TryReadEnvelope(body);

            return new HikCentralUatDiagnostics(
                endpointPath,
                correlationId,
                httpStatusCode,
                envelope?.Code,
                envelope?.Message,
                elapsed);
        }

        public string ToSafeMessage()
        {
            return string.Join(
                " | ",
                $"endpoint={EndpointPath}",
                $"correlationId={CorrelationId}",
                $"httpStatus={(HttpStatusCode.HasValue ? (int)HttpStatusCode.Value : "n/a")}",
                $"hikCentralCode={SafeValue(HikCentralCode)}",
                $"hikCentralMsg={SafeValue(HikCentralMessage)}",
                $"elapsedMs={Elapsed.TotalMilliseconds:0}");
        }

        private static HikCentralEnvelope? TryReadEnvelope(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<HikCentralEnvelope>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string SafeValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "n/a" : value;
        }
    }
}

internal sealed class HikCentralUatFactAttribute : FactAttribute
{
    public HikCentralUatFactAttribute(bool requireTestIdentifier = false)
    {
        Skip = HikCentralUatEnvironment.Evaluate(
            Environment.GetEnvironmentVariable,
            requireTestIdentifier).SkipReason;
    }
}

internal static class HikCentralUatEnvironment
{
    public const string EnabledName = "EXITPASS_HIKCENTRAL_UAT_ENABLED";
    public const string BaseUrlName = "HIKCENTRAL_BASE_URL";
    public const string AppKeyName = "HIKCENTRAL_APP_KEY";
    public const string AppSecretName = "HIKCENTRAL_APP_SECRET";
    public const string TestPlateNumberName = "HIKCENTRAL_TEST_PLATE_NUMBER";
    public const string TestTicketNumberName = "HIKCENTRAL_TEST_TICKET_NUMBER";
    public const string TestParkingRecordIdName = "HIKCENTRAL_TEST_PARKING_RECORD_ID";

    private const string DefaultUserId = "exitpass-adapter";

    private static readonly string[] RequiredVariableNames =
    [
        BaseUrlName,
        AppKeyName,
        AppSecretName
    ];

    public static HikCentralUatGateResult Evaluate(
        Func<string, string?> getEnvironmentVariable,
        bool requireTestIdentifier)
    {
        if (!string.Equals(
            getEnvironmentVariable(EnabledName),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return HikCentralUatGateResult.Skipped(
                "Set EXITPASS_HIKCENTRAL_UAT_ENABLED=true to run live HikCentral UAT tests.");
        }

        var missing = RequiredVariableNames
            .Where(name => string.IsNullOrWhiteSpace(getEnvironmentVariable(name)))
            .ToArray();

        if (missing.Length > 0)
        {
            return HikCentralUatGateResult.Skipped(
                $"Missing required HikCentral UAT environment variables: {string.Join(", ", missing)}.");
        }

        var settings = TryGetSettings(getEnvironmentVariable);
        if (settings is null)
        {
            return HikCentralUatGateResult.Skipped("HIKCENTRAL_BASE_URL must be an absolute URL.");
        }

        var optionErrors = settings.ToOptions().Validate();
        if (optionErrors.Count > 0)
        {
            return HikCentralUatGateResult.Skipped(
                $"Invalid HikCentral UAT adapter options: {string.Join(", ", optionErrors)}.");
        }

        if (requireTestIdentifier &&
            string.IsNullOrWhiteSpace(settings.TestPlateNumber) &&
            string.IsNullOrWhiteSpace(settings.TestTicketNumber))
        {
            return HikCentralUatGateResult.Skipped(
                $"Set {TestPlateNumberName} or {TestTicketNumberName} to run live HikCentral lookup and tariff UAT tests.");
        }

        return HikCentralUatGateResult.Runnable();
    }

    public static HikCentralUatSettings GetRequired(
        Func<string, string?> getEnvironmentVariable,
        bool requireTestIdentifier)
    {
        var gate = Evaluate(getEnvironmentVariable, requireTestIdentifier);
        if (!gate.CanRun)
        {
            throw new InvalidOperationException(gate.SkipReason);
        }

        return TryGetSettings(getEnvironmentVariable)
            ?? throw new InvalidOperationException("HIKCENTRAL_BASE_URL must be an absolute URL.");
    }

    private static HikCentralUatSettings? TryGetSettings(Func<string, string?> getEnvironmentVariable)
    {
        if (!Uri.TryCreate(getEnvironmentVariable(BaseUrlName), UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        return new HikCentralUatSettings(
            baseUri,
            getEnvironmentVariable(AppKeyName)!,
            getEnvironmentVariable(AppSecretName)!,
            DefaultUserId,
            Normalize(getEnvironmentVariable(TestPlateNumberName)),
            Normalize(getEnvironmentVariable(TestTicketNumberName)),
            Normalize(getEnvironmentVariable(TestParkingRecordIdName)));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal sealed record HikCentralUatGateResult(bool CanRun, string? SkipReason)
{
    public static HikCentralUatGateResult Runnable()
    {
        return new HikCentralUatGateResult(true, null);
    }

    public static HikCentralUatGateResult Skipped(string reason)
    {
        return new HikCentralUatGateResult(false, reason);
    }
}

internal sealed record HikCentralUatSettings(
    Uri BaseUri,
    string AppKey,
    string AppSecret,
    string UserId,
    string? TestPlateNumber,
    string? TestTicketNumber,
    string? TestParkingRecordId)
{
    public HikCentralOptions ToOptions()
    {
        return new HikCentralOptions
        {
            Enabled = true,
            BaseUrl = BaseUri.ToString(),
            AppKey = AppKey,
            AppSecret = AppSecret,
            UserId = UserId
        };
    }
}
