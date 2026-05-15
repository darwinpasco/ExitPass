using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.VendorParking;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// API integration tests for provider-neutral Central PMS vendor parking resolution.
/// </summary>
public sealed class VendorParkingResolutionApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VendorParkingResolutionApiIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">In-memory Central PMS API factory.</param>
    public VendorParkingResolutionApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies plate-based vendor parking resolution returns Central PMS session and tariff identifiers.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenPlateProvided_ReturnsResolvedSessionAndTariff()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "ABC1234", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        payload.Should().NotBeNull();
        payload!.ParkingSessionId.Should().NotBe(Guid.Empty);
        payload.TariffSnapshotId.Should().NotBe(Guid.Empty);
        payload.LookupOutcome.Should().Be("resolved");
        payload.PlateNumber.Should().Be("ABC1234");
        payload.NetPayableMinorUnits.Should().Be(10000);
        payload.Currency.Should().Be("PHP");
        payload.VendorSystemId.Should().Be("FAKE-PMS");
        payload.CorrelationId.Should().Be(correlationId);
    }

    /// <summary>
    /// Verifies ticket-based vendor parking resolution returns Central PMS session and tariff identifiers.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenTicketProvided_ReturnsResolvedSessionAndTariff()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000002");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: null, ticketReference: "TICKET-001", correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        payload.Should().NotBeNull();
        payload!.ParkingSessionId.Should().NotBe(Guid.Empty);
        payload.TariffSnapshotId.Should().NotBe(Guid.Empty);
        payload.TicketReference.Should().Be("TICKET-001");
        payload.NetPayableMinorUnits.Should().Be(10000);
    }

    /// <summary>
    /// Verifies missing lookup identifiers are rejected at the API boundary.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenPlateAndTicketMissing_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000003");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: null, ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("INVALID_REQUEST");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies deterministic not-found envelope mapping.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorReturnsNotFound_ReturnsNotFoundEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000004");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "NOTFOUND", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("SESSION_NOT_FOUND");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies retryable vendor unavailability maps to HTTP 503.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorUnavailable_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000005");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "UNAVAILABLE", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_UNAVAILABLE");
        payload.Retryable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies malformed vendor data maps to HTTP 502.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorMalformed_ReturnsBadGateway()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000006");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "MALFORMED", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("MALFORMED_VENDOR_SESSION");
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies vendor business rejection maps to HTTP 409 with the standard envelope.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorRejected_ReturnsConflictEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000007");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "REJECTED", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_REJECTED_LOOKUP");
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies repeated fake-adapter resolution keeps provider-neutral tariff data stable.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenRepeated_ReturnsIdempotentOrStableSessionTariffResult()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000008");
        var request = Request(plateNumber: "ABC1234", ticketReference: null, correlationId);

        using var first = await client.PostAsJsonAsync("/v1/vendor-parking/resolve", request);
        using var second = await client.PostAsJsonAsync("/v1/vendor-parking/resolve", request);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPayload = await first.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        var secondPayload = await second.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();

        firstPayload.Should().NotBeNull();
        secondPayload.Should().NotBeNull();
        secondPayload!.LookupOutcome.Should().Be(firstPayload!.LookupOutcome);
        secondPayload.NetPayableMinorUnits.Should().Be(firstPayload.NetPayableMinorUnits);
        secondPayload.Currency.Should().Be(firstPayload.Currency);
        secondPayload.VendorSystemId.Should().Be(firstPayload.VendorSystemId);
        secondPayload.ParkingSessionId.Should().NotBe(Guid.Empty);
        secondPayload.TariffSnapshotId.Should().NotBe(Guid.Empty);
    }

    private static ResolveVendorParkingRequest Request(
        string? plateNumber,
        string? ticketReference,
        Guid correlationId)
    {
        return new ResolveVendorParkingRequest
        {
            SiteGroupId = "SG-TEST-001",
            SiteId = "SITE-TEST-001",
            VendorSystemId = "FAKE-PMS",
            PlateNumber = plateNumber,
            TicketReference = ticketReference,
            CorrelationId = correlationId
        };
    }
}
