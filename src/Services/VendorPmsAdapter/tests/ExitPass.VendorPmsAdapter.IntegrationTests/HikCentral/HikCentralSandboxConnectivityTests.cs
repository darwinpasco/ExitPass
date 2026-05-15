using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.VendorPmsAdapter.IntegrationTests.HikCentral;

/// <summary>
/// Gated live/sandbox HikCentral connectivity tests.
/// </summary>
public sealed class HikCentralSandboxConnectivityTests
{
    private const string CalculatePath = "/artemis/api/vehicle/v1/parkingfee/calculate";
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the live HikCentral sandbox connectivity tests.
    /// </summary>
    /// <param name="output">xUnit output helper for safe diagnostics.</param>
    public HikCentralSandboxConnectivityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies the live HikCentral gate stays closed unless the explicit run flag is enabled.
    /// </summary>
    [Fact]
    public void HikCentralSandbox_WhenLiveFlagDisabled_SkipsLiveConnectivity()
    {
        var env = new Dictionary<string, string?>
        {
            [LiveHikCentralEnvironment.RunFlagName] = "false",
            [LiveHikCentralEnvironment.BaseUrlName] = "https://hikcentral.example",
            [LiveHikCentralEnvironment.AppKeyName] = "test-key",
            [LiveHikCentralEnvironment.AppSecretName] = "test-secret",
            [LiveHikCentralEnvironment.UserIdName] = "exitpass-adapter"
        };

        var result = LiveHikCentralEnvironment.Evaluate(
            name => env.TryGetValue(name, out var value) ? value : null,
            requireTestIdentifier: false);

        Assert.False(result.CanRun);
        Assert.Equal(
            "Set EXITPASS_RUN_LIVE_HIKCENTRAL_TESTS=true to run live HikCentral sandbox tests.",
            result.SkipReason);
    }

    /// <summary>
    /// Sends a signed low-risk request to the configured sandbox base URL when live tests are enabled.
    /// </summary>
    [LiveHikCentralFact]
    public async Task HikCentralSandbox_WhenCredentialsPresent_CanReachHikCentralBaseEndpointOrSignedCalculate()
    {
        var settings = LiveHikCentralEnvironment.GetRequired();
        var correlationId = Guid.NewGuid();
        var endpointPath = "/";
        using var client = CreateHttpClient(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpointPath);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
        request.Headers.TryAddWithoutValidation("userId", settings.UserId);

        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions(settings.AppKey, settings.AppSecret));
        await signer.SignAsync(request, CancellationToken.None);

