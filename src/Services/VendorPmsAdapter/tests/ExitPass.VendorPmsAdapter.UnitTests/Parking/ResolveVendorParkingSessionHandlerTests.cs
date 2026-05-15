using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.Parking;

/// <summary>
/// Unit tests for <see cref="ResolveVendorParkingSessionHandler"/>.
/// </summary>
public sealed class ResolveVendorParkingSessionHandlerTests
{
    /// <summary>
    /// Verifies that the session resolver delegates valid requests to the vendor client.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenRequestIsValid_DelegatesToVendorClient()
    {
        var correlationId = Guid.NewGuid();
        var client = new FakeVendorParkingDataClient(
            new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.NotFound,
                null,
                "VENDOR_SESSION_NOT_FOUND",
                false,
                correlationId));
        var handler = new ResolveVendorParkingSessionHandler(client);

        var result = await handler.ExecuteAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, correlationId),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.NotFound, result.Status);
        Assert.Equal(correlationId, client.LastSessionRequest?.CorrelationId);
        Assert.Equal("ABC123", client.LastSessionRequest?.PlateNumber);
    }

    /// <summary>
    /// Verifies that the session resolver rejects requests without a lookup key.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenLookupKeyMissing_ThrowsArgumentException()
    {
        var handler = new ResolveVendorParkingSessionHandler(new FakeVendorParkingDataClient());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ExecuteAsync(
                new VendorParkingSessionLookupRequest(null, null, Guid.NewGuid()),
                CancellationToken.None));
    }

    private sealed class FakeVendorParkingDataClient : IVendorParkingDataClient
    {
        private readonly VendorParkingSessionLookupResponse _sessionResponse;

        public FakeVendorParkingDataClient()
            : this(new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.NotFound,
                null,
                "VENDOR_SESSION_NOT_FOUND",
                false,
                Guid.NewGuid()))
        {
        }

        public FakeVendorParkingDataClient(VendorParkingSessionLookupResponse sessionResponse)
        {
            _sessionResponse = sessionResponse;
        }

        public VendorParkingSessionLookupRequest? LastSessionRequest { get; private set; }

        public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
            VendorParkingSessionLookupRequest request,
            CancellationToken cancellationToken)
        {
            LastSessionRequest = request;
            return Task.FromResult(_sessionResponse);
        }

        public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
            VendorTariffQuoteRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
