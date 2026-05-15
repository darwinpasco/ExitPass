using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.Parking;

/// <summary>
/// Unit tests for <see cref="ConfirmVendorParkingFeeHandler"/>.
/// </summary>
public sealed class ConfirmVendorParkingFeeHandlerTests
{
    /// <summary>
    /// Verifies that the confirmation handler delegates valid requests to the vendor client.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenRequestIsValid_DelegatesToVendorClient()
    {
        var correlationId = Guid.NewGuid();
        var confirmation = new VendorParkingFeeConfirmationDto(
            20000,
            "PHP",
            DateTimeOffset.Parse("2022-04-12T14:48:11+08:00"));
        var client = new FakeVendorParkingDataClient(
            new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.Confirmed,
                confirmation,
                null,
                false,
                correlationId));
        var handler = new ConfirmVendorParkingFeeHandler(client);

        var result = await handler.ExecuteAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", correlationId),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Confirmed, result.Status);
        Assert.Equal(20000, result.Confirmation?.AmountMinor);
        Assert.Equal(correlationId, client.LastConfirmationRequest?.CorrelationId);
        Assert.Equal("2700H", client.LastConfirmationRequest?.PlateNumber);
    }

    /// <summary>
    /// Verifies that the confirmation handler rejects missing fee amounts.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenFeeMissing_ThrowsArgumentException()
    {
        var handler = new ConfirmVendorParkingFeeHandler(new FakeVendorParkingDataClient());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ExecuteAsync(
                new VendorParkingFeeConfirmationRequest("2700H", null, 1, null, "PHP", Guid.NewGuid()),
                CancellationToken.None));
    }

    private sealed class FakeVendorParkingDataClient : IVendorParkingDataClient
    {
        private readonly VendorParkingFeeConfirmationResponse _confirmationResponse;

        public FakeVendorParkingDataClient()
            : this(new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.VendorRejected,
                null,
                "VENDOR_PMS_REJECTED",
                false,
                Guid.NewGuid()))
        {
        }

        public FakeVendorParkingDataClient(VendorParkingFeeConfirmationResponse confirmationResponse)
        {
            _confirmationResponse = confirmationResponse;
        }

        public VendorParkingFeeConfirmationRequest? LastConfirmationRequest { get; private set; }

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
            throw new NotSupportedException();
        }

        public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
            VendorParkingFeeConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            LastConfirmationRequest = request;
            return Task.FromResult(_confirmationResponse);
        }
    }
}
