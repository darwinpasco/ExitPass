using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class PosServerSalesInvoiceProfileAdministrationClientTests
{
    private static readonly Guid CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResponseCorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FiscalIdentityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ProfileId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SiteId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SitePosServerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private const string ApiKey = "placeholder-admin-api-key-never-print";

    [Fact]
    public async Task PosServerSalesInvoiceProfile_DisabledConfiguration_PerformsNoHttpCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, new PosServerSalesInvoiceProfileAdministrationOptions());

        var result = await client.GetFiscalIdentityAsync(FiscalIdentityId, Context(), CancellationToken.None);

        result.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.Disabled);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, ApiKey, "pos_server_sales_invoice_profile_admin_base_url_required")]
    [InlineData("", ApiKey, "pos_server_sales_invoice_profile_admin_base_url_required")]
    [InlineData("not-a-url", ApiKey, "pos_server_sales_invoice_profile_admin_base_url_invalid")]
    [InlineData("https://pos-server-admin.test", null, "pos_server_sales_invoice_profile_admin_api_key_required")]
    [InlineData("https://pos-server-admin.test", "", "pos_server_sales_invoice_profile_admin_api_key_required")]
    public async Task PosServerSalesInvoiceProfile_EnabledConfiguration_FailsSafelyWhenRequiredValuesAreMissing(
        string? baseUrl,
        string? apiKey,
        string expectedCode)
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, new PosServerSalesInvoiceProfileAdministrationOptions
        {
            Enabled = true,
            BaseUrl = baseUrl,
            ApiKey = apiKey
        });

        var result = await client.GetFiscalIdentityAsync(FiscalIdentityId, Context(), CancellationToken.None);

        result.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.InvalidConfiguration);
        result.Error!.Code.Should().Be(expectedCode);
        result.Error.Message.Should().NotContain(ApiKey);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_ApiKeyAndCorrelation_AreAttachedOnlyToServerRequest()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() }));
        var client = CreateClient(handler);

        await client.CreateFiscalIdentityAsync(FiscalIdentityMutation(), Context(), CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers["X-PosServer-Admin-Key"].Should().Be(ApiKey);
        request.Headers["X-Correlation-Id"].Should().Be(CorrelationId.ToString("D"));
        request.Headers.Should().NotContainKey("X-PosServer-Admin-Permission");
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_FiscalIdentityCreateReadUpdate_MapGovernedFields()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Created, new { data = FiscalIdentityPayload() }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload(updatedByRef: "admin:update") }));
        var client = CreateClient(handler);

        var created = await client.CreateFiscalIdentityAsync(FiscalIdentityMutation(), Context(), CancellationToken.None);
        var read = await client.GetFiscalIdentityAsync(FiscalIdentityId, Context(), CancellationToken.None);
        var updated = await client.UpdateFiscalIdentityAsync(FiscalIdentityId, FiscalIdentityMutation(), Context(), CancellationToken.None);

        created.Value!.FiscalIdentityId.Should().Be(FiscalIdentityId);
        read.Value!.Tin.Should().Be("123-456-789-000");
        updated.Value!.UpdatedByRef.Should().Be("admin:update");
        handler.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Post, HttpMethod.Get, HttpMethod.Put);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_ProfileCreateReadListUpdate_MapDistinctStatutoryDates()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Created, new { data = ProfilePayload() }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ProfilePayload() }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { profiles = new[] { ProfilePayload() } }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ProfilePayload(lifecycleState: "DRAFT") }));
        var client = CreateClient(handler);

        var created = await client.CreateProfileAsync(ProfileMutation(), Context(), CancellationToken.None);
        var read = await client.GetProfileAsync(ProfileId, Context(), CancellationToken.None);
        var listed = await client.ListProfilesAsync(new ManagementPlatformSalesInvoiceHeaderProfileListRequest(SiteId, SitePosServerId, "DRAFT"), Context(), CancellationToken.None);
        var updated = await client.UpdateDraftProfileAsync(ProfileId, ProfileMutation(), Context(), CancellationToken.None);

        created.Value!.BirAccreditationIssuedDate.Should().Be(new DateOnly(2026, 1, 5));
        read.Value!.BirAccreditationValidUntil.Should().Be(new DateOnly(2027, 1, 5));
        updated.Value!.PtuIssuedDate.Should().Be(new DateOnly(2026, 1, 7));
        listed.Value.Should().ContainSingle();
        handler.Requests[2].Uri.Query.Should().Contain("siteId=");
        handler.Requests[2].Uri.Query.Should().Contain("sitePosServerId=");
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_ValidationApprovalRetirement_MapSuccessfully()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ValidationPayload(["birAccreditationNumber", "ptuIssuedDate"]) }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ProfilePayload(lifecycleState: "APPROVED") }));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ProfilePayload(lifecycleState: "RETIRED") }));
        var client = CreateClient(handler);

        var validation = await client.ValidateProfileAsync(ProfileId, Context(), CancellationToken.None);
        var approval = await client.ApproveProfileAsync(ProfileId, new ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest("admin:approve"), Context(), CancellationToken.None);
        var retirement = await client.RetireProfileAsync(ProfileId, new ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest("admin:retire", DateTimeOffset.Parse("2026-07-18T10:00:00Z")), Context(), CancellationToken.None);

        validation.Value!.MissingOrInvalidFieldCodes.Should().Equal("birAccreditationNumber", "ptuIssuedDate");
        approval.Value!.LifecycleState.Should().Be("APPROVED");
        retirement.Value!.LifecycleState.Should().Be("RETIRED");
    }

    [Theory]
    [InlineData("READY")]
    [InlineData("NO_EFFECTIVE_PROFILE")]
    [InlineData("INCOMPLETE")]
    [InlineData("EXPIRED")]
    [InlineData("AMBIGUOUS")]
    [InlineData("UNSUPPORTED_VERSION")]
    [InlineData("RETIRED")]
    [InlineData("FUTURE_UNKNOWN_READYISH")]
    public async Task PosServerSalesInvoiceProfile_ReadinessValues_ArePreservedSafely(string status)
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = ReadinessPayload(status) }));
        var client = CreateClient(handler);

        var result = await client.GetEffectiveReadinessAsync(
            new ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest(SiteId, SitePosServerId, DateTimeOffset.Parse("2026-07-18T09:00:00Z")),
            Context(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.ResolutionStatus.Should().Be(status);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_UsageResponse_MapsImmutableSnapshotUsageOnly()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = UsagePayload() }));
        var client = CreateClient(handler);

        var result = await client.GetProfileUsageAsync(ProfileId, Context(), CancellationToken.None);

        result.Value!.FiscalDocumentCount.Should().Be(2);
        result.Value.SafeFiscalDocumentIdentifiers.Should().Equal("SI-000001", "SI-000002");
        result.Value.DestructiveMutationBlocked.Should().BeTrue();
    }

    [Theory]
    [InlineData(400, PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest)]
    [InlineData(401, PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed)]
    [InlineData(403, PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied)]
    [InlineData(404, PosServerSalesInvoiceProfileAdminOutcome.NotFound)]
    [InlineData(409, PosServerSalesInvoiceProfileAdminOutcome.Conflict)]
    [InlineData(422, PosServerSalesInvoiceProfileAdminOutcome.ValidationFailure)]
    [InlineData(429, PosServerSalesInvoiceProfileAdminOutcome.Throttled)]
    [InlineData(500, PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable)]
    public async Task PosServerSalesInvoiceProfile_ErrorResponses_MapToSafeOutcomes(
        int statusCode,
        PosServerSalesInvoiceProfileAdminOutcome expectedOutcome)
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse((HttpStatusCode)statusCode, new
        {
            errorCode = "safe_pos_server_error",
            message = "Safe POS Server error.",
            correlationId = ResponseCorrelationId
        }));
        var client = CreateClient(handler);

        var result = await client.CreateProfileAsync(ProfileMutation(), Context(), CancellationToken.None);

        result.Outcome.Should().Be(expectedOutcome);
        result.Error!.Code.Should().Be("safe_pos_server_error");
        result.CorrelationId.Should().Be(ResponseCorrelationId);
        result.Error.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_TimeoutAndNetworkFailures_MapSafely()
    {
        var timeoutHandler = new RecordingHandler();
        timeoutHandler.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("simulated timeout without secret")));
        timeoutHandler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() }));
        var timeoutClient = CreateClient(timeoutHandler);

        var timeout = await timeoutClient.CreateFiscalIdentityAsync(FiscalIdentityMutation(), Context(), CancellationToken.None);

        timeout.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.Timeout);
        timeout.MutationSent.Should().BeTrue();
        timeoutHandler.Requests.Should().HaveCount(1);

        var networkHandler = new RecordingHandler();
        networkHandler.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("simulated connection failure without secret")));
        networkHandler.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("simulated connection failure without secret")));
        var networkClient = CreateClient(networkHandler);

        var network = await networkClient.GetProfileAsync(ProfileId, Context(), CancellationToken.None);

        network.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure);
        network.Retried.Should().BeTrue();
        networkHandler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_MalformedResponse_MapsSafely()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.GetFiscalIdentityAsync(FiscalIdentityId, Context(), CancellationToken.None);

        result.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.MalformedResponse);
        result.Error!.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_GetRetryIsBounded_AndMutationsAreNotRetried()
    {
        var getHandler = new RecordingHandler();
        getHandler.Enqueue(JsonResponse(HttpStatusCode.InternalServerError, new { errorCode = "server_busy", message = "busy" }));
        getHandler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() }));
        var getClient = CreateClient(getHandler);

        var getResult = await getClient.GetFiscalIdentityAsync(FiscalIdentityId, Context(), CancellationToken.None);

        getResult.Succeeded.Should().BeTrue();
        getResult.Retried.Should().BeTrue();
        getHandler.Requests.Should().HaveCount(2);

        var mutationHandler = new RecordingHandler();
        mutationHandler.Enqueue(JsonResponse(HttpStatusCode.InternalServerError, new { errorCode = "server_busy", message = "busy" }));
        mutationHandler.Enqueue(JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() }));
        var mutationClient = CreateClient(mutationHandler);

        var mutationResult = await mutationClient.CreateFiscalIdentityAsync(FiscalIdentityMutation(), Context(), CancellationToken.None);

        mutationResult.Succeeded.Should().BeFalse();
        mutationResult.Retried.Should().BeFalse();
        mutationHandler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task PosServerSalesInvoiceProfile_ApplicationService_ValidatesShapeAndPreservesPosServerAuthority()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Created, new { data = ProfilePayload() }));
        var service = new SalesInvoiceProfileAdministrationService(CreateClient(handler));

        var invalid = await service.CreateProfileAsync(
            ProfileMutation() with { FiscalIdentityId = Guid.Empty },
            Context(),
            CancellationToken.None);
        var valid = await service.CreateProfileAsync(ProfileMutation(), Context(), CancellationToken.None);

        invalid.Outcome.Should().Be(PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest);
        valid.Succeeded.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public void PosServerSalesInvoiceProfile_BrowserFacingDtosAndErrors_ContainNoApiKeyOrSecretFields()
    {
        var contractTypes = typeof(ManagementPlatformFiscalIdentityDto).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "ExitPass.CentralPms.Contracts.ManagementPlatform" &&
                (type.Name.Contains("SalesInvoiceHeaderProfile", StringComparison.OrdinalIgnoreCase) ||
                 type.Name.Contains("FiscalIdentityDto", StringComparison.OrdinalIgnoreCase) ||
                 type.Name.Contains("PosServerSalesInvoiceProfile", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        contractTypes.Should().NotBeEmpty();
        contractTypes.SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Should()
            .NotContain(name => name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PosServerSalesInvoiceProfile_ClientShape_HasNoUiFiscalExitGateOrRepositoryDependency()
    {
        var constructorParameters = typeof(HttpPosServerSalesInvoiceProfileAdminClient)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().Equal("HttpClient", "PosServerSalesInvoiceProfileAdministrationOptions");
        constructorParameters.Should().NotContain(name => name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
        constructorParameters.Should().NotContain(name => name.Contains("Gate", StringComparison.OrdinalIgnoreCase));
        constructorParameters.Should().NotContain(name => name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
        constructorParameters.Should().NotContain(name => name.Contains("FiscalIssuance", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpPosServerSalesInvoiceProfileAdminClient CreateClient(
        RecordingHandler handler,
        PosServerSalesInvoiceProfileAdministrationOptions? options = null)
    {
        var httpClient = new HttpClient(handler);
        return new HttpPosServerSalesInvoiceProfileAdminClient(
            httpClient,
            options ?? new PosServerSalesInvoiceProfileAdministrationOptions
            {
                Enabled = true,
                BaseUrl = "https://pos-server-admin.test",
                ApiKey = ApiKey,
                TimeoutSeconds = 5
            });
    }

    private static ManagementPlatformPosServerAdminRequestContext Context() => new(CorrelationId);

    private static ManagementPlatformFiscalIdentityMutationRequest FiscalIdentityMutation() =>
        new(
            "ExitPass Test Corporation",
            "Redacted registered address",
            "123-456-789-000",
            "VAT_REGISTERED",
            "admin:user");

    private static ManagementPlatformSalesInvoiceHeaderProfileMutationRequest ProfileMutation() =>
        new(
            FiscalIdentityId,
            SiteId,
            SitePosServerId,
            1,
            "template-v1",
            "presentation-v1",
            "POS-123456",
            "MIN-123456",
            "ExitPass Test Parking",
            "BIR-ACC-PLACEHOLDER",
            new DateOnly(2026, 1, 5),
            new DateOnly(2027, 1, 5),
            "PTU-PLACEHOLDER",
            new DateOnly(2026, 1, 7),
            "This serves as your sales invoice.",
            "Customer support placeholder.",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            null,
            "admin:user");

    private static object FiscalIdentityPayload(string? updatedByRef = null) => new
    {
        fiscalIdentityId = FiscalIdentityId,
        registeredBusinessName = "ExitPass Test Corporation",
        registeredBusinessAddress = "Redacted registered address",
        tin = "123-456-789-000",
        taxpayerPosture = "VAT_REGISTERED",
        lifecycleStatus = "ACTIVE",
        createdAt = "2026-07-18T01:00:00Z",
        updatedAt = "2026-07-18T02:00:00Z",
        createdByRef = "admin:create",
        updatedByRef
    };

    private static object ProfilePayload(string lifecycleState = "DRAFT") => new
    {
        salesInvoiceHeaderProfileId = ProfileId,
        fiscalIdentityId = FiscalIdentityId,
        siteId = SiteId,
        sitePosServerId = SitePosServerId,
        profileVersion = 1,
        templateVersion = "template-v1",
        presentationVersion = "presentation-v1",
        posSerialNumber = "POS-123456",
        machineIdentificationNumber = "MIN-123456",
        parkingLocationDisplay = "ExitPass Test Parking",
        birAccreditationNumber = "BIR-ACC-PLACEHOLDER",
        birAccreditationIssuedDate = "2026-01-05",
        birAccreditationValidUntil = "2027-01-05",
        ptuNumber = "PTU-PLACEHOLDER",
        ptuIssuedDate = "2026-01-07",
        salesInvoiceLegalStatement = "This serves as your sales invoice.",
        customerServiceFooter = "Customer support placeholder.",
        effectiveFrom = "2026-07-18T00:00:00Z",
        effectiveTo = (string?)null,
        lifecycleState,
        approvedAt = lifecycleState == "APPROVED" ? "2026-07-18T03:00:00Z" : null,
        approvedByRef = lifecycleState == "APPROVED" ? "admin:approve" : null,
        retiredAt = lifecycleState == "RETIRED" ? "2026-07-18T04:00:00Z" : null,
        createdAt = "2026-07-18T01:00:00Z",
        updatedAt = "2026-07-18T02:00:00Z"
    };

    private static object ValidationPayload(IReadOnlyList<string> missingCodes) => new
    {
        salesInvoiceHeaderProfileId = ProfileId,
        lifecycleState = "DRAFT",
        isComplete = false,
        missingOrInvalidFieldCodes = missingCodes,
        validationMessages = new[] { "Safe validation message." },
        templateVersionPosture = "SUPPORTED",
        presentationVersionPosture = "SUPPORTED",
        effectiveWindowPosture = "CURRENT",
        overlapPosture = "NO_OVERLAP",
        fiscalIdentityPosture = "VALID",
        validatedAt = "2026-07-18T05:00:00Z",
        correlationId = ResponseCorrelationId
    };

    private static object ReadinessPayload(string status) => new
    {
        siteId = SiteId,
        sitePosServerId = SitePosServerId,
        effectiveAt = "2026-07-18T09:00:00Z",
        resolutionStatus = status,
        effectiveProfileId = ProfileId,
        profileVersion = 1,
        fiscalIdentityId = FiscalIdentityId,
        lifecycleState = "APPROVED",
        isComplete = true,
        enforcementRequired = true,
        missingOrInvalidFieldCodes = Array.Empty<string>(),
        birAccreditationValidityPosture = "VALID",
        ptuCompletenessPosture = "COMPLETE",
        supportedVersionPosture = "SUPPORTED",
        overlapOrAmbiguityPosture = "CLEAR",
        lastUpdatedAt = "2026-07-18T06:00:00Z",
        correlationId = ResponseCorrelationId
    };

    private static object UsagePayload() => new
    {
        salesInvoiceHeaderProfileId = ProfileId,
        profileVersion = 1,
        fiscalIdentityId = FiscalIdentityId,
        firstSnapshotAt = "2026-07-18T07:00:00Z",
        latestSnapshotAt = "2026-07-18T08:00:00Z",
        fiscalDocumentCount = 2,
        safeFiscalDocumentIdentifiers = new[] { "SI-000001", "SI-000002" },
        destructiveMutationBlocked = true,
        correlationId = ResponseCorrelationId
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpResponseMessage response) =>
            _responses.Enqueue((_, _) => Task.FromResult(response));

        public void Enqueue(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) =>
            _responses.Enqueue((request, cancellationToken) => Task.FromResult(responseFactory(request, cancellationToken)));

        public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
            _responses.Enqueue(responseFactory);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase),
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_responses.Count == 0)
            {
                return JsonResponse(HttpStatusCode.OK, new { data = FiscalIdentityPayload() });
            }

            var factory = _responses.Dequeue();
            return await factory(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? Body);
}
