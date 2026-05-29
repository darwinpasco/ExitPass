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
/// Verifies the Operator Console session lookup API route and response mapping.
/// </summary>
public sealed class OperatorConsoleSessionLookupApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/sessions/lookup";
    private static readonly Guid EvaluationId = Guid.Parse("46000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("46000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("46000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("46000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("46000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("46000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("46000000-0000-0000-0000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("46000000-0000-0000-0000-000000000008");

    /// <summary>
    /// Verifies the documented Operator Console session lookup route exists.
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

        swaggerJson.Should().Contain("/v1/ops/operator-console/sessions/lookup");
        swaggerJson.Should().Contain("LookupOperatorConsoleSession");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies denied access returns a deterministic 200 response without session details.
    /// </summary>
    [Fact]
    public async Task Lookup_WhenAccessDenied_ReturnsDeniedEnvelopeWithoutSessionDetails()
    {
        using var factory = CreateFactory(DeniedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleSessionLookupResponse>();
        body.Should().NotBeNull();
        body!.AccessEvaluationId.Should().Be(EvaluationId);
        body.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        body.AccessPersisted.Should().BeTrue();
        body.SessionFound.Should().BeFalse();
        body.ParkingSessionId.Should().BeNull();
        body.TicketReference.Should().BeNull();
        body.PlateNumber.Should().BeNull();
    }

    /// <summary>
    /// Verifies an allowed lookup returns session context.
    /// </summary>
    [Fact]
    public async Task Lookup_WhenAccessAllowedAndSessionFound_ReturnsSessionContext()
    {
        using var factory = CreateFactory(AllowedFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleSessionLookupResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.AccessEvaluationId.Should().Be(EvaluationId);
        body.SessionFound.Should().BeTrue();
        body.SessionEligible.Should().BeTrue();
        body.ParkingSessionId.Should().Be(ParkingSessionId);
        body.TicketReference.Should().Be("TICKET-001");
        body.CurrentPayableAmountMinorUnits.Should().Be(12500);
        body.CurrencyCode.Should().Be("PHP");
    }

    /// <summary>
    /// Verifies allowed access with no matching session maps to 404.
    /// </summary>
    [Fact]
    public async Task Lookup_WhenAccessAllowedAndSessionMissing_ReturnsNotFoundEnvelope()
    {
        using var factory = CreateFactory(NotFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleSessionLookupResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.SessionFound.Should().BeFalse();
        body.IneligibilityReason.Should().Be("SESSION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies validation errors map to Central PMS error envelopes.
    /// </summary>
    [Fact]
    public async Task Lookup_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(AllowedFoundResult(), throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_SESSION_LOOKUP_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleSessionLookupResult result,
        bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleSessionLookupService>();
                services.AddSingleton<IOperatorConsoleSessionLookupService>(
                    new FakeSessionLookupService(result, throwValidation));
            });

    private static OperatorConsoleSessionLookupRequest Request() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            "PARKING_SESSION_ID",
            "operator-console-session-lookup-api-test",
            CorrelationId);

    private static OperatorConsoleSessionLookupResult DeniedResult() =>
        new(
            EvaluationId,
            AccessAllowed: false,
            "DENIED",
            ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            Session: null,
            SessionEligible: false,
            IneligibilityReason: "ACCESS_DENIED",
            Alerts: Array.Empty<string>(),
            CorrelationId);

    private static OperatorConsoleSessionLookupResult AllowedFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            new OperatorConsoleSessionReadModel(
                ParkingSessionId,
                "TICKET-001",
                "ABC-1234",
                SiteId,
                SiteGroupId,
                "ACTIVE",
                DateTimeOffset.Parse("2026-05-29T04:00:00Z"),
                CurrentPayableAmountMinorUnits: 12500,
                CurrencyCode: "PHP",
                PaymentStatus: null,
                DiscountStatus: "NOT_APPLIED",
                ExitAuthorizationStatus: null),
            SessionEligible: true,
            IneligibilityReason: null,
            Alerts: Array.Empty<string>(),
            CorrelationId);

    private static OperatorConsoleSessionLookupResult NotFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            Session: null,
            SessionEligible: false,
            IneligibilityReason: "SESSION_NOT_FOUND",
            Alerts: Array.Empty<string>(),
            CorrelationId);

    private sealed class FakeSessionLookupService : IOperatorConsoleSessionLookupService
    {
        private readonly OperatorConsoleSessionLookupResult _result;
        private readonly bool _throwValidation;

        public FakeSessionLookupService(OperatorConsoleSessionLookupResult result, bool throwValidation)
        {
            _result = result;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleSessionLookupResult> LookupAsync(
            OperatorConsoleSessionLookupCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("Either ParkingSessionId or TicketReference is required.");
            }

            return Task.FromResult(_result);
        }
    }
}
