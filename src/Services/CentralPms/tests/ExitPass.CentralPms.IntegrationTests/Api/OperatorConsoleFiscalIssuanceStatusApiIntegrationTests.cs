using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console read-only fiscal issuance status facade.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceStatusApiIntegrationTests
{
    private const string StatusReadPermission = "fiscal-issuance.status.read";
    private const string FiscalVoidPermission = "fiscal-issuance.void.command";
    private static readonly Guid ReferenceId = Guid.Parse("5f000000-0000-0000-0000-000000000001");
    private static readonly Guid EvaluationId = Guid.Parse("5f000000-0000-0000-0000-000000000002");
    private static readonly Guid UserId = Guid.Parse("5f000000-0000-0000-0000-000000000003");
    private static readonly Guid DeviceBindingId = Guid.Parse("5f000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteId = Guid.Parse("5f000000-0000-0000-0000-000000000005");
    private static readonly Guid SiteGroupId = Guid.Parse("5f000000-0000-0000-0000-000000000006");
    private static readonly Guid ShiftId = Guid.Parse("5f000000-0000-0000-0000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("5f000000-0000-0000-0000-000000000008");
    private static readonly Guid OperatorActionRequestId = Guid.Parse("5f000000-0000-0000-0000-000000000018");
    private static readonly string Endpoint = $"/v1/ops/operator-console/fiscal-issuance/references/{ReferenceId}";
    private const string LookupEndpoint = "/v1/ops/operator-console/fiscal-issuance/lookup";
    private static readonly string VoidEndpoint = $"/v1/ops/operator-console/fiscal-issuance/references/{ReferenceId}/void";

    [Fact]
    public void EndpointRouteExistsWithFiscalIssuanceStatusReadPolicy()
    {
        using var factory = CreateFactory(Result(Status()));

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId:guid}")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Get.Method);
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be("FiscalIssuanceStatusRead");
    }

    [Fact]
    public void VoidEndpointRouteExistsWithFiscalIssuanceVoidPolicy()
    {
        using var factory = CreateVoidFactory(VoidResult(VoidCommandResult()));

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId:guid}/void")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be("FiscalIssuanceVoidCommand");
    }

    [Fact]
    public void LookupEndpointRouteExistsWithFiscalIssuanceStatusReadPolicy()
    {
        using var factory = CreateFactory(Result(Status()));

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/fiscal-issuance/lookup")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Get.Method);
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be("FiscalIssuanceStatusRead");
    }

    [Fact]
    public async Task Get_WhenAuthorizedAndReferenceExists_ReturnsFiscalStatus()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FiscalIssuanceStatusResponse>();
        body.Should().NotBeNull();
        body!.FiscalIssuanceReferenceId.Should().Be(ReferenceId);
        body.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_RECORDED");
        body.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");

        fake.CallCount.Should().Be(1);
        fake.LastQuery.Should().NotBeNull();
        fake.LastQuery!.UserId.Should().Be(UserId);
        fake.LastQuery.OperatorDeviceBindingId.Should().Be(DeviceBindingId);
        fake.LastQuery.OperatorShiftId.Should().Be(ShiftId);
        fake.LastQuery.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task Lookup_WhenFiscalDocumentNumberResolves_ReturnsFiscalStatus()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync($"{LookupEndpoint}?query=SI-00000001-UAT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FiscalIssuanceStatusResponse>();
        body.Should().NotBeNull();
        body!.FiscalIssuanceReferenceId.Should().Be(ReferenceId);
        body.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");
        fake.LookupCallCount.Should().Be(1);
        fake.LastLookupQuery.Should().NotBeNull();
        fake.LastLookupQuery!.Query.Should().Be("SI-00000001-UAT");
        fake.LastLookupQuery.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Lookup_WhenGuidProvided_ReturnsFiscalStatusThroughLookupPath()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync($"{LookupEndpoint}?query={ReferenceId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.LookupCallCount.Should().Be(1);
        fake.LastLookupQuery!.Query.Should().Be(ReferenceId.ToString("D"));
    }

    [Fact]
    public async Task Lookup_WhenBlank_ReturnsBadRequest()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync($"{LookupEndpoint}?query=%20%20");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_FISCAL_STATUS_LOOKUP");
        fake.LookupCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Lookup_WhenMissing_ReturnsSafeNotFound()
    {
        using var factory = CreateFactory(Result(
            status: null,
            safeErrorCode: "FISCAL_ISSUANCE_LOOKUP_NOT_FOUND",
            safeErrorPosture: "Fiscal status lookup did not match a fiscal issuance reference."));
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync($"{LookupEndpoint}?query=SI-MISSING-UAT");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("FISCAL_ISSUANCE_LOOKUP_NOT_FOUND");
    }

    [Fact]
    public async Task Lookup_WhenAmbiguous_ReturnsSafeConflict()
    {
        using var factory = CreateFactory(Result(
            status: null,
            safeErrorCode: "FISCAL_DOCUMENT_NUMBER_LOOKUP_AMBIGUOUS",
            safeErrorPosture: "Fiscal document number lookup matched multiple fiscal issuance references.",
            lookupAmbiguous: true));
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync($"{LookupEndpoint}?query=SI-DUP-UAT");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("FISCAL_DOCUMENT_NUMBER_LOOKUP_AMBIGUOUS");
    }

    [Fact]
    public async Task Lookup_WhenRbacEnabledAndPermissionMissing_ReturnsForbidden()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.evaluate");

        using var response = await client.GetAsync($"{LookupEndpoint}?query=SI-00000001-UAT");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fake.LookupCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_WhenAuthorizedAndReferenceMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(Result(status: null));
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("FISCAL_ISSUANCE_REFERENCE_NOT_FOUND");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task Get_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_WhenRbacEnabledAndPermissionMissing_ReturnsForbidden()
    {
        var fake = new FakeFiscalStatusService(Result(Status()));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.evaluate");

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_WhenOperatorConsoleAccessDenied_ReturnsForbiddenWithoutFiscalDetails()
    {
        using var factory = CreateFactory(Result(status: null, accessAllowed: false));
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.GetAsync(Endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("OPERATOR_CONSOLE_FISCAL_STATUS_ACCESS_DENIED");
    }

    [Fact]
    public async Task Get_RemainsGetOnly()
    {
        using var factory = CreateFactory(Result(Status()));
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.PostAsync(Endpoint, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Void_WhenAuthorized_ReturnsSafeVoidResult()
    {
        var fake = new FakeFiscalVoidService(VoidResult(VoidCommandResult()));
        using var factory = CreateVoidFactory(fake);
        using var client = factory.CreateClient();
        AddFiscalVoidHeaders(client);

        using var response = await client.PostAsJsonAsync(VoidEndpoint, VoidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleFiscalIssuanceVoidResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("pos_server_void_recorded");
        body.Accepted.Should().BeTrue();
        body.NewFiscalNumberAllocated.Should().BeFalse();
        body.GateBehaviorTriggered.Should().BeFalse();

        fake.CallCount.Should().Be(1);
        fake.LastCommand.Should().NotBeNull();
        fake.LastCommand!.FiscalIssuanceReferenceId.Should().Be(ReferenceId);
        fake.LastCommand.OperatorActionRequestId.Should().Be(OperatorActionRequestId);
        fake.LastCommand.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Void_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        var fake = new FakeFiscalVoidService(VoidResult(VoidCommandResult()));
        using var factory = CreateVoidFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(VoidEndpoint, VoidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Void_WhenCallerHasOnlyStatusReadPermission_ReturnsForbidden()
    {
        var fake = new FakeFiscalVoidService(VoidResult(VoidCommandResult()));
        using var factory = CreateVoidFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadHeaders(client);

        using var response = await client.PostAsJsonAsync(VoidEndpoint, VoidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Void_WhenOperatorAccessDenied_ReturnsForbiddenWithoutCallingInternalEndpoint()
    {
        using var factory = CreateVoidFactory(VoidResult(voidResult: null, accessAllowed: false));
        using var client = factory.CreateClient();
        AddFiscalVoidHeaders(client);

        using var response = await client.PostAsJsonAsync(VoidEndpoint, VoidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("OPERATOR_CONSOLE_FISCAL_VOID_ACCESS_DENIED");
    }

    [Fact]
    public async Task Void_WhenServiceReturnsConflict_ReturnsConflictWithSafeStatus()
    {
        using var factory = CreateVoidFactory(VoidResult(VoidCommandResult(
            accepted: false,
            status: "pos_server_void_conflict",
            httpStatusCode: 409,
            errors: ["fiscal_document_void_idempotency_conflict"],
            classification: "conflict")));
        using var client = factory.CreateClient();
        AddFiscalVoidHeaders(client);

        using var response = await client.PostAsJsonAsync(VoidEndpoint, VoidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleFiscalIssuanceVoidResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("pos_server_void_conflict");
        body.Errors.Should().Contain("fiscal_document_void_idempotency_conflict");
    }

    private static CustomWebApplicationFactory CreateFactory(OperatorConsoleFiscalIssuanceStatusResult result) =>
        CreateFactory(new FakeFiscalStatusService(result));

    private static CustomWebApplicationFactory CreateFactory(FakeFiscalStatusService fake) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleFiscalIssuanceStatusService>();
                services.AddSingleton<IOperatorConsoleFiscalIssuanceStatusService>(fake);
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static CustomWebApplicationFactory CreateVoidFactory(OperatorConsoleFiscalIssuanceVoidResult result) =>
        CreateVoidFactory(new FakeFiscalVoidService(result));

    private static CustomWebApplicationFactory CreateVoidFactory(FakeFiscalVoidService fake) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleFiscalIssuanceVoidService>();
                services.AddSingleton<IOperatorConsoleFiscalIssuanceVoidService>(fake);
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
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
    }

    private static void AddFiscalVoidHeaders(HttpClient client)
    {
        AddStatusReadHeaders(client);
        client.DefaultRequestHeaders.Remove(CentralPmsRbacPolicyCatalog.PermissionsHeaderName);
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, FiscalVoidPermission);
    }

    private static OperatorConsoleFiscalIssuanceStatusResult Result(
        FiscalIssuanceStatusReadModel? status,
        bool accessAllowed = true,
        string? safeErrorCode = null,
        string? safeErrorPosture = null,
        bool lookupAmbiguous = false) =>
        new(
            EvaluationId,
            accessAllowed,
            accessAllowed ? "ALLOWED" : "DENIED",
            accessAllowed ? Array.Empty<string>() : ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            status,
            CorrelationId,
            safeErrorCode,
            safeErrorPosture,
            lookupAmbiguous);

    private static OperatorConsoleFiscalIssuanceVoidRequest VoidRequest() =>
        new(
            OperatorActionRequestId,
            ReasonCode: "operator_error",
            ReasonText: "Operator selected wrong fiscal document.",
            ConfirmationText: OperatorConsoleFiscalIssuanceVoidService.ConfirmationPhrase,
            CorrelationId);

    private static OperatorConsoleFiscalIssuanceVoidResult VoidResult(
        FiscalIssuanceVoidCommandResponse? voidResult,
        bool accessAllowed = true) =>
        new(
            EvaluationId,
            accessAllowed,
            accessAllowed ? "ALLOWED" : "DENIED",
            accessAllowed ? Array.Empty<string>() : ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            voidResult,
            CorrelationId);

    private static FiscalIssuanceVoidCommandResponse VoidCommandResult(
        bool accepted = true,
        string status = "pos_server_void_recorded",
        int httpStatusCode = 200,
        IReadOnlyList<string>? errors = null,
        string classification = "newly_voided") =>
        new(
            accepted,
            status,
            httpStatusCode,
            errors ?? Array.Empty<string>(),
            ReferenceId,
            Guid.Parse("5f000000-0000-0000-0000-000000000014"),
            "SI-00000001-UAT",
            1,
            accepted ? "voided" : null,
            accepted ? "recorded" : null,
            accepted ? "operator_error" : null,
            accepted ? DateTimeOffset.Parse("2026-07-10T00:00:00Z") : null,
            classification,
            $"operator-console-fiscal-void:{ReferenceId:D}:{OperatorActionRequestId:D}",
            CorrelationId.ToString("D"),
            accepted ? null : "do_not_retry_without_request_change",
            NewFiscalNumberAllocated: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            RefundOrReversalCreated: false,
            HikCentralCalled: false,
            PaymentProviderCalled: false,
            RenderingGenerated: false,
            ReplacementFiscalDocumentCreated: false,
            FiscalSequenceChangedByCentralPms: false,
            IdempotentReplay: status == "pos_server_void_idempotent_replay");

    private static FiscalIssuanceStatusReadModel Status()
    {
        var now = DateTimeOffset.Parse("2026-07-08T08:00:00Z");
        return new FiscalIssuanceStatusReadModel(
            ReferenceId,
            "FISCAL_ISSUANCE_RECORDED",
            ResultClassification: "NEWLY_CREATED",
            FiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            FiscalNumberAssignmentState: "ASSIGNED",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
            PaymentConfirmationId: Guid.Parse("5f000000-0000-0000-0000-000000000009"),
            PaymentAttemptId: Guid.Parse("5f000000-0000-0000-0000-000000000010"),
            ParkingSessionId: Guid.Parse("5f000000-0000-0000-0000-000000000011"),
            SiteId,
            SitePosServerId: Guid.Parse("5f000000-0000-0000-0000-000000000012"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentTypeCodeId: Guid.Parse("5f000000-0000-0000-0000-000000000013"),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            PosServerFiscalDocumentId: Guid.Parse("5f000000-0000-0000-0000-000000000014"),
            FiscalDocumentNumber: "SI-00000001-UAT",
            FiscalIdentityId: Guid.Parse("5f000000-0000-0000-0000-000000000015"),
            FiscalSequencePolicyId: Guid.Parse("5f000000-0000-0000-0000-000000000016"),
            FiscalSequenceValue: 1,
            FiscalSeries: "UAT-SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: now,
            FiscalNumberAssignedByRef: "pos-server",
            SemanticRequestHashValue: "hash-value",
            SemanticRequestHashVersion: "sha256:v1",
            SemanticRequestHashStatus: "AVAILABLE",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceFactCount: 24,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            LatestExceptionReason: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            CorrelationId);
    }

    private sealed class FakeFiscalStatusService : IOperatorConsoleFiscalIssuanceStatusService
    {
        private readonly OperatorConsoleFiscalIssuanceStatusResult _result;

        public FakeFiscalStatusService(OperatorConsoleFiscalIssuanceStatusResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public int LookupCallCount { get; private set; }

        public OperatorConsoleFiscalIssuanceStatusQuery? LastQuery { get; private set; }
        public OperatorConsoleFiscalIssuanceLookupQuery? LastLookupQuery { get; private set; }

        public Task<OperatorConsoleFiscalIssuanceStatusResult> GetAsync(
            OperatorConsoleFiscalIssuanceStatusQuery query,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult(_result);
        }

        public Task<OperatorConsoleFiscalIssuanceStatusResult> LookupAsync(
            OperatorConsoleFiscalIssuanceLookupQuery query,
            CancellationToken cancellationToken)
        {
            LookupCallCount++;
            LastLookupQuery = query;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeFiscalVoidService : IOperatorConsoleFiscalIssuanceVoidService
    {
        private readonly OperatorConsoleFiscalIssuanceVoidResult _result;

        public FakeFiscalVoidService(OperatorConsoleFiscalIssuanceVoidResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public OperatorConsoleFiscalIssuanceVoidCommand? LastCommand { get; private set; }

        public Task<OperatorConsoleFiscalIssuanceVoidResult> VoidAsync(
            OperatorConsoleFiscalIssuanceVoidCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCommand = command;
            return Task.FromResult(_result);
        }
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