        Assert.NotEmpty(request.Headers.GetValues("X-Ca-Signature").Single());

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, CancellationToken.None);
        stopwatch.Stop();

        var diagnostics = await SafeHikCentralDiagnostics.FromResponseAsync(
            endpointPath,
            correlationId,
            stopwatch.Elapsed,
            response,
            CancellationToken.None);

        _output.WriteLine(diagnostics.ToSafeMessage());
        Assert.NotEqual(0, (int)response.StatusCode);
    }

    /// <summary>
    /// Sends a signed parking fee calculate request only when safe test plate or card data is provided.
    /// </summary>
    [LiveHikCentralFact(requireTestIdentifier: true)]
    public async Task HikCentralSandbox_CalculateParkingFee_WhenTestPlateOrCardProvided_ReturnsDeterministicHikCentralResponse()
    {
        var settings = LiveHikCentralEnvironment.GetRequired(requireTestIdentifier: true);
        var correlationId = Guid.NewGuid();
        using var probe = new SafeDiagnosticsHandler(new HttpClientHandler());
        using var httpClient = CreateHttpClient(settings, probe);
        var client = CreateParkingClient(settings, httpClient);

        var stopwatch = Stopwatch.StartNew();
        var result = await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest(settings.TestPlateLicense, settings.TestCardNum, correlationId),
            CancellationToken.None);
        stopwatch.Stop();

        var diagnostics = probe.Diagnostics?.WithElapsed(stopwatch.Elapsed)
            ?? new SafeHikCentralDiagnostics(
                CalculatePath,
                correlationId,
                null,
                null,
                null,
                stopwatch.Elapsed);

        _output.WriteLine(diagnostics.ToSafeMessage());
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.NotEqual(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.NotNull(probe.Diagnostics);
    }

    /// <summary>
    /// Verifies safe diagnostics do not include app secrets or raw signatures.
    /// </summary>
    [Fact]
    public void HikCentralSandbox_DoesNotExposeSecretInFailureOutput()
    {
        const string appSecret = "do-not-print-this-secret";
        const string rawSignature = "do-not-print-this-signature";
        var diagnostics = new SafeHikCentralDiagnostics(
            CalculatePath,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            HttpStatusCode.Unauthorized,
            "401",
            "Invalid signature",
            TimeSpan.FromMilliseconds(42));

        var message = diagnostics.ToSafeMessage();

        Assert.DoesNotContain(appSecret, message, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSignature, message, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSecret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Ca-Signature", message, StringComparison.OrdinalIgnoreCase);
    }

    private static HikCentralParkingClient CreateParkingClient(
        LiveHikCentralSettings settings,
        HttpClient httpClient)
    {
        return new HikCentralParkingClient(
            httpClient,
            new HikCentralRequestSigner(
                new HikCentralCredentialOptions(settings.AppKey, settings.AppSecret)),
            settings.UserId);
    }

    private static HttpClient CreateHttpClient(
        LiveHikCentralSettings settings,
        HttpMessageHandler? handler = null)
    {
        return new HttpClient(handler ?? new HttpClientHandler())
        {
            BaseAddress = settings.BaseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private sealed class SafeDiagnosticsHandler : DelegatingHandler
    {
        public SafeDiagnosticsHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public SafeHikCentralDiagnostics? Diagnostics { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var response = await base.SendAsync(request, cancellationToken);
            var body = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            Diagnostics = SafeHikCentralDiagnostics.FromBody(
                request.RequestUri?.AbsolutePath ?? CalculatePath,
                ReadCorrelationId(request),
                response.StatusCode,
                body,
                TimeSpan.Zero);

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

    private sealed record SafeHikCentralDiagnostics(
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

        public static async Task<SafeHikCentralDiagnostics> FromResponseAsync(
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

        public static SafeHikCentralDiagnostics FromBody(
            string endpointPath,
            Guid correlationId,
            HttpStatusCode? httpStatusCode,
            string? body,
            TimeSpan elapsed)
        {
            var envelope = TryReadEnvelope(body);

            return new SafeHikCentralDiagnostics(
                endpointPath,
                correlationId,
                httpStatusCode,
                envelope?.Code,
                envelope?.Message,
                elapsed);
        }

        public SafeHikCentralDiagnostics WithElapsed(TimeSpan elapsed)
        {
            return this with { Elapsed = elapsed };
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

internal sealed class LiveHikCentralFactAttribute : FactAttribute
{
    public LiveHikCentralFactAttribute(bool requireTestIdentifier = false)
    {
        Skip = LiveHikCentralEnvironment.Evaluate(
            Environment.GetEnvironmentVariable,
            requireTestIdentifier).SkipReason;
    }
}

internal static class LiveHikCentralEnvironment
{
    public const string RunFlagName = "EXITPASS_RUN_LIVE_HIKCENTRAL_TESTS";
    public const string BaseUrlName = "HIKCENTRAL__BASEURL";
    public const string AppKeyName = "HIKCENTRAL__APPKEY";
    public const string AppSecretName = "HIKCENTRAL__APPSECRET";
    public const string UserIdName = "HIKCENTRAL__USERID";
    public const string TestPlateLicenseName = "HIKCENTRAL__TEST_PLATE_LICENSE";
    public const string TestCardNumName = "HIKCENTRAL__TEST_CARD_NUM";

    private static readonly string[] RequiredVariableNames =
    [
        BaseUrlName,
        AppKeyName,
        AppSecretName,
        UserIdName
    ];

    public static LiveHikCentralGateResult Evaluate(
        Func<string, string?> getEnvironmentVariable,
        bool requireTestIdentifier)
    {
        if (!string.Equals(
            getEnvironmentVariable(RunFlagName),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return LiveHikCentralGateResult.Skipped(
                "Set EXITPASS_RUN_LIVE_HIKCENTRAL_TESTS=true to run live HikCentral sandbox tests.");
        }

        var missing = RequiredVariableNames
            .Where(name => string.IsNullOrWhiteSpace(getEnvironmentVariable(name)))
            .ToArray();

        if (missing.Length > 0)
        {
            return LiveHikCentralGateResult.Skipped(
                $"Missing required HikCentral sandbox environment variables: {string.Join(", ", missing)}.");
        }

        if (!Uri.TryCreate(getEnvironmentVariable(BaseUrlName), UriKind.Absolute, out var _))
        {
            return LiveHikCentralGateResult.Skipped("HIKCENTRAL__BASEURL must be an absolute URL.");
        }

        if (requireTestIdentifier &&
            string.IsNullOrWhiteSpace(getEnvironmentVariable(TestPlateLicenseName)) &&
            string.IsNullOrWhiteSpace(getEnvironmentVariable(TestCardNumName)))
        {
            return LiveHikCentralGateResult.Skipped(
                $"Set {TestPlateLicenseName} or {TestCardNumName} to run live HikCentral calculate tests.");
        }

        return LiveHikCentralGateResult.Runnable();
    }

    public static LiveHikCentralSettings GetRequired(bool requireTestIdentifier = false)
    {
        var gate = Evaluate(Environment.GetEnvironmentVariable, requireTestIdentifier);
        if (!gate.CanRun)
        {
            throw new InvalidOperationException(gate.SkipReason);
        }

        return new LiveHikCentralSettings(
            new Uri(Environment.GetEnvironmentVariable(BaseUrlName)!, UriKind.Absolute),
            Environment.GetEnvironmentVariable(AppKeyName)!,
            Environment.GetEnvironmentVariable(AppSecretName)!,
            Environment.GetEnvironmentVariable(UserIdName)!,
            Normalize(Environment.GetEnvironmentVariable(TestPlateLicenseName)),
            Normalize(Environment.GetEnvironmentVariable(TestCardNumName)));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal sealed record LiveHikCentralGateResult(bool CanRun, string? SkipReason)
{
    public static LiveHikCentralGateResult Runnable()
    {
        return new LiveHikCentralGateResult(true, null);
    }

    public static LiveHikCentralGateResult Skipped(string reason)
    {
        return new LiveHikCentralGateResult(false, reason);
    }
}

internal sealed record LiveHikCentralSettings(
    Uri BaseUri,
    string AppKey,
    string AppSecret,
    string UserId,
    string? TestPlateLicense,
    string? TestCardNum);
