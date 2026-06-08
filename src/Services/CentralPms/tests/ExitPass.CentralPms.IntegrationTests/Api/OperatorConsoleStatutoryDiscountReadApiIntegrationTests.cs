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
    private const string AuditReportEndpoint = "/v1/ops/operator-console/audit/statutory-discounts";
    private static readonly Guid DraftId = Guid.Parse("8c000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("8c000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("8c000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("8c000000-0000-0000-0000-000000000004");
    private static readonly Guid PolicyId = Guid.Parse("8c000000-0000-0000-0000-000000000005");
    private static readonly Guid CorrelationId = Guid.Parse("8c000000-0000-0000-0000-000000000006");
    private static readonly Guid UserId = Guid.Parse("8c000000-0000-0000-0000-000000000007");
    private static readonly Guid DeviceBindingId = Guid.Parse("8c000000-0000-0000-0000-000000000008");
    private static readonly Guid ShiftId = Guid.Parse("8c000000-0000-0000-0000-000000000009");

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
    public void AuditReportEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == AuditReportEndpoint)
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
        swaggerJson.Should().Contain("/v1/ops/operator-console/audit/statutory-discounts");
        swaggerJson.Should().Contain("ListOperatorConsoleStatutoryDiscountDrafts");
        swaggerJson.Should().Contain("GetOperatorConsoleStatutoryDiscountDraft");
        swaggerJson.Should().Contain("ListOperatorConsoleStatutoryDiscountAuditReport");
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://localhost:5174")]
    [InlineData("http://127.0.0.1:5174")]
    [InlineData("http://localhost:5175")]
    [InlineData("http://127.0.0.1:5175")]
    [InlineData("http://localhost:5178")]
    [InlineData("http://127.0.0.1:5178")]
    public async Task QueuePreflight_FromOperatorConsoleLocalOrigin_ReturnsCorsHeaders(string origin)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, QueueEndpoint);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", HttpMethod.Get.Method);
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "Content-Type, X-Correlation-Id, X-Operator-User-Id, X-Operator-Device-Binding-Id, X-Operator-Shift-Id");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be(origin);

        var allowedMethods = SplitHeaderValues(response, "Access-Control-Allow-Methods");
        allowedMethods.Should().Contain(HttpMethod.Get.Method);
        allowedMethods.Should().Contain(HttpMethod.Post.Method);
        allowedMethods.Should().Contain(HttpMethod.Options.Method);

        var allowedHeaders = SplitHeaderValues(response, "Access-Control-Allow-Headers");
        allowedHeaders.Should().Contain(header => string.Equals(header, "Content-Type", StringComparison.OrdinalIgnoreCase));
        allowedHeaders.Should().Contain(header => string.Equals(header, "X-Correlation-Id", StringComparison.OrdinalIgnoreCase));
        allowedHeaders.Should().Contain(header => string.Equals(header, "X-Operator-User-Id", StringComparison.OrdinalIgnoreCase));
        allowedHeaders.Should().Contain(header => string.Equals(header, "X-Operator-Device-Binding-Id", StringComparison.OrdinalIgnoreCase));
        allowedHeaders.Should().Contain(header => string.Equals(header, "X-Operator-Shift-Id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Queue_WhenDraftsExist_ReturnsReadModelEnvelope()
    {
        using var factory = CreateFactory(detail: Detail());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{QueueEndpoint}?correlationId={CorrelationId}");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{QueueEndpoint}?correlationId={CorrelationId}");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{QueueEndpoint}/{DraftId}?correlationId={CorrelationId}");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

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
    public async Task AuditReport_WhenRowsExist_ReturnsSafeReadOnlySummary()
    {
        using var factory = CreateFactory(detail: Detail());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuditReportEndpoint}?correlationId={CorrelationId}&validationStatus=REQUESTED");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountAuditReportResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].DraftId.Should().Be(DraftId);
        body.Items[0].StatutoryDiscountValidationId.Should().Be(DraftId);
        body.Items[0].TicketReference.Should().Be("READ-QUEUE-001");
        body.Items[0].PlateNumber.Should().Be("ABC 1234");
        body.Items[0].EvidenceRequiredSatisfied.Should().BeFalse();
        body.Items[0].OriginalAmountMinorUnits.Should().Be(18000);
        body.Items[0].StatutoryDiscountAmountMinorUnits.Should().Be(3600);
        body.Items[0].FinalPayableAmountMinorUnits.Should().Be(14400);
        body.Items[0].AccessEvaluationSummary.Should().Be("VIEW_AUDIT_REPORT / SUCCESS");
        body.CorrelationId.Should().Be(CorrelationId);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("referenceNumber");
        json.Should().NotContain("evidenceStorageRef");
        json.Should().NotContain("raw");
    }

    [Fact]
    public async Task Detail_WhenDraftMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(detail: null);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{QueueEndpoint}/{DraftId}?correlationId={CorrelationId}");
        AddOperatorHeaders(request);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DRAFT_NOT_FOUND");
    }

    [Fact]
    public async Task Queue_WhenOperatorIdentityMissing_ReturnsBadRequest()
    {
        using var factory = CreateFactory(detail: Detail());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{QueueEndpoint}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_READ_REQUEST");
        body.Message.Should().Contain("Operator user identity is required");
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
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton(CreateFakeAccessEvaluationService(allowed: true));
                services.AddSingleton(CreateFakeAccessEvaluationWriter());
            });

    private static void AddOperatorHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Operator-User-Id", UserId.ToString());
        request.Headers.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        request.Headers.Add("X-Operator-Shift-Id", ShiftId.ToString());
    }

    private static IOperatorConsoleAccessEvaluationService CreateFakeAccessEvaluationService(bool allowed)
        => new FakeAccessEvaluationService(allowed);

    private static IOperatorConsoleAccessEvaluationWriter CreateFakeAccessEvaluationWriter()
        => new FakeAccessEvaluationWriter();

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        OperatorConsoleAccessEvaluationCommand command,
        bool allowed) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            allowed ? [] : ["NO_ACTIVE_SHIFT"],
            allowed ? "OPERATOR" : null,
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: allowed),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: allowed),
            new OperatorConsoleSiteContextResult(command.SiteId ?? SiteId, command.SiteGroupId ?? SiteGroupId, Assigned: allowed),
            DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            Persisted: false,
            command.CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                command.UserId,
                HrIdentityMappingId: null,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                ShiftTakeoverId: null,
                command.SiteGroupId ?? SiteGroupId,
                command.SiteId ?? SiteId,
                command.ControlledActionCode,
                command.WorkflowCode,
                command.ParkingSessionId.HasValue ? "PARKING_SESSION" : null,
                command.ParkingSessionId));

    private static string[] SplitHeaderValues(HttpResponseMessage response, string headerName)
    {
        response.Headers.TryGetValues(headerName, out var values).Should().BeTrue();

        return values!
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }

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
            EvidenceRequiredSatisfied: false,
            EvidenceCount: 0,
            LatestEvidenceStatus: null,
            RequiredEvidenceTypes: [],
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
                        _detail.EvidenceRequiredSatisfied,
                        _detail.EvidenceCount,
                        _detail.LatestEvidenceStatus,
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

        public Task<OperatorConsoleStatutoryDiscountAuditReportResult> ListAuditReportAsync(
            OperatorConsoleStatutoryDiscountAuditReportQuery query,
            CancellationToken cancellationToken)
        {
            var items = _emptyQueue || _detail is null
                ? Array.Empty<OperatorConsoleStatutoryDiscountAuditReportItemResult>()
                :
                [
                    new OperatorConsoleStatutoryDiscountAuditReportItemResult(
                        _detail.DraftId,
                        _detail.ParkingSessionId,
                        _detail.TicketReference,
                        _detail.PlateNumber,
                        _detail.SiteId,
                        _detail.SiteGroupId,
                        _detail.EntitlementType!,
                        _detail.ValidationStatus!,
                        _detail.EvidenceRequired,
                        _detail.EvidenceCaptured,
                        _detail.EvidenceRequiredSatisfied,
                        _detail.EvidenceCount,
                        _detail.LatestEvidenceStatus,
                        _detail.PayableBasisApplicationStatus,
                        _detail.OriginalAmountMinorUnits,
                        _detail.StatutoryDiscountAmountMinorUnits,
                        _detail.PayableAmountMinorUnits,
                        _detail.CurrencyCode,
                        _detail.RequestedByUserId,
                        _detail.ValidatedByUserId,
                        _detail.RequestedAt,
                        _detail.ValidatedAt,
                        query.CorrelationId,
                        _detail.PolicyCode,
                        _detail.OrdinanceReference,
                        _detail.LegalBasisReference,
                        null,
                        "VIEW_AUDIT_REPORT / SUCCESS")
                ];

            return Task.FromResult(new OperatorConsoleStatutoryDiscountAuditReportResult(
                items,
                items.Length,
                query.Limit,
                query.Offset,
                query.CorrelationId));
        }
    }

    private sealed class FakeAccessEvaluationService : IOperatorConsoleAccessEvaluationService
    {
        private readonly bool _allowed;

        public FakeAccessEvaluationService(bool allowed)
        {
            _allowed = allowed;
        }

        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccessResult(command, _allowed) with { Persisted = false });
    }

    private sealed class FakeAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with
            {
                EvaluationId = Guid.Parse("8c000000-0000-0000-0000-000000000010"),
                Persisted = true
            });
    }
}
