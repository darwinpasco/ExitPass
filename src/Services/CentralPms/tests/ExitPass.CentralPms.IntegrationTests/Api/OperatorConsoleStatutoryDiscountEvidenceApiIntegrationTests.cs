using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies Operator Console statutory discount evidence metadata endpoints.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
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
    public void SubmittedEvidencePreviewRouteExistsAsAuthorizedPost()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(item => item.RoutePattern.RawText == "/v1/ops/operator-console/statutory-discounts/{draftId:guid}/evidence/preview");

        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
        endpoint.Metadata.GetMetadata<OperatorConsoleOperatingContextRequirementMetadata>()
            .Should().Be(OperatorConsoleOperatingContextRequirementMetadata.NotRequired);
    }

    [Fact]
    public async Task EvidenceEndpointsAppearInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/{draftId}/evidence");
        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/{draftId}/evidence/preview");
        swaggerJson.Should().Contain("CaptureOperatorConsoleStatutoryDiscountEvidence");
        swaggerJson.Should().Contain("ListOperatorConsoleStatutoryDiscountEvidence");
        swaggerJson.Should().Contain("PreviewSubmittedOperatorConsoleStatutoryEvidence");
    }

    [Fact]
    public async Task SubmittedEvidencePreview_WhenRequestedByCreator_StreamsRestrictedImageAndRefreshesAuthorization()
    {
        var repository = new FakeEvidenceRepository();
        var storage = new FakeProtectedObjectStorage();
        using var factory = new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountEvidenceRepository>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.RemoveAll<IStatutoryEvidenceProtectedObjectStorageAdapter>();
                services.RemoveAll<IOptions<StatutoryEvidenceUploadOptions>>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountEvidenceRepository>(repository);
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAllowedAccessEvaluationService());
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAllowedAccessEvaluationWriter());
                services.AddSingleton<IStatutoryEvidenceProtectedObjectStorageAdapter>(storage);
                services.AddSingleton(Options.Create(new StatutoryEvidenceUploadOptions
                {
                    BucketName = "restricted-evidence",
                    MaxContentLengthBytes = 1024
                }));
            });
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/ops/operator-console/statutory-discounts/{DraftId}/evidence/preview");
            AddOperatorHeaders(request);
            request.Content = JsonContent.Create(new OperatorConsoleSubmittedEvidencePreviewRequest(EvidenceId));

            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(FakeProtectedObjectStorage.ImageBytes);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        repository.PreviewReads.Should().Be(2);
        storage.ContentReads.Should().Be(2);
    }

    [Fact]
    public async Task CaptureEvidence_WhenStatutoryIdTypeUsesLegacyMetadataRoute_ReturnsPhotoRequired()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_ID_PHOTO_REQUIRED");
    }

    [Fact]
    public async Task CaptureEvidence_WhenStatutoryIdTypeAndDraftMissing_StillFailsClosedAsPhotoRequired()
    {
        using var factory = CreateFactory(missing: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_ID_PHOTO_REQUIRED");
    }

    [Fact]
    public async Task CaptureEvidence_WhenStatutoryIdTypeAndServiceWouldReject_StillRequiresProtectedPhoto()
    {
        using var factory = CreateFactory(throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(EvidenceEndpoint(), CaptureRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("STATUTORY_ID_PHOTO_REQUIRED");
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
                        null,
                        query.UserId,
                        DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
                        "NOT_REDACTED",
                        "CAPTURED",
                        query.CorrelationId)
                ],
                query.CorrelationId));
        }
    }

    private sealed class FakeEvidenceRepository : IOperatorConsoleStatutoryDiscountEvidenceRepository
    {
        public int PreviewReads { get; private set; }

        public Task<OperatorConsoleStatutoryDiscountEvidenceDraftContext?> GetDraftContextAsync(
            Guid draftId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult> CaptureAsync(
            OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperatorConsoleStatutoryDiscountEvidenceListResult> ListAsync(
            Guid draftId,
            Guid correlationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperatorConsoleStatutoryDiscountEvidencePreviewTarget?> GetPreviewTargetAsync(
            Guid draftId,
            Guid evidenceId,
            CancellationToken cancellationToken)
        {
            PreviewReads++;
            return Task.FromResult<OperatorConsoleStatutoryDiscountEvidencePreviewTarget?>(new(
                evidenceId,
                draftId,
                Guid.Parse("67000000-0000-0000-0000-000000000009"),
                SiteId,
                SiteGroupId,
                UserId,
                "SENIOR_CITIZEN_ID",
                "restricted/2026/09/id-photo.jpg",
                "ABC123",
                "CAPTURED"));
        }
    }

    private sealed class FakeProtectedObjectStorage : IStatutoryEvidenceProtectedObjectStorageAdapter
    {
        public static readonly byte[] ImageBytes = [0xff, 0xd8, 0xff, 0xd9];
        public int ContentReads { get; private set; }

        public Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(
            StatutoryEvidenceObjectUploadAuthorizationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryEvidenceObjectUploadResult> UploadObjectAsync(
            StatutoryEvidenceObjectUploadRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(
            StatutoryEvidenceObjectMetadataRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryEvidenceObjectContent> GetObjectContentAsync(
            StatutoryEvidenceObjectContentRequest request,
            CancellationToken cancellationToken) => OpenObjectContentStreamAsync(request, cancellationToken);

        public Task<StatutoryEvidenceObjectContent> OpenObjectContentStreamAsync(
            StatutoryEvidenceObjectContentRequest request,
            CancellationToken cancellationToken)
        {
            ContentReads++;
            return Task.FromResult(new StatutoryEvidenceObjectContent(
                new MemoryStream(ImageBytes, writable: false),
                "image/jpeg",
                ImageBytes.Length,
                "ABC123",
                "version-1",
                "PRIVATE"));
        }
    }

    private sealed class FakeAllowedAccessEvaluationService : IOperatorConsoleAccessEvaluationService
    {
        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) => Task.FromResult(AllowedAccess(command) with { Persisted = false });
    }

    private sealed class FakeAllowedAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) => Task.FromResult(result with { EvaluationId = Guid.NewGuid(), Persisted = true });
    }

    private static OperatorConsoleAccessEvaluationResult AllowedAccess(OperatorConsoleAccessEvaluationCommand command) =>
        new(
            Guid.Empty,
            Allowed: true,
            "ALLOWED",
            [],
            "OPERATOR",
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(command.SiteId ?? SiteId, command.SiteGroupId ?? SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-09-28T14:30:00+08:00"),
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
                "PARKING_SESSION",
                command.ParkingSessionId));
}

