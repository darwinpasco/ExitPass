using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies Operator Console statutory discount read endpoints.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountReadApiIntegrationTests
{
    private const string QueueEndpoint = "/v1/ops/operator-console/statutory-discounts/drafts";
    private static readonly Guid DraftId = Guid.Parse("8c000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("8c000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("8c000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("8c000000-0000-0000-0000-000000000004");
    private static readonly Guid PolicyId = Guid.Parse("8c000000-0000-0000-0000-000000000005");
    private static readonly Guid CorrelationId = Guid.Parse("8c000000-0000-0000-0000-000000000006");

    [Fact]
    public void QueueEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == QueueEndpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Get.Method);
    }

    [Fact]
    public void DetailEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/statutory-discounts/drafts/{draftId:guid}")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Get.Method);
    }

    [Fact]
    public async Task ReadEndpointsAppearInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/drafts");
        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/drafts/{draftId}");
        swaggerJson.Should().Contain("ListOperatorConsoleStatutoryDiscountDrafts");
        swaggerJson.Should().Contain("GetOperatorConsoleStatutoryDiscountDraft");
    }

    [Fact]
    public async Task Queue_WhenDraftsExist_ReturnsReadModelEnvelope()
    {
        using var factory = CreateFactory(detail: Detail());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{QueueEndpoint}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftQueueResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].DraftId.Should().Be(DraftId);
        body.Items[0].TicketReference.Should().Be("READ-QUEUE-001");
        body.Items[0].PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        body.Items[0].OriginalAmountMinorUnits.Should().Be(18000);
        body.Items[0].PayableAmountMinorUnits.Should().Be(14400);
        body.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task Queue_WhenNoDrafts_ReturnsEmptyItems()
    {
        using var factory = CreateFactory(detail: null, emptyQueue: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{QueueEndpoint}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftQueueResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_WhenDraftExists_ReturnsPolicyAndPayableBasisData()
    {
        using var factory = CreateFactory(detail: Detail());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{QueueEndpoint}/{DraftId}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>();
        body.Should().NotBeNull();
        body!.DraftId.Should().Be(DraftId);
        body.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        body.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        body.NationalLawReference.Should().Be("RA 9994");
        body.OriginalAmountMinorUnits.Should().Be(18000);
        body.PayableAmountMinorUnits.Should().Be(14400);
        body.Activity.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Detail_WhenDraftMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(detail: null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{QueueEndpoint}/{DraftId}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DRAFT_NOT_FOUND");
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail,
        bool emptyQueue = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountReadService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountReadService>(
                    new FakeReadService(detail, emptyQueue));
            });

    private static OperatorConsoleStatutoryDiscountDraftDetailResult Detail() =>
        new(
            DraftId,
            ParkingSessionId,
            "READ-QUEUE-001",
            "ABC 1234",
            SiteId,
            "Terminal Parking / North Exit",
            SiteGroupId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            EvidenceRequired: false,
            EvidenceCaptured: false,
            DateTimeOffset.Parse("2026-06-01T08:15:00+08:00"),
            ValidatedAt: null,
            RequestedByUserId: Guid.Parse("8c000000-0000-0000-0000-000000000007"),
            ValidatedByUserId: null,
            DecisionReasonCode: null,
            FailureReasonCode: null,
            "NATIONAL_LAW_FALLBACK",
            PolicyId,
            ResolvedJurisdictionId: null,
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "RA 9994 Senior Citizen National Fallback",
            "Expanded Senior Citizens Act of 2010",
            OrdinanceReference: null,
            "RA 9994",
            "VERIFIED_OFFICIAL",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            FreeDurationMinutes: null,
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "CHARGEABLE_PORTION_ONLY",
            "NO_STACKING_ON_FREE_PERIOD",
            JsonSerializer.SerializeToElement(new { nationalLawReference = "RA 9994" }),
            OriginalTariffSnapshotId: null,
            PayableBasisApplicationId: null,
            PayableBasisApplicationStatus: null,
            OriginalAmountMinorUnits: 18000,
            StatutoryDiscountAmountMinorUnits: 3600,
            PayableAmountMinorUnits: 14400,
            "PHP",
            ["Draft requested at 2026-06-01T08:15:00.0000000+08:00."]);

    private sealed class FakeReadService : IOperatorConsoleStatutoryDiscountReadService
    {
        private readonly OperatorConsoleStatutoryDiscountDraftDetailResult? _detail;
        private readonly bool _emptyQueue;

        public FakeReadService(OperatorConsoleStatutoryDiscountDraftDetailResult? detail, bool emptyQueue)
        {
            _detail = detail;
            _emptyQueue = emptyQueue;
        }

        public Task<OperatorConsoleStatutoryDiscountDraftQueueResult> ListDraftsAsync(
            OperatorConsoleStatutoryDiscountDraftQueueQuery query,
            CancellationToken cancellationToken)
        {
            var items = _emptyQueue || _detail is null
                ? Array.Empty<OperatorConsoleStatutoryDiscountDraftQueueItemResult>()
                :
                [
                    new OperatorConsoleStatutoryDiscountDraftQueueItemResult(
                        _detail.DraftId,
                        _detail.ParkingSessionId,
                        _detail.TicketReference,
                        _detail.PlateNumber,
                        _detail.SiteId,
                        _detail.SiteName,
                        _detail.EntitlementType!,
                        _detail.ValidationStatus!,
                        _detail.EvidenceRequired,
                        _detail.PolicyResolutionBasis,
                        _detail.PolicyCode,
                        _detail.PolicyName,
                        _detail.OriginalAmountMinorUnits,
                        _detail.PayableAmountMinorUnits,
                        _detail.CurrencyCode,
                        _detail.RequestedAt,
                        _detail.RequestedByUserId,
                        _detail.FailureReasonCode)
                ];

            return Task.FromResult(new OperatorConsoleStatutoryDiscountDraftQueueResult(
                items,
                query.Page,
                query.PageSize,
                HasMore: false,
                query.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> GetDraftAsync(
            OperatorConsoleStatutoryDiscountDraftDetailQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(_detail);
    }
}
