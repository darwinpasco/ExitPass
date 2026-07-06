using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class SemanticHashBackfillInternalApiSecurityIntegrationTests
{
    private const string Endpoint = "/internal/v1/fiscal-exception-queue/semantic-hash-backfill-requests";

    [Fact]
    public void SemanticHashBackfillEndpoint_HasInternalMtlsMetadata()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<InternalServiceEndpointMetadata>().Should().NotBeNull();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    [Fact]
    public void SemanticHashBackfillEndpoint_DoesNotExposePublicRoute()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains(
                "semantic-hash-backfill-requests",
                StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].RoutePattern.RawText.Should().Be(Endpoint);
    }

    [Fact]
    public async Task SemanticHashBackfillEndpoint_WhenDisabledByDefault_FailsClosedWithoutLeakingRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = new FiscalExceptionSemanticHashBackfillInternalApiRequest(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            RecalculationPreviewAuditId: Guid.NewGuid(),
            MutationPreparationAuditId: Guid.NewGuid(),
            ApprovalReference: "APPROVAL-2026-07-06-001",
            DualControlReference: "DUAL-2026-07-06-001",
            ActorServiceIdentityId: Guid.NewGuid(),
            ReasonCode: "semantic_hash_legacy_backfill_request",
            SafeJustification: "legacy semantic hash requires governed sha256:v1 metadata alignment",
            CorrelationId: Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(Endpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<FiscalExceptionSemanticHashBackfillInternalApiResponse>();
        body.Should().NotBeNull();
        body!.BlockReasonCode.Should().Be("semantic_hash_backfill_internal_api_disabled");
        body.SafeSummary.Should().NotContain("APPROVAL-2026-07-06-001");
        body.SafeSummary.Should().NotContain(request.FiscalIssuanceReferenceId.ToString());
        body.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task SemanticHashBackfillEndpoint_WhenArrayPayloadPosted_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var payload = new[]
        {
            new
            {
                fiscalIssuanceReferenceId = Guid.NewGuid(),
                reasonCode = "semantic_hash_legacy_backfill_request"
            }
        };

        using var response = await client.PostAsJsonAsync(Endpoint, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
