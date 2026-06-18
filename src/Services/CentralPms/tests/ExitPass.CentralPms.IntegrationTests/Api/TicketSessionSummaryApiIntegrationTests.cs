using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Operations;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Operations;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the ticket session summary API route and deterministic response mapping.
/// </summary>
public sealed class TicketSessionSummaryApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/ticket-session-summary";
    private static readonly Guid CorrelationId = Guid.Parse("27510000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("27510000-0000-0000-0000-000000000002");

    /// <summary>
    /// Verifies the route is registered.
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
    /// Verifies Swagger exposes the route.
    /// </summary>
    [Fact]
    public async Task EndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/ticket-session-summary");
        swaggerJson.Should().Contain("GetTicketSessionSummary");
    }

    /// <summary>
    /// Verifies successful summary mapping.
    /// </summary>
    [Fact]
    public async Task Summary_WhenResolved_ReturnsTicketSessionSummary()
    {
        using var factory = CreateFactory(Resolved());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var correlationHeaders).Should().BeTrue();
        correlationHeaders!.Should().Contain(CorrelationId.ToString());

        var body = await response.Content.ReadFromJsonAsync<TicketSessionSummaryResponse>();
        body.Should().NotBeNull();
        body!.TicketNumber.Should().Be("TICKET-275");
        body.PlateLicense.Should().Be("Unknown");
        body.ParkingSessionId.Should().Be(ParkingSessionId);
        body.FeeMinorUnits.Should().Be(12550);
        body.PaymentStatus.Should().Be("Paid");
        body.VendorSystemCode.Should().Be("FAKE_PMS");
        body.VendorConfirmationCode.Should().Be("VENDOR_CONFIRMATION_STATUS_UNAVAILABLE");
        body.VendorMessage.Should().Be("Vendor session and tariff summary resolved.");
        body.VendorConfirmationStatus.Should().BeNull();
        body.VendorConfirmationTimestamp.Should().BeNull();
        body.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE" &&
            diagnostic.VendorSystemCode == "FAKE_PMS" &&
            diagnostic.VendorConfirmationCode == "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE" &&
            diagnostic.CorrelationId == CorrelationId);
    }

    /// <summary>
    /// Verifies invalid requests map to 400.
    /// </summary>
    [Fact]
    public async Task Summary_WhenInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(Failed(TicketSessionSummaryOutcome.InvalidRequest, "INVALID_TICKET_SESSION_SUMMARY_REQUEST"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_TICKET_SESSION_SUMMARY_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    /// <summary>
    /// Verifies deterministic not-found mapping.
    /// </summary>
    [Fact]
    public async Task Summary_WhenNotFound_ReturnsNotFound()
    {
        using var factory = CreateFactory(Failed(TicketSessionSummaryOutcome.NotFound, "TICKET_SESSION_NOT_FOUND"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies ambiguous tickets map to 409.
    /// </summary>
    [Fact]
    public async Task Summary_WhenAmbiguous_ReturnsConflict()
    {
        using var factory = CreateFactory(Failed(TicketSessionSummaryOutcome.Ambiguous, "AMBIGUOUS_TICKET_SESSION"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies vendor errors map to 502.
    /// </summary>
    [Fact]
    public async Task Summary_WhenVendorError_ReturnsBadGateway()
    {
        using var factory = CreateFactory(Failed(TicketSessionSummaryOutcome.VendorError, "VENDOR_TARIFF_CALCULATION_FAILED"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    /// <summary>
    /// Verifies adapter unavailable maps to 503 and retryable.
    /// </summary>
    [Fact]
    public async Task Summary_WhenAdapterUnavailable_ReturnsServiceUnavailable()
    {
        using var factory = CreateFactory(Failed(TicketSessionSummaryOutcome.AdapterUnavailable, "VENDOR_ADAPTER_UNAVAILABLE", retryable: true));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Retryable.Should().BeTrue();
    }

    private static CustomWebApplicationFactory CreateFactory(TicketSessionSummaryResult result) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ITicketSessionSummaryService>();
                services.AddSingleton<ITicketSessionSummaryService>(new FakeTicketSessionSummaryService(result));
            });

    private static TicketSessionSummaryRequest Request() =>
        new()
        {
            TicketNumber = "TICKET-275",
            SiteId = Guid.Parse("27510000-0000-0000-0000-000000000004"),
            SiteGroupId = Guid.Parse("27510000-0000-0000-0000-000000000005"),
            CorrelationId = CorrelationId
        };

    private static TicketSessionSummaryResult Resolved() =>
        TicketSessionSummaryResult.Resolved(
            new TicketSessionSummaryReadModel(
                "TICKET-275",
                CardNum: null,
                "Unknown",
                new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero),
                ParkingDurationSeconds: 3600,
                FeeMinorUnits: 12550,
                CurrencyCode: "PHP",
                FeeRuleType: null,
                FeeRuleIndexCode: "RULE-001",
                FeeRuleName: "Standard parking",
                VendorSessionStatus: "PAYMENT_REQUIRED",
                VendorSystemCode: "FAKE_PMS",
                VendorConfirmationCode: "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
                VendorMessage: "Vendor session and tariff summary resolved.",
                ParkingSessionId,
                PaymentAttemptId: Guid.Parse("27510000-0000-0000-0000-000000000003"),
                PaymentAttemptStatus: "FINALIZED",
                PaymentStatus: "Paid",
                PaymentConfirmationStatus: "RECORDED",
                VendorConfirmationStatus: null,
                VendorConfirmationTimestamp: null),
            [
                new TicketSessionSummaryDiagnostic(
                    "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
                    "Unavailable",
                    "central-pms-read-model",
                    Retryable: false,
                    VendorSystemCode: "FAKE_PMS",
                    VendorConfirmationCode: "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
                    VendorMessage: "Unavailable",
                    CorrelationId: CorrelationId)
            ],
            CorrelationId);

    private static TicketSessionSummaryResult Failed(
        TicketSessionSummaryOutcome outcome,
        string errorCode,
        bool retryable = false) =>
        TicketSessionSummaryResult.Failed(
            outcome,
            errorCode,
            retryable,
            [new TicketSessionSummaryDiagnostic(errorCode, "Diagnostic", "test", retryable, VendorConfirmationCode: errorCode, VendorMessage: "Diagnostic", CorrelationId: CorrelationId)],
            CorrelationId);

    private sealed class FakeTicketSessionSummaryService : ITicketSessionSummaryService
    {
        private readonly TicketSessionSummaryResult _result;

        public FakeTicketSessionSummaryService(TicketSessionSummaryResult result)
        {
            _result = result;
        }

        public Task<TicketSessionSummaryResult> GetAsync(
            TicketSessionSummaryCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
