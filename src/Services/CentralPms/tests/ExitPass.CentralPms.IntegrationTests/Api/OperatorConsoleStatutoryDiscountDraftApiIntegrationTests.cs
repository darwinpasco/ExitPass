using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console statutory discount draft API route and response mapping.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private static readonly Guid EvaluationId = Guid.Parse("48000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("48000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("48000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("48000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("48000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("48000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("48000000-0000-0000-0000-000000000007");
    private static readonly Guid DraftId = Guid.Parse("48000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("48000000-0000-0000-0000-000000000009");

    /// <summary>
    /// Verifies the documented Operator Console statutory discount draft route exists.
    /// </summary>
    [Fact]
    public void EndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies the documented route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task EndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/draft");
        swaggerJson.Should().Contain("DraftOperatorConsoleStatutoryDiscount");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies denied access returns a deterministic 200 response without draft details.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccessDenied_ReturnsDeniedEnvelopeWithoutDraft()
    {
        using var factory = CreateFactory(DeniedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessEvaluationId.Should().Be(EvaluationId);
        body.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        body.AccessPersisted.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.DraftId.Should().BeNull();
    }

    /// <summary>
    /// Verifies accepted drafts return persisted draft evidence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccepted_ReturnsDraftEnvelope()
    {
        using var factory = CreateFactory(AcceptedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeTrue();
        body.DraftPersisted.Should().BeTrue();
        body.DraftId.Should().Be(DraftId);
        body.ValidationStatus.Should().Be("REQUESTED");
        body.EntitlementType.Should().Be("SENIOR_CITIZEN");
    }

    /// <summary>
    /// Verifies session-not-found maps to 404 without draft persistence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenSessionMissing_ReturnsNotFoundEnvelope()
    {
        using var factory = CreateFactory(NotFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("SESSION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies validation errors map to Central PMS error envelopes.
    /// </summary>
    [Fact]
    public async Task Draft_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(AcceptedResult(), throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleStatutoryDiscountDraftResult result,
        bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountDraftService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountDraftService>(
                    new FakeStatutoryDiscountDraftService(result, throwValidation));
            });

    private static OperatorConsoleStatutoryDiscountDraftRequest Request() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            "****1234",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Manual operator attestation.",
            ReasonCode: "OPERATOR_DRAFT_REQUESTED",
            "operator-console-statutory-discount-draft-api-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult DeniedResult() =>
        new(
            EvaluationId,
            AccessAllowed: false,
            "DENIED",
            ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult AcceptedResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: true,
            DraftPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            EvidenceCaptureRequired: true,
            IneligibilityReason: null,
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult NotFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            IneligibilityReason: "SESSION_NOT_FOUND",
            ErrorCode: "SESSION_NOT_FOUND",
            CorrelationId);

    private sealed class FakeStatutoryDiscountDraftService : IOperatorConsoleStatutoryDiscountDraftService
    {
        private readonly OperatorConsoleStatutoryDiscountDraftResult _result;
        private readonly bool _throwValidation;

        public FakeStatutoryDiscountDraftService(
            OperatorConsoleStatutoryDiscountDraftResult result,
            bool throwValidation)
        {
            _result = result;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
            OperatorConsoleStatutoryDiscountDraftCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("EntitlementType is required.");
            }

            return Task.FromResult(_result);
        }
    }
}
