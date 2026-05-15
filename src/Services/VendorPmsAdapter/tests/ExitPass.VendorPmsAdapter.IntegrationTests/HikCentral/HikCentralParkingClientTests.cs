using System.Net;
using System.Text;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;

namespace ExitPass.VendorPmsAdapter.IntegrationTests.HikCentral;

/// <summary>
/// Integration-style tests for <see cref="HikCentralParkingClient"/> using a fake HTTP server handler.
/// </summary>
public sealed class HikCentralParkingClientTests
{
    /// <summary>
    /// Verifies that an active HikCentral fee response maps into a provider-neutral session.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsActiveSession_ReturnsProviderNeutralSession()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "parkingInTime": "2026-05-15T09:00:00+08:00",
                "parkingDuration": 3600,
                "feeRuleType": 1,
                "feeRuleIndexCode": "RULE-1",
                "feeRuleName": "Standard Parking",
                "fee": "125.00"
              }
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal("HIKCENTRAL", result.Session?.VendorProviderCode);
        Assert.Equal("ABC123", result.Session?.PlateNumber);
        Assert.Equal("ACTIVE", result.Session?.Status);
        Assert.Equal(12500, result.Session?.TariffQuote?.AmountMinor);
        Assert.Equal("RULE-1", result.Session?.TariffQuote?.TariffVersionReference);
    }

    /// <summary>
    /// Verifies that HikCentral not-found responses map deterministically.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsNotFound_ReturnsNotFound()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "404", "msg": "vehicle not found", "data": null }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("MISSING", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.NotFound, result.Status);
        Assert.Equal("VENDOR_SESSION_NOT_FOUND", result.ErrorCode);
        Assert.False(result.Retryable);
    }

    /// <summary>
    /// Verifies that timeout-like transport failures map to retryable unavailable.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralTimesOut_ReturnsUnavailableRetryable()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => throw new HttpRequestException("timeout")));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.UnavailableRetryable, result.Status);
        Assert.Equal("VENDOR_PMS_UNAVAILABLE", result.ErrorCode);
        Assert.True(result.Retryable);
    }

    /// <summary>
    /// Verifies that malformed HikCentral payloads map to adapter error behavior.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsMalformedPayload_ReturnsAdapterError()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("{ this-is-not-json")));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR", result.ErrorCode);
        Assert.False(result.Retryable);
    }

    /// <summary>
    /// Verifies that a HikCentral fee response maps into a provider-neutral tariff quote.
    /// </summary>
    [Fact]
    public async Task ResolveTariff_WhenHikCentralReturnsFeeQuote_ReturnsProviderNeutralTariffQuote()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "parkingInTime": "2026-05-15T09:00:00+08:00",
                "parkingDuration": 3600,
                "feeRuleIndexCode": "RULE-2",
                "feeRuleName": "Weekend Parking",
                "fee": "80.50"
              }
            }
            """)));

        var result = await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal(8050, result.Quote?.AmountMinor);
        Assert.Equal("PHP", result.Quote?.Currency);
        Assert.Equal("RULE-2", result.Quote?.TariffVersionReference);
    }

    /// <summary>
    /// Verifies that tariff lookup shares deterministic session-not-found behavior.
    /// </summary>
    [Fact]
    public async Task ResolveTariff_WhenSessionNotFound_ReturnsNotFound()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest("MISSING", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.NotFound, result.Status);
        Assert.Equal("VENDOR_SESSION_NOT_FOUND", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that the client propagates the correlation identifier to HikCentral requests.
    /// </summary>
    [Fact]
    public async Task HikCentralClient_SendsCorrelationId_WhenProvided()
    {
        var correlationId = Guid.Parse("33333333-4444-5555-6666-777777777777");
        var handler = new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "parkingInTime": "2026-05-15T09:00:00+08:00",
                "parkingDuration": 1,
                "feeRuleIndexCode": "RULE-1",
                "feeRuleName": "Standard",
                "fee": "1.00"
              }
            }
            """));
        var client = CreateClient(handler);

        await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, correlationId),
            CancellationToken.None);

        Assert.Equal(correlationId.ToString(), handler.LastRequest?.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("/artemis/api/vehicle/v1/parkingfee/calculate", handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    private static HikCentralParkingClient CreateClient(HttpMessageHandler handler)
    {
        return new HikCentralParkingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hikcentral.fake")
        });
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHikCentralHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHikCentralHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
