using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class HikCentralSandboxValidationHarnessTests
{
    private static readonly Guid CorrelationId = Guid.Parse("c1000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task DisabledByDefault_RejectsWithoutTransportCallOrAudit()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("{\"code\":\"0\",\"msg\":\"Success\"}"));
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var harness = CreateHarness(
            handler,
            audit,
            Options(sandboxEnabled: false),
            GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.False(report.Executed);
        Assert.Equal("HIKCENTRAL_SANDBOX_VALIDATION_DISABLED", report.ResultCode);
        Assert.Equal(0, handler.SendCount);
        Assert.Empty(audit.Records);
        AssertSafe(report, "test-secret");
    }

    [Theory]
    [InlineData(GateActionAdapterMode.NoOp, "HIKCENTRAL_SANDBOX_VALIDATION_REQUIRES_LIVE_MODE")]
    [InlineData(GateActionAdapterMode.HikCentralFake, "HIKCENTRAL_SANDBOX_VALIDATION_REQUIRES_LIVE_MODE")]
    public async Task Harness_RequiresExplicitLiveMode(
        GateActionAdapterMode mode,
        string expectedResultCode)
    {
        using var handler = new CapturingHandler(_ => JsonResponse("{\"code\":\"0\",\"msg\":\"Success\"}"));
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var harness = CreateHarness(handler, audit, Options(), mode);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.False(report.Executed);
        Assert.Equal(expectedResultCode, report.ResultCode);
        Assert.Equal(0, handler.SendCount);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task Harness_WhenLiveTransportDisabled_RejectsWithSecretFreeConfigError()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("{\"code\":\"0\",\"msg\":\"Success\"}"));
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var options = Options();
        options.LiveTransportEnabled = false;
        var harness = CreateHarness(handler, audit, options, GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.False(report.Executed);
        Assert.Equal("HIKCENTRAL_SANDBOX_VALIDATION_CONFIG_INVALID", report.ResultCode);
        Assert.Contains("HIKCENTRAL_LIVE_TRANSPORT_DISABLED", report.DiagnosticMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret", report.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCount);
    }

    [Theory]
    [InlineData(null, "test-ak", "test-secret", "exitpass-gate-integration", 10, "HIKCENTRAL_BASE_URL_INVALID")]
    [InlineData("https://hikcentral.test", null, "test-secret", "exitpass-gate-integration", 10, "HIKCENTRAL_APP_KEY_REQUIRED")]
    [InlineData("https://hikcentral.test", "test-ak", null, "exitpass-gate-integration", 10, "HIKCENTRAL_APP_SECRET_REQUIRED")]
    [InlineData("https://hikcentral.test", "test-ak", "test-secret", "", 10, "HIKCENTRAL_USER_ID_INVALID")]
    [InlineData("https://hikcentral.test", "test-ak", "test-secret", "exitpass-gate-integration", 0, "HIKCENTRAL_REQUEST_TIMEOUT_SECONDS_INVALID")]
    public async Task Harness_WhenConfigMissing_RejectsDeterministically(
        string? baseUrl,
        string? appKey,
        string? appSecret,
        string? userId,
        int timeoutSeconds,
        string expectedError)
    {
        using var handler = new CapturingHandler(_ => JsonResponse("{\"code\":\"0\",\"msg\":\"Success\"}"));
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var options = new HikCentralGateActionOptions
        {
            BaseUrl = baseUrl,
            AppKey = appKey,
            AppSecret = appSecret,
            UserId = userId,
            LiveTransportEnabled = true,
            SandboxValidationEnabled = true,
            RequestTimeoutSeconds = timeoutSeconds,
            TransportMode = "Live"
        };
        var harness = CreateHarness(handler, audit, options, GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.False(report.Executed);
        Assert.Equal("HIKCENTRAL_SANDBOX_VALIDATION_CONFIG_INVALID", report.ResultCode);
        Assert.Contains(expectedError, report.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task ValidSandboxConfig_PerformsOneSignedRequestAndWritesAudit()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("""
            {"code":"0","msg":"Success","data":[[{"doorIndexCode":"sandbox-door-01","controlResultCode":0,"controlResultDesc":"Success"}]]}
            """));
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var harness = CreateHarness(handler, audit, Options(), GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.True(report.Executed);
        Assert.True(report.Succeeded);
        Assert.Equal("HIKCENTRAL_GATE_ACTION_SUCCEEDED", report.ResultCode);
        Assert.Equal(1, handler.SendCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, handler.PathAndQuery);
        Assert.Equal("test-ak", handler.Headers["X-Ca-Key"]);
        Assert.True(handler.Headers.ContainsKey("X-Ca-Signature"));
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", handler.Headers["X-Ca-Signature-Headers"]);
        Assert.Equal("*/*", handler.Accept);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal(CorrelationId, report.CorrelationId);
        Assert.NotNull(report.AuditId);
        Assert.Equal("0", report.VendorResponseCode);
        Assert.Equal("Success", report.VendorResponseMessage);

        var record = Assert.Single(audit.Records);
        Assert.Equal(report.AuditId, record.AuditId);
        Assert.Equal(CorrelationId, record.RequestCorrelationId);
        Assert.Equal("sandbox-door-01", record.DoorIndexCode);
        Assert.Equal(nameof(HikCentralGateActionOutcome.Succeeded), record.OutcomeCategory);
        Assert.Matches("^[0-9a-f]{64}$", record.RequestBodySha256);
        AssertSafe(report, "test-secret");
        Assert.DoesNotContain(handler.Headers["X-Ca-Signature"], JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task SandboxValidation_ReturnsSanitizedFailureReport(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        HikCentralGateActionOutcome expectedOutcome,
        bool retryable,
        bool terminal)
    {
        using var handler = new CapturingHandler(responseFactory);
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var harness = CreateHarness(handler, audit, Options(), GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.True(report.Executed);
        Assert.False(report.Succeeded);
        Assert.Equal(expectedOutcome, report.OutcomeCategory);
        Assert.Equal(retryable, report.Retryable);
        Assert.Equal(terminal, report.TerminalFailure);
        Assert.NotNull(report.AuditId);
        Assert.Single(audit.Records);
        AssertSafe(report, "test-secret");
    }

    [Fact]
    public async Task TimeoutValidation_ReturnsRetryableSanitizedReport()
    {
        using var handler = new CapturingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return JsonResponse("{}");
        });
        var options = Options();
        options.RequestTimeoutSeconds = 1;
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var harness = CreateHarness(handler, audit, options, GateActionAdapterMode.HikCentralLive);

        var report = await harness.ValidateGateActionAsync(Request(), CancellationToken.None);

        Assert.True(report.Executed);
        Assert.False(report.Succeeded);
        Assert.Equal(HikCentralGateActionOutcome.Timeout, report.OutcomeCategory);
        Assert.True(report.Retryable);
        Assert.False(report.TerminalFailure);
        Assert.NotNull(report.AuditId);
        Assert.True(audit.Records.Single().TimeoutOccurred);
        AssertSafe(report, "test-secret");
    }

    public static IEnumerable<object[]> FailureCases()
    {
        yield return
        [
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => JsonResponse("{\"code\":\"SIGNATURE_INVALID\",\"msg\":\"Signature verification failed.\"}", HttpStatusCode.Unauthorized)),
            HikCentralGateActionOutcome.Unauthorized,
            false,
            true
        ];
        yield return
        [
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("network unavailable")),
            HikCentralGateActionOutcome.VendorUnavailable,
            true,
            false
        ];
        yield return
        [
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => JsonResponse("{\"code\":\"INVALID_RESOURCE\",\"msg\":\"Door resource not found.\"}", HttpStatusCode.BadRequest)),
            HikCentralGateActionOutcome.InvalidRequest,
            false,
            true
        ];
    }

    private static HikCentralSandboxValidationHarness CreateHarness(
        HttpMessageHandler handler,
        InMemoryHikCentralGateActionAuditRecorder audit,
        HikCentralGateActionOptions options,
        GateActionAdapterMode mode)
    {
        var transport = new LiveHikCentralGateActionTransport(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://hikcentral.test"),
                Timeout = Timeout.InfiniteTimeSpan
            },
            options);
        var adapter = new HikCentralConsumedAuthorizationGateActionAdapter(
            new HikCentralRequestSigner(
                options,
                new FixedClock(DateTimeOffset.Parse("2026-06-01T08:00:00Z")),
                new FixedNonceProvider("fixed-nonce")),
            transport,
            audit);

        return new HikCentralSandboxValidationHarness(
            mode,
            options,
            adapter,
            new InMemoryHikCentralSandboxValidationCommandRecorder());
    }

    private static HikCentralGateActionOptions Options(bool sandboxEnabled = true) =>
        new()
        {
            BaseUrl = "https://hikcentral.test",
            AppKey = "test-ak",
            AppSecret = "test-secret",
            UserId = "exitpass-gate-integration",
            LiveTransportEnabled = true,
            SandboxValidationEnabled = sandboxEnabled,
            RequestTimeoutSeconds = 10,
            TransportMode = "Live"
        };

    private static HikCentralSandboxValidationRequest Request() =>
        new(
            "sandbox-door-01",
            HikCentralDoorControlType.Open,
            HikCentralDoorControlDirection.Exit,
            "Controlled sandbox validation",
            "operator@example.test",
            CorrelationId,
            ConfirmLiveAction: true);

    private static void AssertSafe(HikCentralSandboxValidationReport report, string secret)
    {
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Ca-Signature", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixed-nonce", json, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FixedClock(DateTimeOffset utcNow) : IHikCentralClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedNonceProvider(string nonce) : IHikCentralNonceProvider
    {
        public string CreateNonce() => nonce;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int SendCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? Accept { get; private set; }

        public string? ContentType { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Accept = request.Headers.Accept.SingleOrDefault()?.ToString();
            ContentType = request.Content?.Headers.ContentType?.ToString();
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = header.Value.Single();
            }

            return await _handler(request, cancellationToken);
        }
    }
}

#pragma warning restore CS1591
