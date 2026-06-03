using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies Operator Console statutory discount evidence metadata endpoints.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountEvidenceApiIntegrationTests
{
    private static readonly Guid DraftId = Guid.Parse("67000000-0000-0000-0000-000000000001");
    private static readonly Guid EvidenceId = Guid.Parse("67000000-0000-0000-0000-000000000002");
    private static readonly Guid UserId = Guid.Parse("67000000-0000-0000-0000-000000000003");
    private static readonly Guid DeviceBindingId = Guid.Parse("67000000-0000-0000-0000-000000000004");
    private static readonly Guid ShiftId = Guid.Parse("67000000-0000-0000-0000-000000000005");
    private static readonly Guid SiteId = Guid.Parse("67000000-0000-0000-0000-000000000006");
    private static readonly Guid SiteGroupId = Guid.Parse("67000000-0000-0000-0000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("67000000-0000-0000-0000-000000000008");

    [Fact]
    public void EvidenceEndpointRoutesExist()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/statutory-discounts/{draftId:guid}/evidence")
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
            .Should().BeEquivalentTo([HttpMethod.Get.Method, HttpMethod.Post.Method]);
    }

    [Fact]
    public async Task EvidenceEndpointsAppearInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/{draftId}/evidence");
        swaggerJson.Should().Contain("CaptureOperatorConsoleStatutoryDiscountEvidence");
        swaggerJson.Should().Contain("ListOperatorConsoleStatutoryDiscountEvidence");
    }

    [Fact]
    public async Task CaptureEvidence_WhenAccepted_ReturnsMetadataEnvelope()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>();
        body.Should().NotBeNull();
        body!.EvidenceId.Should().Be(EvidenceId);
        body.EvidenceRequiredSatisfied.Should().BeTrue();
        body.VerificationStatus.Should().Be("CAPTURED");
        body.StorageReference.Should().Be("operator-confirmed");
    }

    [Fact]
    public async Task CaptureEvidence_WhenDraftMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(missing: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DRAFT_NOT_FOUND");
    }

    [Fact]
    public async Task CaptureEvidence_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST");
    }

    [Fact]
    public async Task ListEvidence_WhenAccepted_ReturnsMetadataList()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EvidenceEndpoint()}?correlationId={CorrelationId}");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountEvidenceListResponse>();
        body.Should().NotBeNull();
        body!.EvidenceRequired.Should().BeTrue();
        body.EvidenceRequiredSatisfied.Should().BeTrue();
        body.Items.Should().ContainSingle();
        body.RequiredEvidenceTypes.Should().Contain("SENIOR_CITIZEN_ID");
    }

    private static CustomWebApplicationFactory CreateFactory(bool missing = false, bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountEvidenceService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountEvidenceService>(
                    new FakeEvidenceService(missing, throwValidation));
            });

    private static string EvidenceEndpoint() =>
        $"/v1/ops/operator-console/statutory-discounts/{DraftId}/evidence";

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureRequest CaptureRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            "SENIOR_CITIZEN_ID",
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: null,
            ReferenceNumber: null,
            Notes: null,
            OperatorConfirmation: true,
            "evidence-api-test",
            CorrelationId);

    private static void AddOperatorHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Operator-User-Id", UserId.ToString());
        request.Headers.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        request.Headers.Add("X-Operator-Shift-Id", ShiftId.ToString());
    }

    private sealed class FakeEvidenceService : IOperatorConsoleStatutoryDiscountEvidenceService
    {
        private readonly bool _missing;
        private readonly bool _throwValidation;

        public FakeEvidenceService(bool missing, bool throwValidation)
        {
            _missing = missing;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?> CaptureAsync(
            OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("Invalid evidence request.");
            }

            if (_missing)
            {
                return Task.FromResult<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?>(null);
            }

            return Task.FromResult<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?>(new(
                EvidenceId,
                command.DraftId,
                command.EvidenceType,
                command.CaptureMethod,
                command.FileName,
                command.ContentType,
                command.SizeBytes,
                "operator-confirmed",
                ReferenceNumberMasked: null,
                command.UserId,
                DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
                "NOT_REDACTED",
                "CAPTURED",
                EvidenceRequiredSatisfied: true,
                CurrentDraftStatus: "REQUESTED",
                AccessAllowed: true,
                ErrorCode: null,
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountEvidenceListResult?> ListAsync(
            OperatorConsoleStatutoryDiscountEvidenceListQuery query,
            CancellationToken cancellationToken)
        {
            if (_missing)
            {
                return Task.FromResult<OperatorConsoleStatutoryDiscountEvidenceListResult?>(null);
            }

            return Task.FromResult<OperatorConsoleStatutoryDiscountEvidenceListResult?>(new(
                query.DraftId,
                EvidenceRequired: true,
                EvidenceRequiredSatisfied: true,
                ["SENIOR_CITIZEN_ID"],
                EvidenceCount: 1,
                LatestEvidenceStatus: "CAPTURED",
                [
                    new OperatorConsoleStatutoryDiscountEvidenceMetadataResult(
                        EvidenceId,
                        query.DraftId,
                        "SENIOR_CITIZEN_ID",
                        "OPERATOR_CONFIRMED",
                        "operator-confirmed",
                        query.UserId,
                        DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
                        "NOT_REDACTED",
                        "CAPTURED",
                        query.CorrelationId)
                ],
                query.CorrelationId));
        }
    }
}

