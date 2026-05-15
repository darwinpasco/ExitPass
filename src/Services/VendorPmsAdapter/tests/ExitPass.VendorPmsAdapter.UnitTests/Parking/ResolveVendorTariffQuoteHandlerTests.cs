using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.Parking;

/// <summary>
/// Unit tests for <see cref="ResolveVendorTariffQuoteHandler"/>.
/// </summary>
public sealed class ResolveVendorTariffQuoteHandlerTests
{
    /// <summary>
    /// Verifies that the tariff resolver delegates valid requests to the vendor client.
    /// </summary>
    [Fact]
    public async Task ResolveTariff_WhenRequestIsValid_DelegatesToVendorClient()
    {
        var correlationId = Guid.NewGuid();
        var quote = new VendorTariffQuoteDto(12500, "PHP", "RULE-1", "Standard", DateTimeOffset.UtcNow);
        var client = new FakeVendorParkingDataClient(
            new VendorTariffQuoteResponse(
                VendorParkingLookupStatus.Found,
                quote,
                null,
                false,
                correlationId));
        var handler = new ResolveVendorTariffQuoteHandler(client);

        var result = await handler.ExecuteAsync(
            new VendorTariffQuoteRequest("ABC123", null, correlationId),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal(12500, result.Quote?.AmountMinor);
        Assert.Equal(correlationId, client.LastTariffRequest?.CorrelationId);
    }

    /// <summary>
    /// Verifies that tariff resolver rejects missing correlation identifiers.
    /// </summary>
    [Fact]
    public async Task ResolveTariff_WhenCorrelationIdMissing_ThrowsArgumentException()
    {
        var handler = new ResolveVendorTariffQuoteHandler(new FakeVendorParkingDataClient());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ExecuteAsync(
                new VendorTariffQuoteRequest("ABC123", null, Guid.Empty),
                CancellationToken.None));
    }

    private sealed class FakeVendorParkingDataClient : IVendorParkingDataClient
    {
        private readonly VendorTariffQuoteResponse _tariffResponse;

        public FakeVendorParkingDataClient()
            : this(new VendorTariffQuoteResponse(
                VendorParkingLookupStatus.NotFound,
                null,
                "VENDOR_SESSION_NOT_FOUND",
                false,
                Guid.NewGuid()))
        {
        }

        public FakeVendorParkingDataClient(VendorTariffQuoteResponse tariffResponse)
        {
            _tariffResponse = tariffResponse;
        }

        public VendorTariffQuoteRequest? LastTariffRequest { get; private set; }

        public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
            VendorParkingSessionLookupRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
            VendorTariffQuoteRequest request,
            CancellationToken cancellationToken)
        {
            LastTariffRequest = request;
            return Task.FromResult(_tariffResponse);
        }

        public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
            VendorParkingFeeConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
