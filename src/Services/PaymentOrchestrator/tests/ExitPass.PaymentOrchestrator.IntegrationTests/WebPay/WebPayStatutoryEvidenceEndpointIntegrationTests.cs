using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.WebPay;

public sealed class WebPayStatutoryEvidenceEndpointIntegrationTests
    : IClassFixture<PaymentOrchestratorWebApplicationFactory>
{
    private static readonly Guid DecisionCommandId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid EvidenceSetReference = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid EvidenceItemReference = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid UploadSessionReference = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid CorrelationId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private readonly PaymentOrchestratorWebApplicationFactory _factory;

    public WebPayStatutoryEvidenceEndpointIntegrationTests(PaymentOrchestratorWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bootstrap_ReturnsBrowserSafeI016Contract()
    {
        var state = new EvidenceEndpointState();
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(
            "/v1/webpay/statutory-discounts/evidence/bootstrap",
            new WebPayStatutoryEvidenceBootstrapRequest { StatutoryDiscountDecisionCommandId = DecisionCommandId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("REQUIRED_NOT_STARTED", document.RootElement.GetProperty("lifecycleClassification").GetString());
        Assert.Equal("image/jpeg", document.RootElement.GetProperty("allowedContentTypes")[0].GetString());
        Assert.False(document.RootElement.TryGetProperty("sourceChannel", out _));
        Assert.False(document.RootElement.TryGetProperty("readyForAptPreCash", out _));
        Assert.False(document.RootElement.TryGetProperty("objectKey", out _));
        Assert.False(document.RootElement.TryGetProperty("bucket", out _));
        Assert.False(document.RootElement.TryGetProperty("providerUrl", out _));
        Assert.Equal(DecisionCommandId, state.BootstrapRequest!.StatutoryDiscountDecisionCommandId);
    }

    [Fact]
    public async Task UploadSession_ReturnsOnlyOpaqueAuthorization()
    {
        var state = new EvidenceEndpointState();
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(
            "/v1/webpay/statutory-discounts/evidence/upload-sessions",
            new WebPayStatutoryEvidenceUploadSessionRequest
            {
                EvidenceSetReference = EvidenceSetReference,
                EvidenceItemReference = EvidenceItemReference,
                DeclaredContentType = "image/jpeg",
                DeclaredContentLength = 4,
                DeclaredChecksumSha256 = new string('a', 64)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(UploadSessionReference, document.RootElement.GetProperty("opaqueUploadSessionReference").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("objectKey", out _));
        Assert.False(document.RootElement.TryGetProperty("storageEndpoint", out _));
        Assert.False(document.RootElement.TryGetProperty("headers", out _));
    }

    [Fact]
    public async Task Upload_StreamsRequestBodyThroughOpaqueSameOriginRoute()
    {
        var state = new EvidenceEndpointState();
        using var client = CreateClient(state);
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Headers.ContentLength = 4;
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/v1/webpay/statutory-discounts/evidence/upload-sessions/{UploadSessionReference:D}") { Content = content };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", state.UploadContentType);
        Assert.Equal(4, state.UploadContentLength);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, state.UploadBytes);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Bootstrap_AuthenticationOrAuthorizationFailure_ReturnsSafeUnavailable(int statusCode)
    {
        var state = new EvidenceEndpointState
        {
            ChannelResult = CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(
                new CentralPmsWebPayError(statusCode, "INTERNAL_POLICY_statutory-discounts.evidence.capture.webpay", "service identity rejected by internal policy", false, CorrelationId))
        };
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(
            "/v1/webpay/statutory-discounts/evidence/bootstrap",
            new WebPayStatutoryEvidenceBootstrapRequest { StatutoryDiscountDecisionCommandId = DecisionCommandId });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("WEBPAY_STATUTORY_EVIDENCE_SERVICE_UNAVAILABLE", body);
        Assert.DoesNotContain("service identity", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("statutory-discounts.evidence.capture.webpay", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INTERNAL_POLICY", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalize_Conflict_ReturnsSafeConflictWithoutRawUpstreamBody()
    {
        var state = new EvidenceEndpointState
        {
            ChannelResult = CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(
                new CentralPmsWebPayError(409, "REVIEW_LOCKED", "database evidence row locked by reviewer 123", false, CorrelationId))
        };
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(
            $"/v1/webpay/statutory-discounts/evidence/upload-sessions/{UploadSessionReference:D}/finalize",
            new WebPayStatutoryEvidenceFinalizeRequest());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("WEBPAY_STATUTORY_EVIDENCE_CONFLICT", body);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewer 123", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(EvidenceEndpointState state)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralPmsWebPayStatutoryEvidenceClient>();
                services.AddSingleton<ICentralPmsWebPayStatutoryEvidenceClient>(state);
            });
        }).CreateClient();
    }

    private sealed class EvidenceEndpointState : ICentralPmsWebPayStatutoryEvidenceClient
    {
        public CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel> ChannelResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Success(new CentralPmsStatutoryEvidenceChannel(
                "FOUND", false, null, CorrelationId, "WEBPAY", true, EvidenceSetReference, EvidenceItemReference,
                new[] { "image/jpeg", "image/png" }, 5_000_000, 1920, 1080, 2_073_600,
                "STATUTORY_ID", "ENTITLEMENT_ID_FRONT", "REQUIRED_NOT_STARTED", "REPLACEMENT_ALLOWED",
                false, false, "EVIDENCE_REQUIRED", DateTimeOffset.Parse("2026-08-05T09:00:00Z")));

        public CentralPmsStatutoryEvidenceBootstrapRequest? BootstrapRequest { get; private set; }
        public byte[]? UploadBytes { get; private set; }
        public string? UploadContentType { get; private set; }
        public long UploadContentLength { get; private set; }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> BootstrapAsync(
            CentralPmsStatutoryEvidenceBootstrapRequest request, Guid correlationId, CancellationToken cancellationToken)
        {
            BootstrapRequest = request;
            return Task.FromResult(ChannelResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> GetStatusAsync(
            Guid? statutoryDiscountDecisionCommandId, Guid? evidenceSetReference, Guid correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelResult);

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> CreateUploadSessionAsync(
            CentralPmsStatutoryEvidenceUploadSessionRequest request, Guid correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(UploadResult());

        public async Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> UploadAsync(
            Guid opaqueUploadSessionReference, string contentType, long contentLength, Stream content, Guid correlationId, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            UploadBytes = buffer.ToArray();
            UploadContentType = contentType;
            UploadContentLength = contentLength;
            return UploadResult();
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> FinalizeAsync(
            Guid opaqueUploadSessionReference, string? clientOperationKey, Guid correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelResult);

        private static CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession> UploadResult() =>
            CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Success(new CentralPmsStatutoryEvidenceUploadSession(
                "UPLOAD_AUTHORIZED", false, null, CorrelationId, UploadSessionReference, "PUT",
                DateTimeOffset.Parse("2026-08-05T09:05:00Z"), "image/jpeg", 5_000_000));
    }
}
