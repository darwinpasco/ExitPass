using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class StatutoryDiscountDecisionApiAccessPolicyIntegrationTests
{
    private static readonly Guid CommandId = Guid.Parse("7d000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestReference = Guid.Parse("7d000000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("7d000000-0000-0000-0000-000000000003");
    private static readonly Guid CorrelationId = Guid.Parse("7d000000-0000-0000-0000-000000000004");
    private static readonly Guid ActorUserId = Guid.Parse("7d000000-0000-0000-0000-000000000005");
    private static readonly Guid ServiceIdentityId = Guid.Parse("7d000000-0000-0000-0000-000000000006");

    [Fact]
    public async Task Submit_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v1/statutory-discounts/decisions", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task Submit_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "reconciliation.evaluate");

        using var response = await PostDecisionAsync(client, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task Submit_WhenBodySourceChannelDoesNotMatchAuthenticatedChannel_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.decision.submit.webpay");

        using var response = await PostDecisionAsync(client, Request(sourceChannel: "ASSISTED_PAYMENT_TERMINAL"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH");
        body.ClientResultStatus.Should().Be("NON_RETRYABLE_FAILURE");
    }

    [Theory]
    [InlineData("OPERATOR_CONSOLE", "statutory-discounts.decision.submit.operator-console")]
    [InlineData("WEBPAY", "statutory-discounts.decision.submit.webpay")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal")]
    public async Task Submit_WhenSourceChannelPermissionMatches_Succeeds(string sourceChannel, string permission)
    {
        var fake = new FakeFacadeService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        if (sourceChannel == "OPERATOR_CONSOLE")
        {
            AddUserHeaders(client, permission);
        }
        else
        {
            AddServiceHeaders(client, permission);
        }

        using var response = await PostDecisionAsync(client, Request(sourceChannel: sourceChannel));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();
        body!.SourceChannel.Should().Be(sourceChannel);
        body.CommandStatus.Should().Be("COMPLETED");
        body.ClientResultStatus.Should().Be("APPROVED");
        body.RecoveryClassification.Should().Be("NONE");
        body.Retryable.Should().BeFalse();
        fake.LastCommand!.SourceChannel.Should().Be(sourceChannel);
        fake.LastCommand.ActorUserId.Should().Be(sourceChannel == "OPERATOR_CONSOLE" ? ActorUserId : ServiceIdentityId);
    }

    [Theory]
    [InlineData("WEBPAY", "statutory-discounts.decision.submit.webpay")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal")]
    public async Task Submit_WhenNonOperatorChannelSendsOperatorOnlyFields_ReturnsBadRequest(string sourceChannel, string permission)
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddServiceHeaders(client, permission);

        using var response = await PostDecisionAsync(client, Request(sourceChannel: sourceChannel, includeOperatorOnlyFields: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_CHANNEL_FIELD_PROHIBITED");
        body.ClientResultStatus.Should().Be("NON_RETRYABLE_FAILURE");
    }

    [Theory]
    [InlineData("WEBPAY", "statutory-discounts.decision.submit.webpay")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal")]
    public async Task Submit_WhenServiceChannelRequestsApplyWithoutOperatorOnlyFacts_ReachesFacade(string sourceChannel, string permission)
    {
        var fake = new FakeFacadeService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddServiceHeaders(client, permission);

        using var response = await PostDecisionAsync(client, Request(sourceChannel: sourceChannel, applyPayableBasis: true));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        fake.LastCommand!.SourceChannel.Should().Be(sourceChannel);
        fake.LastCommand.ApplyPayableBasis.Should().BeTrue();
        fake.LastCommand.Decision.Should().BeNull();
        fake.LastCommand.ReviewerUserId.Should().BeNull();
        fake.LastCommand.OperatorDeviceBindingId.Should().BeNull();
        fake.LastCommand.OperatorShiftId.Should().BeNull();
    }

    [Fact]
    public async Task Submit_WhenServiceChannelApplicationIntentDecisionMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(new FakeFacadeService
        {
            SubmitException = new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "An approved statutory-discount decision must exist before payable-basis application can be requested.",
                isNotFound: true)
        });
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.decision.submit.webpay");

        using var response = await PostDecisionAsync(client, Request(sourceChannel: "WEBPAY", applyPayableBasis: true));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_NOT_FOUND");
        body.ClientResultStatus.Should().Be(StatutoryDiscountDecisionClientResultStatuses.NotFound);
        body.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_WhenServiceChannelApplicationIntentDecisionRejected_ReturnsNonRetryableNotApproved()
    {
        using var factory = CreateFactory(new FakeFacadeService
        {
            SubmitException = new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED",
                "Payable-basis application requires an approved statutory-discount decision.")
        });
        using var client = factory.CreateClient();
        AddServiceHeaders(client, "statutory-discounts.decision.submit.webpay");

        using var response = await PostDecisionAsync(client, Request(sourceChannel: "WEBPAY", applyPayableBasis: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_NOT_APPROVED");
        body.ClientResultStatus.Should().Be(StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved);
        body.RecoveryClassification.Should().Be(StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable);
        body.RecoveryAction.Should().Be(StatutoryDiscountDecisionRecoveryActions.DoNotRetry);
        body.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_WhenAuthenticatedIdentityMapsToMultipleSourceChannels_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddServiceHeaders(
            client,
            "statutory-discounts.decision.submit.webpay statutory-discounts.decision.submit.assisted-payment-terminal");

        using var response = await PostDecisionAsync(client, Request(sourceChannel: "WEBPAY"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_SOURCE_CHANNEL_AMBIGUOUS");
    }

    [Fact]
    public async Task Submit_WhenActorDoesNotMatchAuthenticatedIdentity_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.submit.operator-console");

        using var response = await PostDecisionAsync(client, Request(actorUserId: Guid.Parse("7d000000-0000-0000-0000-000000000099")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_ACTOR_CONTEXT_MISMATCH");
    }

    [Fact]
    public async Task Read_WhenAuthorized_ReturnsSafeCanonicalResponseWithoutEvidencePayload()
    {
        var fake = new FakeFacadeService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.read");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());

        using var response = await client.GetAsync($"/v1/statutory-discounts/decisions/{CommandId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("storageReference");
        raw.Should().NotContain("raw");
        var body = await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();
        body!.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
    }

    [Fact]
    public void SharedDecisionContracts_DoNotExposeRawEvidenceOrFullStatutoryIdFields()
    {
        var propertyNames = typeof(StatutoryDiscountDecisionRequest)
            .GetProperties()
            .Concat(typeof(StatutoryDiscountDecisionResponse).GetProperties())
            .Select(property => property.Name);

        propertyNames.Should().NotContain(name => ContainsProhibitedIdentityOrEvidenceTerm(name));
    }

    [Fact]
    public async Task Read_WhenReferenceMissing_ReturnsStandardNotFound()
    {
        using var factory = CreateFactory(new FakeFacadeService { ReturnNullReadback = true });
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.read");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());

        using var response = await client.GetAsync($"/v1/statutory-discounts/decisions/{CommandId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_NOT_FOUND");
    }

    [Fact]
    public async Task Submit_WhenIdempotencyKeyMissing_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.submit.operator-console");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());

        using var response = await client.PostAsJsonAsync("/v1/statutory-discounts/decisions", Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Idempotency-Key");
    }

    [Fact]
    public async Task Submit_WhenSourceChannelMalformed_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.submit.operator-console");

        using var response = await PostDecisionAsync(client, Request(sourceChannel: "MOBILE_APP"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("UNSUPPORTED_SOURCE_CHANNEL");
    }

    [Fact]
    public async Task Submit_WhenEntitlementUnsupported_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.submit.operator-console");

        using var response = await PostDecisionAsync(client, Request(entitlementType: "SOLO_PARENT"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("UNSUPPORTED_ENTITLEMENT_TYPE");
    }

    [Fact]
    public async Task Submit_WhenUnsafeIdentifierIsSent_ReturnsBadRequestWithoutEchoingValue()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();
        AddUserHeaders(client, "statutory-discounts.decision.submit.operator-console");

        using var response = await PostDecisionAsync(client, Request(maskedIdReference: "SC-123456789"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("UNSAFE_IDENTIFIER_REJECTED");
        raw.Should().NotContain("123456789");
    }

    [Fact]
    public void Endpoints_ExposeExpectedRbacPolicyMetadata()
    {
        using var factory = CreateFactory(new FakeFacadeService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/statutory-discounts/decisions", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .BeEquivalentTo("CentralPmsStatutoryDiscountDecisionSubmit", "CentralPmsStatutoryDiscountDecisionRead");
    }

    private static CustomWebApplicationFactory CreateFactory(FakeFacadeService fake) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IStatutoryDiscountDecisionFacadeService>();
                services.AddSingleton<IStatutoryDiscountDecisionFacadeService>(fake);
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static async Task<HttpResponseMessage> PostDecisionAsync(
        HttpClient client,
        StatutoryDiscountDecisionRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/statutory-discounts/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", "statutory-decision-api-test-key");
        message.Headers.Add("X-Correlation-Id", CorrelationId.ToString());
        return await client.SendAsync(message);
    }

    private static void AddUserHeaders(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, ActorUserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permissions);
    }

    private static void AddServiceHeaders(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permissions);
    }

    private static StatutoryDiscountDecisionRequest Request(
        string sourceChannel = "OPERATOR_CONSOLE",
        string entitlementType = "SENIOR_CITIZEN",
        string maskedIdReference = "SC-****-1234",
        Guid? actorUserId = null,
        bool includeOperatorOnlyFields = false,
        bool? applyPayableBasis = null)
    {
        var isOperator = string.Equals(sourceChannel, "OPERATOR_CONSOLE", StringComparison.Ordinal);
        var includeOperatorFacts = isOperator || includeOperatorOnlyFields;
        var requestActorId = actorUserId ?? (isOperator ? ActorUserId : Guid.Empty);

        return new(
            RequestReference,
            sourceChannel,
            ParkingSessionId,
            SiteId: Guid.Parse("7d000000-0000-0000-0000-000000000007"),
            SiteGroupId: Guid.Parse("7d000000-0000-0000-0000-000000000008"),
            TicketReference: "TICKET-001",
            PlateNumber: "ABC1234",
            entitlementType,
            IdDocumentType: "SENIOR_CITIZEN_ID",
            IssuingAuthority: "OSCA",
            ExpiryDate: DateOnly.Parse("2030-01-01"),
            maskedIdReference,
            EvidenceCaptureRequested: true,
            EvidenceReferences:
            [
                new StatutoryDiscountEvidenceReferenceRequest(
                    EvidenceType: "SENIOR_CITIZEN_ID",
                    CaptureMethod: "MANUAL_REFERENCE",
                    FileName: null,
                    ContentType: null,
                    SizeBytes: null,
                    StorageReference: "evidence-ref-001",
                    ReferenceNumberMasked: "SC-****-1234",
                    VerificationStatus: "VERIFIED")
            ],
            requestActorId,
            OperatorDeviceBindingId: includeOperatorFacts ? Guid.Parse("7d000000-0000-0000-0000-000000000009") : null,
            OperatorShiftId: includeOperatorFacts ? Guid.Parse("7d000000-0000-0000-0000-00000000000a") : null,
            RequesterAttestation: true,
            AttestationNotes: "attested",
            ReasonCode: "CUSTOMER_REQUEST",
            Decision: includeOperatorFacts ? "APPROVE" : null,
            DecisionReasonCode: includeOperatorFacts ? "ELIGIBLE" : null,
            ReviewerUserId: includeOperatorFacts ? Guid.Parse("7d000000-0000-0000-0000-00000000000b") : null,
            ReviewerAttestation: includeOperatorFacts,
            ApplyPayableBasis: applyPayableBasis ?? includeOperatorFacts,
            OriginalTariffSnapshotId: includeOperatorFacts ? Guid.Parse("7d000000-0000-0000-0000-00000000000c") : null);
    }

    private static bool ContainsProhibitedIdentityOrEvidenceTerm(string propertyName)
    {
        string[] prohibitedTerms =
        [
            "Raw",
            "Bytes",
            "Base64",
            "Image",
            "Payload",
            "FullId",
            "FullStatutoryId",
            "UnmaskedId"
        ];

        return prohibitedTerms.Any(term => propertyName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeFacadeService : IStatutoryDiscountDecisionFacadeService
    {
        public StatutoryDiscountDecisionCommand? LastCommand { get; private set; }

        public bool ReturnNullReadback { get; init; }

        public StatutoryDiscountDecisionRejectedException? SubmitException { get; init; }

        public Task<StatutoryDiscountDecisionResult> SubmitAsync(
            StatutoryDiscountDecisionCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            if (SubmitException is not null)
            {
                throw SubmitException;
            }

            if (command.EntitlementType == "SOLO_PARENT")
            {
                throw new StatutoryDiscountDecisionRejectedException(
                    "UNSUPPORTED_ENTITLEMENT_TYPE",
                    "Only SENIOR_CITIZEN and PWD are supported in this slice.");
            }

            if (command.MaskedIdReference == "SC-123456789")
            {
                throw new StatutoryDiscountDecisionRejectedException(
                    "UNSAFE_IDENTIFIER_REJECTED",
                    "Full statutory ID numbers are not accepted by the shared contract.");
            }

            return Task.FromResult(Result(command.SourceChannel, command.RequestReference, command.CorrelationId));
        }

        public Task<StatutoryDiscountDecisionResult?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnNullReadback
                ? null
                : Result("OPERATOR_CONSOLE", RequestReference, correlationId));

        private static StatutoryDiscountDecisionResult Result(
            string sourceChannel,
            Guid requestReference,
            Guid correlationId) =>
            new(
                CommandId,
                requestReference,
                StatutoryDiscountValidationId: Guid.Parse("7d000000-0000-0000-0000-00000000000d"),
                ParkingSessionId,
                sourceChannel,
                EntitlementType: "SENIOR_CITIZEN",
                DecisionStatus: "APPLIED_PAYABLE_BASIS",
                PolicyResolutionBasis: "NATIONAL_LAW_FALLBACK",
                AppliedPolicyReferenceId: Guid.Parse("7d000000-0000-0000-0000-00000000000e"),
                FallbackPolicyReferenceId: null,
                LocalOrdinanceApplied: false,
                GrossAmountMinorUnits: 12500,
                StatutoryDiscountAmountMinorUnits: 2232,
                NetPayableAmountMinorUnits: 8929,
                Currency: "PHP",
                EvidenceRequired: true,
                EvidenceRecorded: true,
                ReasonCode: "ELIGIBLE",
                ErrorCode: null,
                correlationId,
                CreatedAt: DateTimeOffset.Parse("2026-07-21T08:00:00Z"),
                DecidedAt: DateTimeOffset.Parse("2026-07-21T08:01:00Z"),
                AppliedAt: DateTimeOffset.Parse("2026-07-21T08:02:00Z"),
                OriginalTariffSnapshotId: Guid.Parse("7d000000-0000-0000-0000-00000000000c"),
                AppliedTariffSnapshotId: Guid.Parse("7d000000-0000-0000-0000-00000000000f"),
                ResultClassification: "ACCEPTED",
                SemanticHashSourceVersion: StatutoryDiscountDecisionSemanticHash.SourceVersion);
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
            Task.FromResult(serviceIdentityId == ServiceIdentityId);

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
