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
/// Verifies the Operator Console fiscal status view-audit report endpoint.
/// </summary>
public sealed class OperatorConsoleFiscalStatusViewAuditReportApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/audit/fiscal-status-views";
    private const string StatusReadPermission = "fiscal-issuance.status.read";
    private static readonly Guid ActionLogEntryId = Guid.Parse("6b000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("6b000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("6b000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("6b000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("6b000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("6b000000-0000-0000-0000-000000000006");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("6b000000-0000-0000-0000-000000000007");
    private static readonly Guid RequestCorrelationId = Guid.Parse("6b000000-0000-0000-0000-000000000008");
    private static readonly Guid RowCorrelationId = Guid.Parse("6b000000-0000-0000-0000-000000000009");

    [Fact]
    public void EndpointRouteExistsWithFiscalIssuanceStatusReadPolicy()
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
            .Be("FiscalIssuanceStatusRead");
    }

    [Fact]
    public async Task Get_WhenAuthorized_ReturnsSafeReportAndPassesFilters()
    {
        var fakeReport = new FakeReportService();
        var fakeAccess = new FakeAccessEvaluationService(allowed: true);
        using var factory = CreateFactory(fakeReport, fakeAccess);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync(
            $"{Endpoint}?from=2026-07-08T01:00:00Z&to=2026-07-08T02:00:00Z&siteId={SiteId}&siteGroupId={SiteGroupId}&operatorUserId={UserId}&fiscalIssuanceReferenceId={FiscalIssuanceReferenceId}&resultClass=NOT_FOUND&correlationId={RowCorrelationId}&limit=500&offset=-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleFiscalStatusViewAuditReportResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].ActionCode.Should().Be(OperatorConsoleActionCodes.ViewFiscalIssuanceStatus);
        body.Items[0].ResultClass.Should().Be("NOT_FOUND");
        body.Items[0].FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        body.Items[0].SafeDenialOrErrorPosture.Should().Be("Fiscal issuance reference was not found.");
        body.CorrelationId.Should().Be(RequestCorrelationId);

        fakeReport.LastQuery.Should().NotBeNull();
        fakeReport.LastQuery!.From.Should().Be(DateTimeOffset.Parse("2026-07-08T01:00:00Z"));
        fakeReport.LastQuery.To.Should().Be(DateTimeOffset.Parse("2026-07-08T02:00:00Z"));
        fakeReport.LastQuery.SiteId.Should().Be(SiteId);
        fakeReport.LastQuery.SiteGroupId.Should().Be(SiteGroupId);
        fakeReport.LastQuery.OperatorUserId.Should().Be(UserId);
        fakeReport.LastQuery.FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        fakeReport.LastQuery.ResultClass.Should().Be("NOT_FOUND");
        fakeReport.LastQuery.CorrelationIdFilter.Should().Be(RowCorrelationId);
        fakeReport.LastQuery.Limit.Should().Be(500);
        fakeReport.LastQuery.Offset.Should().Be(-5);
        fakeReport.LastQuery.CorrelationId.Should().Be(RequestCorrelationId);

        fakeAccess.LastCommand.Should().NotBeNull();
        fakeAccess.LastCommand!.ControlledActionCode.Should().Be(OperatorConsoleActionCodes.ViewFiscalStatusViewAuditReport);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("rawFiscal");
        json.Should().NotContain("posServerRequest");
        json.Should().NotContain("secret");
        json.Should().NotContain("stackTrace");
        json.Should().NotContain("customerPii");
        json.Should().NotContain("paymentProvider");
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

    [Fact]
    public async Task Get_WhenRbacEnabledAndPermissionMissing_ReturnsForbidden()
    {
        var fakeReport = new FakeReportService();
        using var factory = CreateFactory(fakeReport, new FakeAccessEvaluationService(allowed: true));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.evaluate");

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
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_AUDIT_REPORT_ACCESS_DENIED");
        fakeReport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_RemainsGetOnly()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.PostAsync(Endpoint, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
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
                services.RemoveAll<IOperatorConsoleFiscalStatusViewAuditReportService>();
                services.AddSingleton<IOperatorConsoleFiscalStatusViewAuditReportService>(fakeReport);
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(fakeAccess);
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAccessEvaluationWriter());
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static void AddStatusReadHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, StatusReadPermission);
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
            allowed ? "OPERATOR" : null,
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

    private sealed class FakeReportService : IOperatorConsoleFiscalStatusViewAuditReportService
    {
        public int CallCount { get; private set; }

        public OperatorConsoleFiscalStatusViewAuditReportQuery? LastQuery { get; private set; }

        public Task<OperatorConsoleFiscalStatusViewAuditReportResult> ListAsync(
            OperatorConsoleFiscalStatusViewAuditReportQuery query,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            var item = new OperatorConsoleFiscalStatusViewAuditReportItemResult(
                ActionLogEntryId,
                DateTimeOffset.Parse("2026-07-08T01:30:00Z"),
                OperatorConsoleActionCodes.ViewFiscalIssuanceStatus,
                query.ResultClass ?? "SUCCEEDED",
                UserId,
                query.SiteId,
                query.SiteGroupId,
                query.FiscalIssuanceReferenceId ?? FiscalIssuanceReferenceId,
                query.CorrelationIdFilter ?? RowCorrelationId,
                "Fiscal issuance reference was not found.",
                "operator-console-fiscal-issuance-status");

            return Task.FromResult(new OperatorConsoleFiscalStatusViewAuditReportResult(
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
                EvaluationId = Guid.Parse("6b000000-0000-0000-0000-000000000010"),
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
