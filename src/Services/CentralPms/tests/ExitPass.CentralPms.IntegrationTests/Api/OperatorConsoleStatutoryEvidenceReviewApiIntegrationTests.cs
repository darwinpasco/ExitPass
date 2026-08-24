using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleStatutoryEvidenceReviewApiIntegrationTests
{
    private static readonly Guid UserId = Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceBindingId = Guid.Parse("92000000-0000-0000-0000-000000000006");
    private static readonly Guid ShiftId = Guid.Parse("92000000-0000-0000-0000-000000000007");
    private static readonly Guid DecisionId = Guid.Parse("92000000-0000-0000-0000-000000000002");
    private static readonly Guid SetReference = Guid.Parse("92000000-0000-0000-0000-000000000003");
    private static readonly Guid ItemReference = Guid.Parse("92000000-0000-0000-0000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("92000000-0000-0000-0000-000000000005");
    private static readonly byte[] JpegBytes = [0xff, 0xd8, 0xff, 0xd9];

    private static string MetadataPath => $"/v1/ops/operator-console/statutory-discounts/reviews/{DecisionId:D}/evidence";
    private static string PreviewPath => $"{MetadataPath}/preview";

    [Fact]
    public void Routes_KeepMetadataGetAndUseBodySelectedPreviewPostWithDedicatedPolicy()
    {
        using var factory = CreateFactory(new FakeReviewService());
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("/evidence", StringComparison.OrdinalIgnoreCase) == true &&
                endpoint.RoutePattern.RawText.Contains("/statutory-discounts/reviews/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Single(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/evidence", StringComparison.Ordinal))
            .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
        endpoints.Single(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/evidence/preview", StringComparison.Ordinal))
            .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Equal("POST");
        endpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()!.PolicyName ==
            OperatorConsoleStatutoryEvidenceReviewConstants.Policy);
    }

    [Theory]
    [InlineData("statutory-discounts.evidence.capture")]
    [InlineData("statutory-discounts.evidence.view")]
    [InlineData("statutory-discounts.evidence-governance.view")]
    [InlineData("statutory-discounts.evidence.scan.execute")]
    [InlineData("statutory-discounts.decision.review")]
    [InlineData("statutory-discounts.decision.approve")]
    [InlineData("statutory-discounts.evidence.capture.webpay")]
    [InlineData("statutory-discounts.evidence.capture.assisted-payment-terminal")]
    [InlineData("reconciliation.manage")]
    public async Task RelatedPermissionAlone_DoesNotGrantReviewPreview(string permission)
    {
        using var factory = CreateFactory(new FakeReviewService());
        using var client = factory.CreateClient();
        AddIdentityHeaders(client, permission);

        using var response = await client.GetAsync(MetadataPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task Metadata_AuthorizedReviewer_ReturnsReviewSafeDto()
    {
        using var factory = CreateFactory(new FakeReviewService());
        using var client = factory.CreateClient();
        AddIdentityHeaders(client);

        using var response = await client.GetAsync(MetadataPath);

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "response was {0}", responseText);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryEvidenceReviewResponse>();
        body.Should().NotBeNull();
        body!.EvidenceSetReference.Should().Be(SetReference);
        body.Items.Should().ContainSingle();
        body.Items[0].PreviewPermitted.Should().BeTrue();

        var json = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[]
                 {
                     "objectKey", "storageLocator", "bucket", "endpoint", "checksum", "signedUrl",
                     "credential", "connectionString", "scannerEndpoint", "rawProvider", "rawScanner"
                 })
        {
            json.Contains(forbidden, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task Preview_AuthorizedReviewer_StreamsInlineWithRestrictiveHeaders(string contentType)
    {
        var bytes = contentType == "image/png" ? new byte[] { 0x89, 0x50, 0x4e, 0x47 } : JpegBytes;
        var service = new FakeReviewService
        {
            PreviewResult = AcceptedPreview(contentType, bytes)
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddIdentityHeaders(client);

        using var response = await PostPreviewAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentLength.Should().Be(bytes.Length);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.Private.Should().BeTrue();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        service.StreamOutcomes.Should().ContainSingle("COMPLETED");
    }

    [Fact]
    public async Task Preview_NonEligible_ReturnsSafeConflictWithoutInternals()
    {
        using var factory = CreateFactory(new FakeReviewService
        {
            PreviewResult = new OperatorConsoleStatutoryEvidencePreviewResult(
                "REJECTED",
                "STATUTORY_EVIDENCE_VALIDATION_PENDING",
                false,
                CorrelationId,
                null,
                null)
        });
        using var client = factory.CreateClient();
        AddIdentityHeaders(client);

        using var response = await PostPreviewAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_EVIDENCE_VALIDATION_PENDING");
        body.Message.Should().NotContain("MinIO").And.NotContain("S3").And.NotContain("SELECT");
    }

    [Fact]
    public async Task Preview_UnknownReference_IsAntiEnumerated()
    {
        using var factory = CreateFactory(new FakeReviewService
        {
            PreviewResult = new OperatorConsoleStatutoryEvidencePreviewResult("REJECTED", "NOT_FOUND", false, CorrelationId, null, null)
        });
        using var client = factory.CreateClient();
        AddIdentityHeaders(client);

        using var response = await PostPreviewAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("NOT_FOUND");
        body.Message.Should().NotContain(ItemReference.ToString()).And.NotContain(DecisionId.ToString());
    }

    [Fact]
    public async Task Preview_StorageUnavailable_ReturnsSafeRetryableServiceUnavailable()
    {
        using var factory = CreateFactory(new FakeReviewService
        {
            PreviewResult = new OperatorConsoleStatutoryEvidencePreviewResult(
                "REJECTED",
                "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE",
                true,
                CorrelationId,
                null,
                null)
        });
        using var client = factory.CreateClient();
        AddIdentityHeaders(client);

        using var response = await PostPreviewAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Retryable.Should().BeTrue();
        body.Message.Contains("endpoint", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        body.Message.Contains("bucket", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        body.Message.Contains("object key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        body.Message.Contains("storage locator", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private static CustomWebApplicationFactory CreateFactory(FakeReviewService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryEvidenceReviewService>();
                services.AddSingleton<IOperatorConsoleStatutoryEvidenceReviewService>(service);
            });

    private static Task<HttpResponseMessage> PostPreviewAsync(HttpClient client) =>
        client.PostAsJsonAsync(PreviewPath, new OperatorConsoleStatutoryEvidencePreviewRequest(ItemReference));

    private static void AddIdentityHeaders(
        HttpClient client,
        string permission = OperatorConsoleStatutoryEvidenceReviewConstants.Permission)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", ShiftId.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
    }

    private static OperatorConsoleStatutoryEvidencePreviewResult AcceptedPreview(string contentType, byte[] bytes) =>
        new(
            "ACCEPTED",
            null,
            false,
            CorrelationId,
            new StatutoryEvidenceObjectContent(
                new ChunkedNonSeekableStream(bytes),
                contentType,
                bytes.Length,
                null,
                null,
                null),
            new OperatorConsoleStatutoryEvidencePreviewAuditContext(
                new OperatorConsoleStatutoryEvidencePreviewTarget(
                    DecisionId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    SetReference,
                    1,
                    Guid.NewGuid(),
                    ItemReference,
                    1,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1,
                    "internal-only",
                    contentType,
                    bytes.Length,
                    new string('a', 64),
                    null,
                    CorrelationId,
                    UserId),
                new StatutoryEvidenceActor(UserId, null, "OPERATOR_CONSOLE")));

    private sealed class FakeReviewService : IOperatorConsoleStatutoryEvidenceReviewService
    {
        public List<string> StreamOutcomes { get; } = [];

        public OperatorConsoleStatutoryEvidencePreviewResult? PreviewResult { get; init; }

        public Task<OperatorConsoleStatutoryEvidenceReviewResult?> ReadAsync(
            Guid statutoryDiscountDecisionCommandId,
            OperatorConsoleReviewAccessContext accessContext,
            CancellationToken cancellationToken) =>
            Task.FromResult<OperatorConsoleStatutoryEvidenceReviewResult?>(new(
                statutoryDiscountDecisionCommandId,
                SetReference,
                "WEBPAY",
                "NOT_DECIDED",
                "PENDING_REVIEW",
                true,
                true,
                "LOCKED_FOR_REVIEW",
                "ACTIVE",
                "NOT_REQUESTED",
                false,
                "REPLACEMENT_NOT_ALLOWED",
                [new OperatorConsoleStatutoryEvidenceReviewItemResult(
                    ItemReference,
                    "PWD_ID",
                    "FRONT",
                    "image/jpeg",
                    "image/jpeg",
                    4,
                    "UPLOADED",
                    "PASSED",
                    "CLEAN",
                    "REVIEWABLE",
                    "UNBOUND",
                    "ACTIVE",
                    "NOT_REQUESTED",
                    false,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    true,
                    null)],
                accessContext.CorrelationId));

        public Task<OperatorConsoleStatutoryEvidencePreviewResult> OpenPreviewAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid evidenceItemReference,
            OperatorConsoleReviewAccessContext accessContext,
            CancellationToken cancellationToken) =>
            Task.FromResult(PreviewResult ?? AcceptedPreview("image/jpeg", JpegBytes));

        public Task RecordPreviewStreamOutcomeAsync(
            OperatorConsoleStatutoryEvidencePreviewAuditContext context,
            string outcome,
            CancellationToken cancellationToken)
        {
            StreamOutcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    private sealed class ChunkedNonSeekableStream(byte[] bytes) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Math.Min(Math.Min(buffer.Length, 2), bytes.Length - _offset);
            if (count <= 0)
            {
                return ValueTask.FromResult(0);
            }

            bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
