using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
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
/// Verifies the Operator Console fiscal void action audit review endpoint.
/// </summary>
public sealed class OperatorConsoleFiscalVoidActionAuditReportApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/audit/fiscal-void-actions";
    private const string AuditReadPermission = "fiscal-issuance.void.audit.read";
    private const string StatusReadPermission = "fiscal-issuance.status.read";
    private const string VoidCommandPermission = "fiscal-issuance.void.command";
    private static readonly Guid ActionLogEntryId = Guid.Parse("6d000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("6d000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("6d000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("6d000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("6d000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("6d000000-0000-0000-0000-000000000006");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("6d000000-0000-0000-0000-000000000007");
    private static readonly Guid PosServerFiscalDocumentId = Guid.Parse("6d000000-0000-0000-0000-000000000008");
    private static readonly Guid RequestCorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000009");
    private static readonly Guid RowCorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000010");

    [Fact]
    public void EndpointRouteExistsWithFiscalVoidActionAuditReviewPolicy()
    {
        using var factory = CreateFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Get.Method);
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be("FiscalVoidActionAuditReview");
    }

    [Fact]
    public async Task Get_WhenAuthorized_ReturnsSafeReportAndPassesFilters()
    {
        var fakeReport = new FakeReportService();
        var fakeAccess = new FakeAccessEvaluationService(allowed: true);
        using var factory = CreateFactory(fakeReport, fakeAccess);
        using var client = factory.CreateClient();
        AddHeaders(client, AuditReadPermission);

        using var response = await client.GetAsync(
            $"{Endpoint}?from=2026-07-08T01:00:00Z&to=2026-07-08T02:00:00Z&siteId={SiteId}&siteGroupId={SiteGroupId}&operatorUserId={UserId}&fiscalIssuanceReferenceId={FiscalIssuanceReferenceId}&fiscalDocumentNumber=SI-OCVOID-0001-UAT&resultClass=CONFLICT&correlationId={RowCorrelationId}&limit=500&offset=-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleFiscalVoidActionAuditReportResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].ActionCode.Should().Be(OperatorConsoleActionCodes.VoidFiscalDocument);
        body.Items[0].ResultClass.Should().Be("CONFLICT");
        body.Items[0].FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        body.Items[0].FiscalDocumentNumber.Should().Be("SI-OCVOID-0001-UAT");
        body.Items[0].PosServerFiscalDocumentId.Should().Be(PosServerFiscalDocumentId);
        body.Items[0].ReasonCode.Should().Be("operator_error");
        body.Items[0].CorrelationId.Should().Be(RowCorrelationId);
        body.Items[0].PaymentFinalityChanged.Should().BeFalse();
        body.Items[0].GateBehaviorTriggered.Should().BeFalse();
        body.CorrelationId.Should().Be(RequestCorrelationId);

        fakeReport.LastQuery.Should().NotBeNull();
        fakeReport.LastQuery!.From.Should().Be(DateTimeOffset.Parse("2026-07-08T01:00:00Z"));
        fakeReport.LastQuery.To.Should().Be(DateTimeOffset.Parse("2026-07-08T02:00:00Z"));
        fakeReport.LastQuery.SiteId.Should().Be(SiteId);
        fakeReport.LastQuery.SiteGroupId.Should().Be(SiteGroupId);
        fakeReport.LastQuery.OperatorUserId.Should().Be(UserId);
        fakeReport.LastQuery.FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        fakeReport.LastQuery.FiscalDocumentNumber.Should().Be("SI-OCVOID-0001-UAT");
        fakeReport.LastQuery.ResultClass.Should().Be("CONFLICT");
        fakeReport.LastQuery.CorrelationIdFilter.Should().Be(RowCorrelationId);
        fakeReport.LastQuery.Limit.Should().Be(500);
        fakeReport.LastQuery.Offset.Should().Be(-5);
        fakeReport.LastQuery.CorrelationId.Should().Be(RequestCorrelationId);

        fakeAccess.LastCommand.Should().NotBeNull();
        fakeAccess.LastCommand!.ControlledActionCode.Should().Be(OperatorConsoleActionCodes.ViewFiscalVoidActionAuditReport);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("rawFiscal");
        json.Should().NotContain("posServerRequest");
        json.Should().NotContain("secret");
        json.Should().NotContain("stackTrace");
        json.Should().NotContain("customerPii");
        json.Should().NotContain("paymentProviderPayload");
        json.Should().NotContain("statutoryEvidence");
        json.Should().NotContain("rawPaymentCallback");
    }

    [Fact]
    public async Task Get_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        var fakeReport = new FakeReportService();
        using var factory = CreateFactory(fakeReport, new FakeAccessEvaluationService(allowed: true));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
        fakeReport.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(StatusReadPermission)]
    [InlineData(VoidCommandPermission)]
    public async Task Get_WhenPermissionMissing_ReturnsForbidden(string permission)
    {
        var fakeReport = new FakeReportService();
        using var factory = CreateFactory(fakeReport, new FakeAccessEvaluationService(allowed: true));
        using var client = factory.CreateClient();
        AddHeaders(client, permission);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
        fakeReport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_WhenOperatorConsoleAccessDenied_ReturnsForbiddenWithoutReportRows()
    {
        var fakeReport = new FakeReportService();
        using var factory = CreateFactory(fakeReport, new FakeAccessEvaluationService(allowed: false));
        using var client = factory.CreateClient();
        AddHeaders(client, AuditReadPermission);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("OPERATOR_CONSOLE_FISCAL_VOID_ACTION_AUDIT_REVIEW_ACCESS_DENIED");
        fakeReport.CallCount.Should().Be(0);
    }

    private static CustomWebApplicationFactory CreateFactory() =>
        CreateFactory(new FakeReportService(), new FakeAccessEvaluationService(allowed: true));

    private static CustomWebApplicationFactory CreateFactory(
        FakeReportService fakeReport,
        FakeAccessEvaluationService fakeAccess) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleFiscalVoidActionAuditReportService>();
                services.AddSingleton<IOperatorConsoleFiscalVoidActionAuditReportService>(fakeReport);
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(fakeAccess);
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAccessEvaluationWriter());
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static void AddHeaders(HttpClient client, string permission)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", ShiftId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", SiteGroupId.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", RequestCorrelationId.ToString());
    }

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        OperatorConsoleAccessEvaluationCommand command,
        bool allowed) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            allowed ? [] : ["NO_ACTIVE_SHIFT"],
            allowed ? "SUPERVISOR" : null,
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(command.SiteId, command.SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-07-08T08:00:00Z"),
            Persisted: false,
            command.CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                command.UserId,
                null,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                null,
                command.SiteGroupId,
                command.SiteId,
                command.ControlledActionCode,
                command.WorkflowCode,
                null,
                null));

    private sealed class FakeReportService : IOperatorConsoleFiscalVoidActionAuditReportService
    {
        public int CallCount { get; private set; }

        public OperatorConsoleFiscalVoidActionAuditReportQuery? LastQuery { get; private set; }

        public Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
            OperatorConsoleFiscalVoidActionAuditReportQuery query,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            var item = new OperatorConsoleFiscalVoidActionAuditReportItemResult(
                ActionLogEntryId,
                DateTimeOffset.Parse("2026-07-08T01:30:00Z"),
                OperatorConsoleActionCodes.VoidFiscalDocument,
                query.ResultClass ?? "SUCCEEDED",
                UserId,
                query.SiteId,
                query.SiteGroupId,
                query.FiscalIssuanceReferenceId ?? FiscalIssuanceReferenceId,
                query.FiscalDocumentNumber ?? "SI-OCVOID-0001-UAT",
                PosServerFiscalDocumentId,
                "operator_error",
                null,
                query.CorrelationIdFilter ?? RowCorrelationId,
                null,
                null,
                "fiscal_document_void_idempotency_conflict",
                "operator-console-fiscal-issuance-status",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false);

            return Task.FromResult(new OperatorConsoleFiscalVoidActionAuditReportResult(
                [item],
                TotalCount: 1,
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

        public OperatorConsoleAccessEvaluationCommand? LastCommand { get; private set; }

        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(AccessResult(command, _allowed));
        }
    }

    private sealed class FakeAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with
            {
                EvaluationId = Guid.Parse("6d000000-0000-0000-0000-000000000011"),
                Persisted = true
            });
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
