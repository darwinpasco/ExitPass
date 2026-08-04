using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryEvidence;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class StatutoryEvidenceMetadataApiIntegrationTests
{
    private const string RoutePattern = "/v1/internal/statutory-discounts/evidence/sets";
    private const string Route = "/v1/internal/statutory-discounts/evidence/sets";

    private static readonly Guid UserId = Guid.Parse("12000000-0000-0000-0000-000000000001");
    private static readonly Guid ServiceIdentityId = Guid.Parse("12000000-0000-0000-0000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("12000000-0000-0000-0000-000000000003");
    private static readonly Guid EvidenceSetReference = Guid.Parse("12000000-0000-0000-0000-000000000004");
    private static readonly Guid EvidenceItemReference = Guid.Parse("12000000-0000-0000-0000-000000000005");
    private static readonly Guid UploadAuthorizationReference = Guid.Parse("12000000-0000-0000-0000-00000000000b");
    private static readonly Guid DecisionCommandId = Guid.Parse("12000000-0000-0000-0000-000000000006");
    private static readonly Guid ValidationId = Guid.Parse("12000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("12000000-0000-0000-0000-000000000008");
    private static readonly Guid SiteId = Guid.Parse("12000000-0000-0000-0000-000000000009");
    private static readonly Guid SiteGroupId = Guid.Parse("12000000-0000-0000-0000-00000000000a");

    [Fact]
    public void EvidenceMetadataEndpoint_IsRegisteredWithCapturePolicy()
    {
        using var factory = CreateFactory();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == RoutePattern);

        endpoint.Metadata
            .OfType<ReconciliationPolicyMetadata>()
            .Single()
            .PolicyName.Should().Be("CentralPmsStatutoryEvidenceCaptureMetadata");
    }

    [Fact]
    public async Task CreateEvidenceSet_WhenCapturePermissionPresent_ReturnsSafeMetadataOnlyResponse()
    {
        var service = new FakeEvidenceMetadataService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.evidence.capture");

        using var response = await client.PostAsJsonAsync($"{Route}?correlationId={CorrelationId}", CreateSetRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatutoryEvidenceOperationResponse>();
        body!.Classification.Should().Be("ACCEPTED");
        body.CorrelationId.Should().Be(CorrelationId);
        body.EvidenceSet!.EvidenceSetReference.Should().Be(EvidenceSetReference);
        body.EvidenceSet.Items.Should().ContainSingle();
        body.EvidenceSet.Items[0].EvidenceItemReference.Should().Be(EvidenceItemReference);
        body.EvidenceSet.Items[0].ValidationResultClassification.Should().BeNull();
        body.EvidenceSet.Items[0].ScanResultClassification.Should().BeNull();
        service.LastCreateSetCommand!.Actor.SourceChannel.Should().Be("WEBPAY");
        service.LastCreateSetCommand.Actor.ServiceIdentityId.Should().Be(ServiceIdentityId);
    }

    [Fact]
    public async Task CreateEvidenceSet_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, CreateSetRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task CreateEvidenceSet_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.decision.submit.webpay");

        using var response = await client.PostAsJsonAsync(Route, CreateSetRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task GetEvidenceSet_WhenViewPermissionPresent_ReturnsSafeReadbackOnly()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.evidence.view");

        using var response = await client.GetAsync($"/v1/internal/statutory-discounts/evidence/sets/{EvidenceSetReference}?correlationId={CorrelationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatutoryEvidenceSetResponse>();
        body!.EvidenceSetReference.Should().Be(EvidenceSetReference);
        body.SourceChannel.Should().Be("WEBPAY");
        body.Items.Should().ContainSingle();

        var json = await response.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("storage");
        json.ToLowerInvariant().Should().NotContain("checksum");
        json.ToLowerInvariant().Should().NotContain("signed");
        json.ToLowerInvariant().Should().NotContain("base64");
        json.Should().NotContain("objectKey");
    }

    [Fact]
    public void UploadAuthorizationEndpoint_IsRegisteredWithCapturePolicy()
    {
        using var factory = CreateFactory();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == $"{RoutePattern}/{{evidenceSetReference:guid}}/items/{{evidenceItemReference:guid}}/upload-authorizations");

        endpoint.Metadata
            .OfType<ReconciliationPolicyMetadata>()
            .Single()
            .PolicyName.Should().Be("CentralPmsStatutoryEvidenceCaptureMetadata");
    }

    [Fact]
    public async Task AuthorizeUpload_WhenCapturePermissionPresent_ReturnsShortLivedUploadMaterialWithoutStorageIdentifiers()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.evidence.capture");

        using var response = await client.PostAsJsonAsync(
            $"{Route}/{EvidenceSetReference}/items/{EvidenceItemReference}/upload-authorizations?correlationId={CorrelationId}",
            UploadAuthorizationRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatutoryEvidenceUploadAuthorizationResponse>();
        body!.Classification.Should().Be("ACCEPTED");
        body.UploadAuthorization!.UploadAuthorizationReference.Should().Be(UploadAuthorizationReference);
        body.UploadAuthorization.Method.Should().Be("PUT");
        body.UploadAuthorization.AcceptedContentType.Should().Be("image/jpeg");
        body.EvidenceItem!.UploadStatus.Should().Be("AUTHORIZED");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("uploadUrl");
        json.Should().NotContain("objectKey");
        json.Should().NotContain("bucket");
        json.Should().NotContain("checksum");
        json.Should().NotContain("secret");
    }

    [Fact]
    public async Task AuthorizeUpload_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.evidence.view");

        using var response = await client.PostAsJsonAsync(
            $"{Route}/{EvidenceSetReference}/items/{EvidenceItemReference}/upload-authorizations",
            UploadAuthorizationRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FinalizeUpload_WhenCapturePermissionPresent_ReturnsUploadedWithoutValidationScanOrReviewabilityPass()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.evidence.capture");

        using var response = await client.PostAsJsonAsync(
            $"{Route}/{EvidenceSetReference}/items/{EvidenceItemReference}/upload-finalizations?correlationId={CorrelationId}",
            UploadFinalizationRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatutoryEvidenceUploadFinalizationResponse>();
        body!.Classification.Should().Be("ACCEPTED");
        body.EvidenceItem!.UploadStatus.Should().Be("UPLOADED");
        body.EvidenceItem.ValidationStatus.Should().Be("NOT_STARTED");
        body.EvidenceItem.ScanStatus.Should().Be("NOT_STARTED");
        body.EvidenceItem.ReviewabilityStatus.Should().Be("NOT_REVIEWABLE");
    }

    [Fact]
    public async Task Swagger_DoesNotExposeEvidenceByteOrPreviewOperations()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/internal/statutory-discounts/evidence/sets");
        swaggerJson.Should().NotContain("objectKey");
        swaggerJson.Should().NotContain("\"base64\"");
        swaggerJson.Should().NotContain("\"evidenceBytes\"");
        swaggerJson.Should().NotContain("\"objectStorageLocator\"");
    }

    private static CustomWebApplicationFactory CreateFactory(FakeEvidenceMetadataService? service = null) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IStatutoryEvidenceMetadataService>();
                services.AddSingleton<IStatutoryEvidenceMetadataService>(service ?? new FakeEvidenceMetadataService());
                services.RemoveAll<IStatutoryEvidenceUploadService>();
                services.AddSingleton<IStatutoryEvidenceUploadService>(new FakeEvidenceUploadService());
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static void AddServiceHeaders(HttpClient client, string permission)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
    }

    private static StatutoryEvidenceCreateSetRequest CreateSetRequest() =>
        new(
            DecisionCommandId,
            ValidationId,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            "1",
            "PH_STATUTORY_PARKING_STANDARD",
            "2026-07-28",
            "LOCAL_TEST",
            "i012-api",
            "create-set",
            "WEBPAY");

    private static StatutoryEvidenceUploadAuthorizationRequest UploadAuthorizationRequest() =>
        new(
            "image/jpeg",
            1024,
            "DOCUMENT_IMAGE",
            "SHA256",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "upload-api",
            "authorize",
            "WEBPAY");

    private static StatutoryEvidenceUploadFinalizationRequest UploadFinalizationRequest() =>
        new(
            UploadAuthorizationReference,
            "upload-api",
            "finalize",
            "WEBPAY");

    private static StatutoryEvidenceSetReadModel ReadModel() =>
        new(
            EvidenceSetReference,
            DecisionCommandId,
            ValidationId,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "WEBPAY",
            "OPEN",
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            "1",
            "PH_STATUTORY_PARKING_STANDARD",
            "2026-07-28",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            null,
            CorrelationId,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            [ItemReadModel()]);

    private static StatutoryEvidenceItemReadModel ItemReadModel() =>
        new(
            EvidenceItemReference,
            "SENIOR_CITIZEN_ID",
            "FRONT",
            "NOT_AUTHORIZED",
            "NOT_STARTED",
            "NOT_STARTED",
            "NOT_REVIEWABLE",
            "UNBOUND",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            "DOCUMENT_PROFILE_ONLY",
            null,
            "SENIOR_CITIZEN_ID_FRONT_V1",
            null,
            null,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"));

    private static StatutoryEvidenceItemReadModel AuthorizedItemReadModel() =>
        ItemReadModel() with { UploadStatus = "AUTHORIZED", DeclaredContentType = "image/jpeg" };

    private static StatutoryEvidenceItemReadModel UploadedItemReadModel() =>
        ItemReadModel() with { UploadStatus = "UPLOADED", DeclaredContentType = "image/jpeg" };

    private sealed class FakeEvidenceMetadataService : IStatutoryEvidenceMetadataService
    {
        public StatutoryEvidenceCreateSetCommand? LastCreateSetCommand { get; private set; }

        public Task<StatutoryEvidenceOperationOutcome> CreateOrResolveSetAsync(
            StatutoryEvidenceCreateSetCommand command,
            CancellationToken cancellationToken)
        {
            LastCreateSetCommand = command;
            return Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel(), null));
        }

        public Task<StatutoryEvidenceOperationOutcome> AddItemAsync(StatutoryEvidenceAddItemCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel(), ItemReadModel()));

        public Task<StatutoryEvidenceOperationOutcome> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel() with { SetStatus = "LOCKED_FOR_REVIEW" }, null));

        public Task<StatutoryEvidenceOperationOutcome> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel() with { HoldActive = true, HoldReasonCode = command.ReasonCode }, null));

        public Task<StatutoryEvidenceOperationOutcome> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel(), null));

        public Task<StatutoryEvidenceOperationOutcome> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, ReadModel() with { DeletionStatus = "REQUESTED" }, null));

        public Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken) =>
            Task.FromResult<StatutoryEvidenceSetReadModel?>(ReadModel());
    }

    private sealed class FakeEvidenceUploadService : IStatutoryEvidenceUploadService
    {
        public Task<StatutoryEvidenceUploadAuthorizationOutcome> AuthorizeUploadAsync(
            StatutoryEvidenceUploadAuthorizationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceUploadAuthorizationOutcome(
                "ACCEPTED",
                false,
                null,
                command.CorrelationId,
                new StatutoryEvidenceUploadAuthorizationReadModel(
                    UploadAuthorizationReference,
                    "PUT",
                    new Uri("https://storage.local/short-lived-upload-material"),
                    new Dictionary<string, string>
                    {
                        ["Content-Type"] = command.DeclaredContentType
                    },
                    DateTimeOffset.Parse("2026-08-03T00:05:00Z"),
                    5_000_000,
                    command.DeclaredContentType,
                    command.CorrelationId),
                AuthorizedItemReadModel()));

        public Task<StatutoryEvidenceUploadFinalizationOutcome> FinalizeUploadAsync(
            StatutoryEvidenceUploadFinalizationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceUploadFinalizationOutcome(
                "ACCEPTED",
                false,
                null,
                command.CorrelationId,
                UploadedItemReadModel()));
    }

    private sealed class FakeRbacRepository : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ServiceIdentityIsActiveAsync(Guid serviceIdentityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task RecordDeniedAsync(string policyName, Guid? userId, Guid? serviceIdentityId, Guid? correlationId, string requestPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordAuditEventAsync(string eventType, string eventResult, string eventReasonCode, string targetEntityType, Guid? targetEntityId, Guid? actorUserId, Guid? actorServiceIdentityId, Guid? correlationId, string summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
