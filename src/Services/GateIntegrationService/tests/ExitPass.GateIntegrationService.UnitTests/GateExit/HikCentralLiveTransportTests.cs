using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class HikCentralLiveTransportTests
{
    [Fact]
    public async Task SendAsync_SendsSignedPostRequestToDoorControlPath()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("""
            {"code":"0","msg":"Success","data":[[{"doorIndexCode":"exit-gate-01","controlResultCode":0,"controlResultDesc":"Success"}]]}
            """));
        var transport = CreateTransport(handler);
        var signed = CreateSignedRequest();

        await transport.SendAsync(signed, CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/artemis/api/acs/v1/door/doControl", handler.Request.RequestUri!.PathAndQuery);
        Assert.Equal(signed.Body, handler.Body);
        Assert.Equal("test-ak", handler.Request.Headers.GetValues("X-Ca-Key").Single());
        Assert.Equal(signed.Headers["X-Ca-Signature"], handler.Request.Headers.GetValues("X-Ca-Signature").Single());
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", handler.Request.Headers.GetValues("X-Ca-Signature-Headers").Single());
        Assert.Equal("*/*", handler.Request.Headers.Accept.Single().ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.ToString());
        Assert.Equal(
            signed.Headers["Content-MD5"],
            handler.Request.Content.Headers.GetValues("Content-MD5").Single());
    }

    [Fact]
    public async Task SendAsync_WhenSuccessEnvelope_ReturnsParsedEnvelope()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("""
            {"code":"0","msg":"Success","data":[[{"doorIndexCode":"exit-gate-01","controlResultCode":0,"controlResultDesc":"Success"}]]}
            """));
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("0", result.Envelope!.Code);
        Assert.Equal("Success", result.Envelope.Message);
        var door = Assert.Single(result.Envelope.DoorResults);
        Assert.Equal("exit-gate-01", door.DoorIndexCode);
        Assert.Equal(0, door.ControlResultCode);
        Assert.Equal(HikCentralGateActionOutcome.Succeeded, classified.Outcome);
    }

    [Fact]
    public async Task SendAsync_WhenVendorFailureEnvelope_ReturnsParsedFailureForClassifier()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("""
            {"code":"INVALID_RESOURCE","msg":"Door resource not found.","data":[[{"doorIndexCode":"missing","controlResultCode":404,"controlResultDesc":"Not found"}]]}
            """));
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.Equal("INVALID_RESOURCE", result.Envelope!.Code);
        Assert.Equal(HikCentralGateActionOutcome.InvalidRequest, classified.Outcome);
        Assert.True(classified.TerminalFailure);
    }

    [Fact]
    public async Task SendAsync_WhenNonSuccessStatus_ReturnsStatusForClassifier()
    {
        using var handler = new CapturingHandler(_ => JsonResponse(
            "{\"code\":\"SIGNATURE_INVALID\",\"msg\":\"Signature verification failed.\"}",
            HttpStatusCode.Unauthorized));
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.Equal(401, result.HttpStatusCode);
        Assert.Equal(HikCentralGateActionOutcome.Unauthorized, classified.Outcome);
        Assert.True(classified.TerminalFailure);
    }

    [Fact]
    public async Task SendAsync_WhenTimeout_ReturnsTimedOutTransportResult()
    {
        using var handler = new CapturingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return JsonResponse("{}");
        });
        var transport = CreateTransport(handler, timeoutSeconds: 1);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.True(result.TimedOut);
        Assert.Equal(HikCentralGateActionOutcome.Timeout, classified.Outcome);
        Assert.True(classified.Retryable);
    }

    [Fact]
    public async Task SendAsync_WhenNetworkFailure_ReturnsVendorUnavailableTransportResult()
    {
        using var handler = new CapturingHandler(_ => throw new HttpRequestException("network unavailable"));
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.True(result.VendorUnavailable);
        Assert.Equal(HikCentralGateActionOutcome.VendorUnavailable, classified.Outcome);
        Assert.True(classified.Retryable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task SendAsync_WhenBodyMalformedOrEmpty_ReturnsDeterministicUnknown(string body)
    {
        using var handler = new CapturingHandler(_ => JsonResponse(body));
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(CreateSignedRequest(), CancellationToken.None);
        var classified = HikCentralGateActionResultClassifier.Classify(result);

        Assert.Equal(HikCentralGateActionOutcome.Unknown, classified.Outcome);
        Assert.True(classified.Retryable);
    }

    [Fact]
    public void ValidateForLiveTransport_WhenHardGateDisabled_ReturnsDisabledErrorOnly()
    {
        var options = CreateOptions();
        options.LiveTransportEnabled = false;

        var errors = options.ValidateForLiveTransport();

        Assert.Equal(["HIKCENTRAL_LIVE_TRANSPORT_DISABLED"], errors);
    }

    [Fact]
    public void ValidateForLiveTransport_WhenRequiredValuesMissing_ReturnsSecretFreeErrors()
    {
        var options = new HikCentralGateActionOptions
        {
            LiveTransportEnabled = true,
            RequestTimeoutSeconds = 0,
            UserId = "exitpass-gate-integration"
        };

        var errors = options.ValidateForLiveTransport();

        Assert.Contains("HIKCENTRAL_BASE_URL_INVALID", errors);
        Assert.Contains("HIKCENTRAL_APP_KEY_REQUIRED", errors);
        Assert.Contains("HIKCENTRAL_APP_SECRET_REQUIRED", errors);
        Assert.Contains("HIKCENTRAL_REQUEST_TIMEOUT_SECONDS_INVALID", errors);
    }

    private static LiveHikCentralGateActionTransport CreateTransport(
        HttpMessageHandler handler,
        int timeoutSeconds = 10) =>
        new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://hikcentral.test", UriKind.Absolute),
                Timeout = Timeout.InfiniteTimeSpan
            },
            CreateOptions(timeoutSeconds));

    private static HikCentralGateActionOptions CreateOptions(int timeoutSeconds = 10) =>
        new()
        {
            BaseUrl = "https://hikcentral.test",
            AppKey = "test-ak",
            AppSecret = "test-secret",
            UserId = "exitpass-gate-integration",
            LiveTransportEnabled = true,
            RequestTimeoutSeconds = timeoutSeconds,
            TransportMode = "Live"
        };

    private static HikCentralSignedRequest CreateSignedRequest()
    {
        var body = "{\"doorIndexCodes\":[\"exit-gate-01\"],\"controlType\":2,\"controlDirection\":1}";
        var contentMd5 = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(body)));
        var canonical = HikCentralRequestSigner.BuildCanonicalRequest(
            "POST",
            HikCentralRequestSigner.DoorControlPath,
            "*/*",
            contentMd5,
            "application/json",
            new Dictionary<string, string>
            {
                ["x-ca-key"] = "test-ak",
                ["x-ca-nonce"] = "fixed-nonce",
                ["x-ca-timestamp"] = "1479968678000"
            });
        var signature = "test-signature";
        return new HikCentralSignedRequest(
            "POST",
            HikCentralRequestSigner.DoorControlPath,
            body,
            HikCentralSignedRequest.HeadersOf(new Dictionary<string, string>
            {
                ["Accept"] = "*/*",
                ["Content-MD5"] = contentMd5,
                ["Content-Type"] = "application/json",
                ["userId"] = "exitpass-gate-integration",
                ["X-Ca-Key"] = "test-ak",
                ["X-Ca-Nonce"] = "fixed-nonce",
                ["X-Ca-Timestamp"] = "1479968678000",
                ["X-Ca-Signature-Headers"] = "x-ca-key,x-ca-nonce,x-ca-timestamp",
                ["X-Ca-Signature"] = signature
            }),
            canonical,
            signature);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _handler(request, cancellationToken);
        }
    }
}

#pragma warning restore CS1591
