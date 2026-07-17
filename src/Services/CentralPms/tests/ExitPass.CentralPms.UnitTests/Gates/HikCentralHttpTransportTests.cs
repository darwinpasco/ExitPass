using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for the focused HikCentral HTTP transport boundary.
/// </summary>
public sealed class HikCentralHttpTransportTests
{
    private static readonly Uri BaseAddress = new("https://hikcentral.test:8443/");

    [Fact]
    public async Task SendAsync_WithValidRequest_SendsExactlyOnceAndPreservesRequest()
    {
        using var request = BuildSignedRequest(out var fixture);
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0","msg":"Success"}"""));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Same(request, handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://hikcentral.test:8443/artemis/api/acs/v1/door/doControl", handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Equal<byte>(fixture.Plan.BodyUtf8, handler.LastRequestBody!);
        Assert.Equal<byte>(fixture.Plan.BodyUtf8, await request.Content!.ReadAsByteArrayAsync());
        Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
        Assert.Equal(fixture.Material.ContentMd5, request.Content.Headers.GetValues("Content-MD5").Single());
        Assert.Equal("*/*", request.Headers.GetValues("Accept").Single());
        Assert.Equal("test-client-key", request.Headers.GetValues("X-Ca-Key").Single());
        Assert.Equal("fixed-nonce", request.Headers.GetValues("X-Ca-Nonce").Single());
        Assert.Equal("1479968678000", request.Headers.GetValues("X-Ca-Timestamp").Single());
        Assert.Equal("HmacSHA256", request.Headers.GetValues("X-Ca-Signature-Method").Single());
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", request.Headers.GetValues("X-Ca-Signature-Headers").Single());
        Assert.Equal(fixture.Signature.EncodedSignatureValue, request.Headers.GetValues("X-Ca-Signature").Single());
        Assert.Equal(200, result.HttpStatusCode);
        Assert.True(result.IsSuccessStatusCode);
        Assert.Equal(HikCentralHttpTransportOutcome.Succeeded, result.Outcome);
        Assert.False(result.TimedOut);
        Assert.False(result.TransportFailure);
        Assert.False(result.VendorUnavailable);
        Assert.False(result.ResponseBodyTooLarge);
        Assert.Equal("0", result.VendorResultCode);
        Assert.Equal("Success", result.VendorResultMessage);
        Assert.Equal(Sha256Hex(Encoding.UTF8.GetBytes("""{"code":"0","msg":"Success"}""")), result.ResponseBodySha256);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HikCentralHttpTransportOutcome.ClientError, false, false)]
    [InlineData(HttpStatusCode.Unauthorized, HikCentralHttpTransportOutcome.Unauthorized, false, false)]
    [InlineData(HttpStatusCode.Forbidden, HikCentralHttpTransportOutcome.Forbidden, false, false)]
    [InlineData(HttpStatusCode.RequestTimeout, HikCentralHttpTransportOutcome.RequestTimeout, true, false)]
    [InlineData((HttpStatusCode)429, HikCentralHttpTransportOutcome.Throttled, false, false)]
    [InlineData(HttpStatusCode.InternalServerError, HikCentralHttpTransportOutcome.VendorFailure, false, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HikCentralHttpTransportOutcome.VendorFailure, false, true)]
    public async Task SendAsync_ClassifiesHttpStatusCodes(
        HttpStatusCode statusCode,
        HikCentralHttpTransportOutcome expectedOutcome,
        bool expectedTimedOut,
        bool expectedVendorUnavailable)
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler(_ => JsonResponse(statusCode, """{"code":"ERR","msg":"Failure"}"""));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal((int)statusCode, result.HttpStatusCode);
        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedTimedOut, result.TimedOut);
        Assert.Equal(expectedVendorUnavailable, result.VendorUnavailable);
        Assert.False(result.TransportFailure);
        Assert.Equal("ERR", result.VendorResultCode);
        Assert.Equal("Failure", result.VendorResultMessage);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIsMalformedJson_ReturnsMalformedClassification()
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{not-json"));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(HikCentralHttpTransportOutcome.MalformedResponse, result.Outcome);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Null(result.VendorResultCode);
        Assert.Null(result.VendorResultMessage);
    }

    [Fact]
    public async Task SendAsync_WhenResponseBodyIsEmpty_ReturnsHttpClassificationWithEmptyHash()
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(HikCentralHttpTransportOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ResponseBodyByteCount);
        Assert.Equal(Sha256Hex([]), result.ResponseBodySha256);
    }

    [Fact]
    public async Task SendAsync_WhenResponseBodyExceedsLimit_StopsAndClassifiesSafely()
    {
        using var request = BuildSignedRequest(out _);
        var body = new string('x', 32);
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(
            client,
            new HikCentralHttpTransportOptions(MaxResponseBodyBytes: 8));

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(HikCentralHttpTransportOutcome.ResponseBodyTooLarge, result.Outcome);
        Assert.True(result.ResponseBodyTooLarge);
        Assert.True(result.ResponseBodyByteCount > 8);
        Assert.Null(result.ResponseBodySha256);
        Assert.Null(result.VendorResultCode);
        Assert.Null(result.VendorResultMessage);
    }

    [Fact]
    public async Task SendAsync_WhenCancellationAlreadyRequested_DoesNotCallHandler()
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0"}"""));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(request, cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenCallerCancelsDuringSend_PropagatesCancellation()
    {
        using var request = BuildSignedRequest(out _);
        using var cancellation = new CancellationTokenSource();
        var handler = new CapturingHandler(async token =>
        {
            cancellation.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return JsonResponse(HttpStatusCode.OK, """{"code":"0"}""");
        });
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(request, cancellation.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenHttpClientTimesOut_ReturnsTimeoutClassification()
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return JsonResponse(HttpStatusCode.OK, """{"code":"0"}""");
        });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(25)
        };
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(HikCentralHttpTransportOutcome.TimedOut, result.Outcome);
        Assert.True(result.TimedOut);
        Assert.False(result.TransportFailure);
        Assert.Null(result.HttpStatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenHttpRequestFails_ReturnsTransportFailureWithoutRetry()
    {
        using var request = BuildSignedRequest(out _);
        var handler = new CapturingHandler((HttpRequestMessage _) => throw new HttpRequestException("simulated connection failure"));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var result = await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HikCentralHttpTransportOutcome.TransportFailure, result.Outcome);
        Assert.True(result.TransportFailure);
        Assert.True(result.VendorUnavailable);
        Assert.Null(result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_DisposesResponseButDoesNotDisposeInputRequest()
    {
        using var request = BuildSignedRequest(out var fixture);
        var responseContent = new TrackingByteArrayContent(Encoding.UTF8.GetBytes("""{"code":"0"}"""));
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent
        });
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        await transport.SendAsync(request, CancellationToken.None);

        Assert.True(responseContent.Disposed);
        Assert.Equal<byte>(fixture.Plan.BodyUtf8, await request.Content!.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task SendAsync_WhenRequestIsInvalid_RejectsBeforeSending(
        Action<HttpRequestMessage> mutate,
        string expectedErrorCode)
    {
        using var request = BuildSignedRequest(out _);
        mutate(request);
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0"}"""));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => transport.SendAsync(request, CancellationToken.None));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenRejected_DoesNotExposeSensitiveHeaderValues()
    {
        using var request = BuildSignedRequest(out var fixture);
        request.Headers.Remove("X-Ca-Signature-Headers");
        request.Headers.TryAddWithoutValidation("X-Ca-Signature-Headers", "x-ca-key,x-ca-nonce,x-ca-timestamp,x-ca-secret");
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0"}"""));
        using var client = new HttpClient(handler);
        var transport = new HikCentralHttpTransport(client);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => transport.SendAsync(request, CancellationToken.None));

        Assert.DoesNotContain(fixture.Signature.EncodedSignatureValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixed-nonce", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultModel_ContainsOnlySafeMetadata()
    {
        var propertyNames = typeof(HikCentralHttpTransportResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, IsForbiddenResultProperty);
        Assert.DoesNotContain(Enum.GetNames<HikCentralHttpTransportOutcome>(), name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Enum.GetNames<HikCentralHttpTransportOutcome>(), name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Transport_DoesNotDeclareConfigurationCredentialDatabaseCommandAuditWorkerOrAdapterDependencies()
    {
        var constructorParameters = typeof(HikCentralHttpTransport)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralHttpTransport)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.Contains(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(constructorParameters, IsForbiddenRuntimeDependency);
        Assert.DoesNotContain(fieldTypes, IsForbiddenRuntimeDependency);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new Action<HttpRequestMessage>(request => request.RequestUri = new Uri("http://hikcentral.test/artemis/api/acs/v1/door/doControl")), "HIKCENTRAL_HTTP_REQUEST_HTTPS_REQUIRED"];
        yield return [new Action<HttpRequestMessage>(request => request.RequestUri = new Uri("https://user:pass@hikcentral.test/artemis/api/acs/v1/door/doControl")), "HIKCENTRAL_HTTP_REQUEST_URI_CREDENTIALS_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.RequestUri = new Uri("https://hikcentral.test/artemis/api/acs/v1/door/doControl?x=1")), "HIKCENTRAL_HTTP_REQUEST_URI_QUERY_FRAGMENT_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.RequestUri = new Uri("https://hikcentral.test/artemis/api/acs/v1/door/other")), "HIKCENTRAL_HTTP_REQUEST_PATH_UNAPPROVED"];
        yield return [new Action<HttpRequestMessage>(request => request.Method = HttpMethod.Get), "HIKCENTRAL_HTTP_REQUEST_METHOD_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.Content = null), "HIKCENTRAL_HTTP_REQUEST_CONTENT_REQUIRED"];
        yield return [new Action<HttpRequestMessage>(request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret")), "HIKCENTRAL_HTTP_REQUEST_SECRET_HEADER_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.Headers.TryAddWithoutValidation("Cookie", "session=secret")), "HIKCENTRAL_HTTP_REQUEST_SECRET_HEADER_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.Headers.Remove("X-Ca-Key")), "HIKCENTRAL_HTTP_REQUEST_HEADER_REQUIRED"];
        yield return [new Action<HttpRequestMessage>(request =>
        {
            request.Headers.Remove("X-Ca-Nonce");
            request.Headers.TryAddWithoutValidation("X-Ca-Nonce", "fixed-nonce\r\nInjected: true");
        }), "HIKCENTRAL_HTTP_REQUEST_HEADER_INVALID"];
        yield return [new Action<HttpRequestMessage>(request =>
        {
            request.Headers.Remove("X-Ca-Signature-Method");
            request.Headers.TryAddWithoutValidation("X-Ca-Signature-Method", "HmacSHA1");
        }), "HIKCENTRAL_HTTP_REQUEST_HEADER_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request =>
        {
            request.Headers.Remove("X-Ca-Signature-Headers");
            request.Headers.TryAddWithoutValidation("X-Ca-Signature-Headers", "x-ca-key,x-ca-nonce,x-ca-timestamp,x-ca-extra");
        }), "HIKCENTRAL_HTTP_REQUEST_HEADER_UNSUPPORTED"];
        yield return [new Action<HttpRequestMessage>(request => request.Content!.Headers.Remove("Content-MD5")), "HIKCENTRAL_HTTP_REQUEST_CONTENT_HEADER_REQUIRED"];
        yield return [new Action<HttpRequestMessage>(request => request.Content!.Headers.ContentType = new MediaTypeHeaderValue("text/plain")), "HIKCENTRAL_HTTP_REQUEST_CONTENT_TYPE_UNSUPPORTED"];
    }

    private static HttpRequestMessage BuildSignedRequest(out SignedRequestFixture fixture)
    {
        fixture = ValidFixture();
        return new HikCentralSignedHttpRequestBuilder().Build(BaseAddress, fixture.Plan, fixture.Material, fixture.Signature);
    }

    private static SignedRequestFixture ValidFixture()
    {
        var request = new HikCentralGateActionRequest(
            GateCommandId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            GateAuthorizationConsumptionId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ExitAuthorizationId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            GateDeviceId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            VendorSystemId: Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            LaneId: Guid.Parse("11111111-0000-0000-0000-000000000001"),
            TargetResourceCode: "EXIT-GATE-01",
            VendorOperation: HikCentralGateActionConstants.OpenGateOperation,
            CorrelationId: Guid.Parse("22222222-0000-0000-0000-000000000001"),
            RequestedAt: DateTimeOffset.Parse("2026-07-17T08:00:00Z"));
        var plan = new HikCentralGateActionRequestPlanBuilder()
            .Build(request, HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE"));
        var material = new HikCentralRequestSigningMaterialBuilder()
            .Build(new HikCentralSigningMaterialInput(
                plan,
                "test-client-key",
                "1479968678000",
                "fixed-nonce",
                "HmacSHA256"));
        var signature = new HikCentralRequestSignatureCalculator()
            .Calculate(material, Encoding.UTF8.GetBytes("test-app-secret"));

        return new SignedRequestFixture(plan, material, signature);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsForbiddenResultProperty(string propertyName) =>
        propertyName.Contains("Header", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("XCa", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("AppSecret", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("Signature", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("Cookie", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("RequestBody", StringComparison.OrdinalIgnoreCase);

    private static bool IsForbiddenRuntimeDependency(Type type)
    {
        if (type == typeof(HttpClient) ||
            type == typeof(HikCentralHttpTransportOptions) ||
            type == typeof(TimeProvider))
        {
            return false;
        }

        return type.Namespace?.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
               type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Adapter", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Worker", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Signer", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public CapturingHandler(Func<CancellationToken, Task<HttpResponseMessage>> handler)
            : this((_, cancellationToken) => handler(cancellationToken))
        {
        }

        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public byte[]? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            return await _handler(request, cancellationToken);
        }
    }

    private sealed class TrackingByteArrayContent : ByteArrayContent
    {
        public TrackingByteArrayContent(byte[] content)
            : base(content)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record SignedRequestFixture(
        HikCentralGateActionRequestPlan Plan,
        HikCentralSigningMaterial Material,
        HikCentralRequestSignature Signature);
}
