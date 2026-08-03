using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies least-privilege RBAC contracts for Operator Console statutory discount endpoints.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountRbacContractIntegrationTests
{
    private const string SessionLookupEndpoint = "/v1/ops/operator-console/sessions/lookup";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DraftDetailEndpoint = "/v1/ops/operator-console/statutory-discounts/drafts/{0}";
    private const string EvidenceEndpoint = "/v1/ops/operator-console/statutory-discounts/{0}/evidence";
    private const string DecisionEndpoint = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private const string ApplyEndpoint = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";
    private const string PolicyResolutionEndpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private const string AuditEndpoint = "/v1/ops/operator-console/audit/statutory-discounts";
    private const string PolicyImportReviewsEndpoint = "/v1/ops/operator-console/statutory-discounts/policies/import/reviews";

    private const string SessionLookupPermission = "statutory-discounts.session.lookup";
    private const string DraftViewPermission = "statutory-discounts.draft.view";
    private const string DraftCreatePermission = "statutory-discounts.draft.create";
    private const string EvidenceViewPermission = "statutory-discounts.evidence.view";
    private const string EvidenceCapturePermission = "statutory-discounts.evidence.capture";
    private const string DecisionReviewPermission = "statutory-discounts.decision.review";
    private const string ReviewQueueReadPermission = "statutory-discounts.review.queue.read";
    private const string ReviewDetailReadPermission = "statutory-discounts.review.detail.read";
    private const string DecisionApprovePermission = "statutory-discounts.decision.approve";
    private const string DecisionRejectPermission = "statutory-discounts.decision.reject";
    private const string ApplyPermission = "statutory-discounts.payable-basis.apply";
    private const string PolicyResolvePermission = "statutory-discounts.policy.resolve";
    private const string AuditReadPermission = "statutory-discounts.audit.read";
    private const string StatusReadOnlyPermission = "fiscal-issuance.status.read";
    private const string PolicyImportReviewPermission = "operator-console.policy-import-review.review";
    private const string ReconciliationManagePermission = "reconciliation.manage";

    private static readonly Guid UserId = Guid.Parse("99000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceBindingId = Guid.Parse("99000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("99000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("99000000-0000-0000-0000-000000000004");
    private static readonly Guid ShiftId = Guid.Parse("99000000-0000-0000-0000-000000000005");
    private static readonly Guid ParkingSessionId = Guid.Parse("99000000-0000-0000-0000-000000000006");
    private static readonly Guid DraftId = Guid.Parse("99000000-0000-0000-0000-000000000007");
    private static readonly Guid EvaluationId = Guid.Parse("99000000-0000-0000-0000-000000000008");
    private static readonly Guid EvidenceId = Guid.Parse("99000000-0000-0000-0000-000000000009");
    private static readonly Guid PolicyId = Guid.Parse("99000000-0000-0000-0000-000000000010");
    private static readonly Guid ApplicationId = Guid.Parse("99000000-0000-0000-0000-000000000011");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("99000000-0000-0000-0000-000000000012");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("99000000-0000-0000-0000-000000000013");
    private static readonly Guid CorrelationId = Guid.Parse("99000000-0000-0000-0000-000000000014");
    private static readonly Guid PolicyVersionId = Guid.Parse("99000000-0000-0000-0000-000000000016");
    private static readonly Guid JurisdictionId = Guid.Parse("99000000-0000-0000-0000-000000000017");

    [Theory]
    [InlineData(SessionLookupEndpoint, "POST", "OperatorConsoleStatutoryDiscountSessionLookup")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/drafts", "GET", "OperatorConsoleStatutoryDiscountDraftView")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/drafts/{draftId:guid}", "GET", "OperatorConsoleStatutoryDiscountDraftView")]
    [InlineData(AuditEndpoint, "GET", "OperatorConsoleStatutoryDiscountAuditRead")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/reviews/pending", "GET", "OperatorConsoleStatutoryDiscountReviewQueueRead")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}", "GET", "OperatorConsoleStatutoryDiscountReviewDetailRead")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}/decision", "POST", "OperatorConsoleStatutoryDiscountDecisionMutate")]
    [InlineData(DraftEndpoint, "POST", "OperatorConsoleStatutoryDiscountDraftCreate")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/{draftId:guid}/decision", "POST", "OperatorConsoleStatutoryDiscountDecisionMutate")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/{draftId:guid}/evidence", "POST", "OperatorConsoleStatutoryDiscountEvidenceCapture")]
    [InlineData("/v1/ops/operator-console/statutory-discounts/{draftId:guid}/evidence", "GET", "OperatorConsoleStatutoryDiscountEvidenceView")]
    [InlineData(PolicyResolutionEndpoint, "POST", "OperatorConsoleStatutoryDiscountPolicyResolve")]
    public void StatutoryDiscountEndpointsDeclareExpectedRbacPolicy(string route, string method, string policyName)
    {
        using var factory = CreateFactory(new FakeStatutoryDiscountServices());

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == route)
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method))
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName.Should().Be(policyName);
    }

    [Fact]
    public async Task SessionLookupUser_CanLookupSession_ButCannotCreateDraft()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, SessionLookupPermission);

        (await SendSessionLookupAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);
        services.SessionLookupCallCount.Should().Be(1);

        using var denied = await SendDraftAsync(client);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
        services.DraftCallCount.Should().Be(0);
    }

    [Fact]
    public async Task DraftViewUser_CanViewDraft_ButCannotMutateEvidenceDecisionOrApply()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, DraftViewPermission);

        (await client.GetAsync($"{string.Format(DraftDetailEndpoint, DraftId)}?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.GetDraftCallCount.Should().Be(1);

        (await SendEvidenceCaptureAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendDecisionAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.EvidenceCaptureCallCount.Should().Be(0);
        services.DecisionCallCount.Should().Be(0);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task EvidenceCaptureUser_CanCaptureEvidence_ButCannotApproveOrApply()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, EvidenceCapturePermission);

        (await SendEvidenceCaptureAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);
        services.EvidenceCaptureCallCount.Should().Be(1);

        (await SendDecisionAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.DecisionCallCount.Should().Be(0);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewReadUser_CanReadServiceChannelQueueAndDetail_ButCannotApproveOrReject()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, ReviewQueueReadPermission);

        (await client.GetAsync($"/v1/ops/operator-console/statutory-discounts/reviews/pending?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.ServiceChannelReviewListCallCount.Should().Be(1);

        (await SendServiceChannelDecisionAsync(client, "APPROVE")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendServiceChannelDecisionAsync(client, "REJECT")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.ServiceChannelReviewDecisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewDetailUser_CanReadServiceChannelDetail_ButCannotApproveOrReject()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, ReviewDetailReadPermission);

        (await client.GetAsync($"/v1/ops/operator-console/statutory-discounts/reviews/{DraftId}?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.ServiceChannelReviewDetailCallCount.Should().Be(1);

        (await SendServiceChannelDecisionAsync(client, "APPROVE")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendServiceChannelDecisionAsync(client, "REJECT")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.ServiceChannelReviewDecisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task LegacyDecisionReviewUser_CannotApproveOrReject()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, DecisionReviewPermission);

        (await SendDecisionAsync(client, "APPROVE")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendDecisionAsync(client, "REJECT")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.DecisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ApproveUser_CanApprove_ButCannotRejectOrApplyPayableBasis()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, DecisionApprovePermission);

        (await SendDecisionAsync(client, "APPROVE")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.DecisionCallCount.Should().Be(1);

        (await SendDecisionAsync(client, "REJECT")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.DecisionCallCount.Should().Be(1);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RejectUser_CanReject_ButCannotApproveOrApplyPayableBasis()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, DecisionRejectPermission);

        (await SendDecisionAsync(client, "REJECT")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.DecisionCallCount.Should().Be(1);

        (await SendDecisionAsync(client, "APPROVE")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.DecisionCallCount.Should().Be(1);
        services.ApplyCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(DecisionApprovePermission, "APPROVE")]
    [InlineData(DecisionRejectPermission, "REJECT")]
    public async Task ServiceIdentity_CannotUseHumanReviewDecisionRoute(string permission, string decision)
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateServiceClient(factory, permission);

        var response = await SendDecisionAsync(client, decision);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.DecisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PayableBasisApplyPermission_CannotUseRemovedOperatorConsoleRouteOrApprove()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, ApplyPermission);

        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.ApplyCallCount.Should().Be(0);

        (await SendDecisionAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.DecisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReconciliationManage_CannotBypassRemovedOperatorConsoleApplyRoute()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, ReconciliationManagePermission);

        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AuditReadUser_CanReadAudit_ButCannotMutate()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, AuditReadPermission);

        (await client.GetAsync($"{AuditEndpoint}?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        services.AuditReportCallCount.Should().Be(1);

        (await SendDraftAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        services.DraftCallCount.Should().Be(0);
    }

    [Fact]
    public async Task EvidenceViewAndPolicyResolve_AreSeparateReadContracts()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);

        using (var evidenceClient = CreateClient(factory, EvidenceViewPermission))
        {
            (await evidenceClient.GetAsync($"{string.Format(EvidenceEndpoint, DraftId)}?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
            services.EvidenceListCallCount.Should().Be(1);
            (await SendPolicyResolutionAsync(evidenceClient)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var policyClient = CreateClient(factory, PolicyResolvePermission))
        {
            (await SendPolicyResolutionAsync(policyClient)).StatusCode.Should().Be(HttpStatusCode.OK);
            services.PolicyResolveCallCount.Should().Be(1);
            (await policyClient.GetAsync($"{string.Format(EvidenceEndpoint, DraftId)}?correlationId={CorrelationId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task StatusReadOnlyUser_IsDeniedFromStatutoryDiscountMutations()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, StatusReadOnlyPermission);

        (await SendDraftAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendEvidenceCaptureAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendDecisionAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.DraftCallCount.Should().Be(0);
        services.EvidenceCaptureCallCount.Should().Be(0);
        services.DecisionCallCount.Should().Be(0);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PolicyImportReviewPermission_DoesNotAllowRuntimeStatutoryDiscountActions()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, PolicyImportReviewPermission);

        (await SendDraftAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendApplyAsync(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        services.DraftCallCount.Should().Be(0);
        services.ApplyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RuntimeStatutoryDiscountPermission_DoesNotAllowPolicyImportReview()
    {
        var services = new FakeStatutoryDiscountServices();
        using var factory = CreateFactory(services);
        using var client = CreateClient(factory, DraftCreatePermission);

        using var response = await client.GetAsync(PolicyImportReviewsEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    private static CustomWebApplicationFactory CreateFactory(FakeStatutoryDiscountServices services) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(collection =>
            {
                collection.RemoveAll<IOperatorConsoleSessionLookupService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountDraftService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountDecisionService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountEvidenceService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountApplyPayableBasisService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountPolicyResolutionService>();
                collection.RemoveAll<IOperatorConsoleStatutoryDiscountReadService>();
                collection.RemoveAll<IOperatorConsoleServiceChannelStatutoryDiscountReviewService>();
                collection.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                collection.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                collection.RemoveAll<ICentralPmsRbacRepository>();

                collection.AddSingleton<IOperatorConsoleSessionLookupService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountDraftService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountDecisionService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountEvidenceService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountApplyPayableBasisService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountPolicyResolutionService>(services);
                collection.AddSingleton<IOperatorConsoleStatutoryDiscountReadService>(services);
                collection.AddSingleton<IOperatorConsoleServiceChannelStatutoryDiscountReviewService>(services);
                collection.AddSingleton<IOperatorConsoleAccessEvaluationService>(services);
                collection.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(services);
                collection.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static HttpClient CreateClient(CustomWebApplicationFactory factory, string permission)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", ShiftId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", SiteGroupId.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        return client;
    }

    private static HttpClient CreateServiceClient(CustomWebApplicationFactory factory, string permission)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, Guid.Parse("99000000-0000-0000-0000-000000000015").ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", ShiftId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", SiteGroupId.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        return client;
    }

    private static Task<HttpResponseMessage> SendSessionLookupAsync(HttpClient client) =>
        client.PostAsJsonAsync(SessionLookupEndpoint, new OperatorConsoleSessionLookupRequest(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "RBAC-TICKET-001",
            PlateNumber: null,
            "PARKING_SESSION_ID",
            "rbac-session-lookup",
            CorrelationId));

    private static Task<HttpResponseMessage> SendDraftAsync(HttpClient client) =>
        client.PostAsJsonAsync(DraftEndpoint, new OperatorConsoleStatutoryDiscountDraftRequest(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "RBAC-TICKET-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "SC-RBAC-****-0001",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "OPERATOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "RBAC contract test.",
            ReasonCode: "RBAC_TEST",
            "rbac-draft",
            CorrelationId));

    private static Task<HttpResponseMessage> SendEvidenceCaptureAsync(HttpClient client) =>
        client.PostAsJsonAsync(string.Format(EvidenceEndpoint, DraftId), new OperatorConsoleStatutoryDiscountEvidenceCaptureRequest(
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
            Notes: "RBAC contract test.",
            OperatorConfirmation: true,
            "rbac-evidence",
            CorrelationId));

    private static Task<HttpResponseMessage> SendDecisionAsync(HttpClient client, string decision = "APPROVE") =>
        client.PostAsJsonAsync(string.Format(DecisionEndpoint, DraftId), new OperatorConsoleStatutoryDiscountDecisionRequest(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            DecisionReasonCode: string.Equals(decision, "APPROVE", StringComparison.OrdinalIgnoreCase) ? "RBAC_TEST" : "DOCUMENT_INVALID",
            DecisionNotes: "RBAC contract test.",
            ReviewerAttestation: true,
            "rbac-decision",
            CorrelationId));

    private static Task<HttpResponseMessage> SendServiceChannelDecisionAsync(HttpClient client, string decision = "APPROVE") =>
        client.PostAsJsonAsync($"/v1/ops/operator-console/statutory-discounts/reviews/{DraftId}/decision", new OperatorConsoleStatutoryDiscountDecisionRequest(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            DecisionReasonCode: string.Equals(decision, "APPROVE", StringComparison.OrdinalIgnoreCase) ? "RBAC_TEST" : "DOCUMENT_INVALID",
            DecisionNotes: "RBAC contract test.",
            ReviewerAttestation: true,
            "rbac-service-channel-decision",
            CorrelationId));

    private static Task<HttpResponseMessage> SendApplyAsync(HttpClient client) =>
        client.PostAsJsonAsync(string.Format(ApplyEndpoint, DraftId), new
        {
            userId = UserId,
            operatorDeviceBindingId = DeviceBindingId,
            siteId = SiteId,
            siteGroupId = SiteGroupId,
            operatorShiftId = ShiftId,
            originalTariffSnapshotId = OriginalTariffSnapshotId,
            idempotencyKey = "rbac-apply",
            correlationId = CorrelationId
        });

    private static Task<HttpResponseMessage> SendPolicyResolutionAsync(HttpClient client) =>
        client.PostAsJsonAsync(PolicyResolutionEndpoint, new OperatorConsoleStatutoryDiscountPolicyResolutionRequest(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "rbac-policy-resolution",
            CorrelationId));

    private static OperatorConsoleAccessEvaluationResult AccessResult(OperatorConsoleAccessEvaluationCommand command) =>
        new(
            EvaluationId,
            true,
            "ALLOWED",
            Array.Empty<string>(),
            "OPERATOR",
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(command.SiteId ?? SiteId, command.SiteGroupId ?? SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-07-12T10:00:00+08:00"),
            false,
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

    private sealed class FakeStatutoryDiscountServices :
        IOperatorConsoleSessionLookupService,
        IOperatorConsoleStatutoryDiscountDraftService,
        IOperatorConsoleStatutoryDiscountDecisionService,
        IOperatorConsoleStatutoryDiscountEvidenceService,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService,
        IOperatorConsoleStatutoryDiscountPolicyResolutionService,
        IOperatorConsoleStatutoryDiscountReadService,
        IOperatorConsoleServiceChannelStatutoryDiscountReviewService,
        IOperatorConsoleAccessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter
    {
        public int SessionLookupCallCount { get; private set; }
        public int DraftCallCount { get; private set; }
        public int GetDraftCallCount { get; private set; }
        public int EvidenceCaptureCallCount { get; private set; }
        public int EvidenceListCallCount { get; private set; }
        public int DecisionCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }
        public int PolicyResolveCallCount { get; private set; }
        public int AuditReportCallCount { get; private set; }
        public int ServiceChannelReviewListCallCount { get; private set; }
        public int ServiceChannelReviewDetailCallCount { get; private set; }
        public int ServiceChannelReviewDecisionCallCount { get; private set; }

        public Task<OperatorConsoleSessionLookupResult> LookupAsync(
            OperatorConsoleSessionLookupCommand command,
            CancellationToken cancellationToken)
        {
            SessionLookupCallCount++;
            return Task.FromResult(new OperatorConsoleSessionLookupResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                new OperatorConsoleSessionReadModel(
                    ParkingSessionId,
                    "RBAC-TICKET-001",
                    "ABC-1234",
                    SiteId,
                    SiteGroupId,
                    "ACTIVE",
                    DateTimeOffset.Parse("2026-07-12T08:00:00+08:00"),
                    CurrentPayableAmountMinorUnits: 12500,
                    CurrencyCode: "PHP",
                    PaymentStatus: null,
                    DiscountStatus: "NOT_APPLIED",
                    ExitAuthorizationStatus: null),
                SessionEligible: true,
                IneligibilityReason: null,
                Alerts: Array.Empty<string>(),
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
            OperatorConsoleStatutoryDiscountDraftCommand command,
            CancellationToken cancellationToken)
        {
            DraftCallCount++;
            return Task.FromResult(new OperatorConsoleStatutoryDiscountDraftResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                DraftAccepted: true,
                DraftPersisted: true,
                DraftId,
                command.ParkingSessionId,
                command.EntitlementType,
                "REQUESTED",
                EvidenceCaptureRequired: true,
                EvidenceRequired: true,
                EvidenceReferenceCreated: true,
                EvidenceId,
                ReusedExistingDraft: false,
                Policy(),
                IneligibilityReason: null,
                ErrorCode: null,
                command.CorrelationId,
                OperatorConsolePolicyReadinessClassifications.ReadyVerified));
        }

        public Task<OperatorConsoleStatutoryDiscountDecisionResult> DecideAsync(
            OperatorConsoleStatutoryDiscountDecisionCommand command,
            CancellationToken cancellationToken)
        {
            DecisionCallCount++;
            return Task.FromResult(new OperatorConsoleStatutoryDiscountDecisionResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                DecisionAccepted: true,
                DecisionPersisted: true,
                command.DraftId,
                ParkingSessionId,
                "SENIOR_CITIZEN",
                "REQUESTED",
                "APPROVED",
                command.Decision,
                command.DecisionReasonCode,
                AlreadyDecided: false,
                DecisionChanged: true,
                IneligibilityReason: null,
                ErrorCode: null,
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?> CaptureAsync(
            OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command,
            CancellationToken cancellationToken)
        {
            EvidenceCaptureCallCount++;
            return Task.FromResult<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?>(new(
                EvidenceId,
                command.DraftId,
                command.EvidenceType,
                command.CaptureMethod,
                command.FileName,
                command.ContentType,
                command.SizeBytes,
                StorageReference: null,
                ReferenceNumberMasked: null,
                command.UserId,
                DateTimeOffset.Parse("2026-07-12T10:01:00+08:00"),
                "NOT_REDACTED",
                "CAPTURED",
                EvidenceRequiredSatisfied: true,
                "REQUESTED",
                AccessAllowed: true,
                ErrorCode: null,
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountEvidenceListResult?> ListAsync(
            OperatorConsoleStatutoryDiscountEvidenceListQuery query,
            CancellationToken cancellationToken)
        {
            EvidenceListCallCount++;
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
                        null,
                        null,
                        query.UserId,
                        DateTimeOffset.Parse("2026-07-12T10:01:00+08:00"),
                        "NOT_REDACTED",
                        "CAPTURED",
                        query.CorrelationId)
                ],
                query.CorrelationId));
        }

        public Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
            StatutoryDiscountServiceChannelReviewQueueQuery query,
            OperatorConsoleReviewAccessContext accessContext,
            CancellationToken cancellationToken)
        {
            ServiceChannelReviewListCallCount++;
            return Task.FromResult(new StatutoryDiscountServiceChannelReviewQueueResult(
                [
                    new StatutoryDiscountServiceChannelReviewQueueItem(
                        DraftId,
                        ParkingSessionId,
                        "WEBPAY",
                        SiteId,
                        SiteGroupId,
                        "RBAC-TICKET-001",
                        "ABC-1234",
                        "SENIOR_CITIZEN",
                        "PENDING_REVIEW",
                        "PENDING_REVIEW",
                        StatutoryDiscountServiceChannelReviewStatuses.PendingReview,
                        EvidenceRequired: true,
                        EvidenceRecorded: true,
                        OriginalTariffSnapshotId,
                        DateTimeOffset.Parse("2026-07-12T09:00:00+08:00"),
                        query.CorrelationId)
                ],
                query.Page,
                query.PageSize,
                HasMore: false,
                query.CorrelationId));
        }

        public Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            OperatorConsoleReviewAccessContext accessContext,
            CancellationToken cancellationToken)
        {
            ServiceChannelReviewDetailCallCount++;
            return Task.FromResult<StatutoryDiscountServiceChannelReviewDetail?>(ServiceChannelReviewDetail(accessContext.CorrelationId));
        }

        public Task<StatutoryDiscountServiceChannelReviewDecisionResult> DecideAsync(
            StatutoryDiscountServiceChannelReviewDecisionCommand command,
            CancellationToken cancellationToken)
        {
            ServiceChannelReviewDecisionCallCount++;
            return Task.FromResult(new StatutoryDiscountServiceChannelReviewDecisionResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                DecisionAccepted: true,
                DecisionPersisted: true,
                command.StatutoryDiscountDecisionCommandId,
                ParkingSessionId,
                "WEBPAY",
                "SENIOR_CITIZEN",
                "PENDING_REVIEW",
                command.Decision,
                "PENDING_REVIEW",
                command.Decision,
                string.Equals(command.Decision, "APPROVE", StringComparison.OrdinalIgnoreCase)
                    ? StatutoryDiscountServiceChannelReviewStatuses.Approved
                    : StatutoryDiscountServiceChannelReviewStatuses.Rejected,
                command.Decision,
                command.DecisionReasonCode,
                AlreadyDecided: false,
                DecisionChanged: true,
                IneligibilityReason: null,
                ErrorCode: null,
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResult> ApplyAsync(
            OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            return Task.FromResult(new OperatorConsoleStatutoryDiscountApplyPayableBasisResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                ApplicationAccepted: true,
                ApplicationPersisted: true,
                ApplicationId,
                command.ValidationId,
                ParkingSessionId,
                command.OriginalTariffSnapshotId ?? OriginalTariffSnapshotId,
                AppliedTariffSnapshotId,
                "APPLIED",
                AlreadyApplied: false,
                GrossAmountMinorUnits: 12500,
                VatAmountMinorUnits: 1339,
                VatExclusiveAmountMinorUnits: 11161,
                StatutoryDiscountAmountMinorUnits: 2232,
                FinalPayableAmountMinorUnits: 8929,
                "PHP",
                PolicyId,
                ResolvedJurisdictionId: null,
                "NATIONAL_LAW_FALLBACK",
                "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
                "STATUTORY_DISCOUNT_VAT_EXEMPT",
                "RA 9994",
                OrdinanceReference: null,
                PolicySnapshotUsed: true,
                IneligibilityReason: null,
                ErrorCode: null,
                command.CorrelationId));
        }

        public Task<OperatorConsoleStatutoryDiscountPolicyResolutionResult> ResolveAsync(
            OperatorConsoleStatutoryDiscountPolicyResolutionCommand command,
            CancellationToken cancellationToken)
        {
            PolicyResolveCallCount++;
            return Task.FromResult(new OperatorConsoleStatutoryDiscountPolicyResolutionResult(
                EvaluationId,
                AccessAllowed: true,
                "ALLOWED",
                Array.Empty<string>(),
                AccessPersisted: true,
                PolicyResolved: true,
                Policy(),
                IneligibilityReason: null,
                ErrorCode: null,
                command.CorrelationId,
                OperatorConsolePolicyReadinessClassifications.ReadyVerified));
        }

        public Task<OperatorConsoleStatutoryDiscountDraftQueueResult> ListDraftsAsync(
            OperatorConsoleStatutoryDiscountDraftQueueQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OperatorConsoleStatutoryDiscountDraftQueueResult(
                [
                    new OperatorConsoleStatutoryDiscountDraftQueueItemResult(
                        DraftId,
                        ParkingSessionId,
                        "RBAC-TICKET-001",
                        "ABC-1234",
                        SiteId,
                        "RBAC Site",
                        "SENIOR_CITIZEN",
                        "REQUESTED",
                        EvidenceRequired: true,
                        EvidenceRequiredSatisfied: false,
                        EvidenceCount: 0,
                        LatestEvidenceStatus: null,
                        "NATIONAL_LAW_FALLBACK",
                        "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
                        "RA 9994 Senior Citizen National Fallback",
                        OriginalAmountMinorUnits: 12500,
                        PayableAmountMinorUnits: 12500,
                        "PHP",
                        DateTimeOffset.Parse("2026-07-12T09:00:00+08:00"),
                        UserId,
                        BlockedReason: null)
                ],
                query.Page,
                query.PageSize,
                HasMore: false,
                query.CorrelationId));

        public Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> GetDraftAsync(
            OperatorConsoleStatutoryDiscountDraftDetailQuery query,
            CancellationToken cancellationToken)
        {
            GetDraftCallCount++;
            return Task.FromResult<OperatorConsoleStatutoryDiscountDraftDetailResult?>(new(
                DraftId,
                ParkingSessionId,
                "RBAC-TICKET-001",
                "ABC-1234",
                SiteId,
                "RBAC Site",
                SiteGroupId,
                "SENIOR_CITIZEN",
                "REQUESTED",
                StatutoryDiscountDecisionCommandId: null,
                IdDocumentType: null,
                IssuingAuthority: null,
                ExpiryDate: null,
                MaskedIdReference: null,
                RequesterAttestation: null,
                AttestationNotes: null,
                EvidenceRequired: true,
                EvidenceCaptured: false,
                EvidenceRequiredSatisfied: false,
                EvidenceCount: 0,
                LatestEvidenceStatus: null,
                ["SENIOR_CITIZEN_ID"],
                DateTimeOffset.Parse("2026-07-12T09:00:00+08:00"),
                ValidatedAt: null,
                UserId,
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
                PolicySnapshot: JsonSerializer.SerializeToElement(new { nationalLawReference = "RA 9994" }),
                OriginalTariffSnapshotId,
                PayableBasisApplicationId: null,
                StatutoryDiscountPayableBasisApplicationCommandId: null,
                PayableBasisApplicationStatus: null,
                AppliedTariffSnapshotId: null,
                OriginalAmountMinorUnits: 12500,
                VatAmountMinorUnits: null,
                VatExclusiveAmountMinorUnits: null,
                StatutoryDiscountAmountMinorUnits: null,
                PayableAmountMinorUnits: 12500,
                FinalPayableAmountMinorUnits: null,
                "PHP",
                ["Draft requested."]));
        }

        public Task<OperatorConsoleStatutoryDiscountAuditReportResult> ListAuditReportAsync(
            OperatorConsoleStatutoryDiscountAuditReportQuery query,
            CancellationToken cancellationToken)
        {
            AuditReportCallCount++;
            return Task.FromResult(new OperatorConsoleStatutoryDiscountAuditReportResult(
                [
                    new OperatorConsoleStatutoryDiscountAuditReportItemResult(
                        DraftId,
                        ParkingSessionId,
                        "RBAC-TICKET-001",
                        "ABC-1234",
                        query.SiteId ?? SiteId,
                        query.SiteGroupId ?? SiteGroupId,
                        "SENIOR_CITIZEN",
                        "APPROVED",
                        EvidenceRequired: true,
                        EvidenceCaptured: true,
                        EvidenceRequiredSatisfied: true,
                        EvidenceCount: 1,
                        LatestEvidenceStatus: "CAPTURED",
                        PayableBasisApplicationStatus: "APPLIED",
                        OriginalAmountMinorUnits: 12500,
                        StatutoryDiscountAmountMinorUnits: 2232,
                        FinalPayableAmountMinorUnits: 8929,
                        "PHP",
                        UserId,
                        ValidatedByUserId: UserId,
                        DateTimeOffset.Parse("2026-07-12T09:00:00+08:00"),
                        DateTimeOffset.Parse("2026-07-12T10:00:00+08:00"),
                        query.CorrelationId,
                        "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
                        OrdinanceReference: null,
                        "RA 9994",
                        AppliedTariffSnapshotId,
                        "VIEW_AUDIT_REPORT / SUCCESS")
                ],
                TotalCount: 1,
                query.Limit,
                query.Offset,
                query.CorrelationId));
        }

        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccessResult(command));

        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with { EvaluationId = EvaluationId, Persisted = true });

        private static OperatorConsoleResolvedStatutoryDiscountPolicy Policy() =>
            new(
                PolicyId,
                JurisdictionId: null,
                SiteId,
                SiteGroupId,
                "SENIOR_CITIZEN",
                "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
                "RA 9994 Senior Citizen National Fallback",
                "NATIONAL_LAW_FALLBACK",
                "NATIONAL_LAW",
                "LEGAL_REFERENCE",
                "Expanded Senior Citizens Act of 2010",
                OrdinanceReference: null,
                "RA 9994",
                "VERIFIED_OFFICIAL",
                "NON_RESIDENT_ALLOWED",
                "STATUTORY_DISCOUNT_VAT_EXEMPT",
                FreeDurationMinutes: null,
                InitialRateExempt: false,
                FullFeeExempt: false,
                OvernightExcluded: false,
                ValetExcluded: false,
                StandaloneParkingExcluded: false,
                DriverOrPassengerRequired: false,
                "NOT_APPLICABLE",
                "APPLY_NATIONAL_STATUTORY_DISCOUNT",
                "CHARGEABLE_PORTION_ONLY",
                "NO_STACKING_ON_FREE_PERIOD",
                "NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY",
                RequiresOperatorValidation: true,
                RequiresEvidence: true,
                DateOnly.Parse("2026-01-01"),
                EffectiveTo: null,
                SourceReference: "rbac-contract-test",
                JsonSerializer.SerializeToElement(new { nationalLawReference = "RA 9994" }));

        private static StatutoryDiscountServiceChannelReviewDetail ServiceChannelReviewDetail(Guid correlationId) =>
            new(
                DraftId,
                StatutoryDiscountValidationId: null,
                DraftId,
                ParkingSessionId,
                "WEBPAY",
                SiteId,
                SiteGroupId,
                "RBAC-TICKET-001",
                "ABC-1234",
                "SENIOR_CITIZEN",
                "PENDING_REVIEW",
                "PENDING_REVIEW",
                StatutoryDiscountServiceChannelReviewStatuses.PendingReview,
                "SENIOR_CITIZEN_ID",
                "OSCA",
                ExpiryDate: null,
                "SC-RBAC-****-0001",
                [
                    new StatutoryDiscountServiceChannelReviewEvidenceFact(
                        "SENIOR_CITIZEN_ID",
                        "WEBPAY_UPLOAD_REFERENCE",
                        StorageReference: null,
                        "SC-RBAC-****-0001",
                        "SUBMITTED")
                ],
                RequesterAttestation: true,
                "RBAC contract test.",
                ReasonCode: "RBAC_TEST",
                EvidenceRequired: true,
                EvidenceRecorded: true,
                OriginalTariffSnapshotId,
                OriginalAmountMinorUnits: 12500,
                VatExclusiveAmountMinorUnits: null,
                VatAmountMinorUnits: null,
                StatutoryDiscountAmountMinorUnits: null,
                FinalPayableAmountMinorUnits: null,
                "PHP",
                GoverningPolicy(),
                ReviewerUserId: null,
                ReviewerAccessEvaluationId: null,
                ReviewerDecision: null,
                ReviewerReasonCode: null,
                DateTimeOffset.Parse("2026-07-12T09:00:00+08:00"),
                ReviewedAt: null,
                correlationId);

        private static StatutoryDiscountServiceChannelReviewPolicyAuthority GoverningPolicy() =>
            new(
                PolicyVersionId,
                JurisdictionId,
                "137604000",
                "Paranaque City",
                "PARANAQUE_PARKING_PRIVILEGE",
                "2026.07",
                OrdinanceNumber: null,
                OrdinanceTitle: null,
                "VERIFIED_ACTIVE_OPERATIONAL",
                "ACTIVE_FOR_TRANSACTION_USE",
                "PARTIAL_VERIFIED",
                "PARKING_SERVICE_COVERED",
                "FULL_FEE_EXEMPTION",
                "RESIDENT_ONLY",
                OfficialSourceAvailable: false,
                OrdinanceTextAvailable: false,
                OrdinanceNumberAvailable: false,
                DateTimeOffset.Parse("2026-07-01T00:00:00+08:00"),
                EffectiveTo: null,
                [
                    new StatutoryDiscountPolicyEvidenceRequirement(
                        "SENIOR_CITIZEN_ID",
                        "REQUIRED",
                        "Valid Senior Citizen ID",
                        SafeRequirementNotes: null),
                    new StatutoryDiscountPolicyEvidenceRequirement(
                        "RESIDENCY_EVIDENCE",
                        "REQUIRED",
                        "Proof of residency",
                        SafeRequirementNotes: null)
                ],
                "FROZEN_LOCAL_ORDINANCE_POLICY_AUTHORITY");
    }

    private sealed class FakeRbacRepository : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(
            Guid userId,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ServiceIdentityIsActiveAsync(
            Guid serviceIdentityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task RecordDeniedAsync(
            string policyName,
            Guid? userId,
            Guid? serviceIdentityId,
            Guid? correlationId,
            string requestPath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordAuditEventAsync(
            string eventType,
            string eventResult,
            string eventReasonCode,
            string targetEntityType,
            Guid? targetEntityId,
            Guid? actorUserId,
            Guid? actorServiceIdentityId,
            Guid? correlationId,
            string summary,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
