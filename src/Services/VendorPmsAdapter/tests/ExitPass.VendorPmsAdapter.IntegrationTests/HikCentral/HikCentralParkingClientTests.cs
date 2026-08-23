using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExitPass.VendorPmsAdapter.IntegrationTests.HikCentral;

/// <summary>
/// Integration-style tests for <see cref="HikCentralParkingClient"/> using a fake HTTP server handler.
/// </summary>
public sealed class HikCentralParkingClientTests
{
    /// <summary>
    /// Verifies that plate lookups send the official HikCentral V3.1.0 calculate request shape.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenPlateLicenseProvided_SendsOfficialV310Shape()
    {
        var handler = new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "10.00"));
        var client = CreateClient(handler);

        await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("ABC123", body.RootElement.GetProperty("plateLicense").GetString());
        Assert.False(body.RootElement.TryGetProperty("cardNum", out _));
        Assert.Equal("exitpass-adapter", handler.LastRequest?.Headers.GetValues("userId").Single());
        AssertSignedCalculateRequest(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that card lookups send the official HikCentral V3.1.0 calculate request shape.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenCardNumProvided_SendsOfficialV310Shape()
    {
        var handler = new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "10.00"));
        var client = CreateClient(handler);

        await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(null, "CARD-9", Guid.NewGuid()),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("CARD-9", body.RootElement.GetProperty("cardNum").GetString());
        Assert.False(body.RootElement.TryGetProperty("plateLicense", out _));
        Assert.Equal("exitpass-adapter", handler.LastRequest?.Headers.GetValues("userId").Single());
        AssertSignedCalculateRequest(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that missing plate and card values fail before the fake HikCentral server is called.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenNeitherPlateNorCardProvided_ReturnsValidationError()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler);

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(null, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.Equal("VENDOR_LOOKUP_VALIDATION_ERROR", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that plate values longer than the official V3.1.0 maximum fail validation.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenPlateLicenseTooLong_ReturnsValidationError()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler);

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(new string('A', 33), null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.Equal("VENDOR_LOOKUP_VALIDATION_ERROR", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that an active HikCentral fee response maps into a provider-neutral session.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsActiveSession_ReturnsProviderNeutralSession()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "125.00")));

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
    /// Verifies that ticket-only HikCentral calculate payloads do not require a real plate license.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenTicketOnlyCalculateReturnsUnknownPlate_ReturnsProviderNeutralSession()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "Unknown",
                "cardNum": "3519351207107",
                "parkingInTime": "2026-06-17T11:19:12+08:00",
                "parkingDuration": 3539,
                "feeRuleType": 0,
                "feeRuleIndexCode": "1",
                "feeRuleName": "test fee",
                "fee": "50.00"
              }
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(null, "3519351207107", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal("Unknown", result.Session?.PlateNumber);
        Assert.Contains("3519351207107", result.Session?.VendorSessionReference, StringComparison.Ordinal);
        Assert.Equal(5000, result.Session?.TariffQuote?.AmountMinor);
        Assert.Equal("1", result.Session?.TariffQuote?.TariffVersionReference);
    }

    /// <summary>
    /// Verifies that HikCentral cannot satisfy a ticket lookup with a different card number.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenReturnedCardDoesNotMatchRequestedTicket_ReturnsNotFound()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "cardNum": "CARD-A",
                "parkingInTime": "2026-06-17T11:19:12+08:00",
                "parkingDuration": 3539,
                "fee": "50.00"
              }
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(null, "UNKNOWN-CARD", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.NotFound, result.Status);
        Assert.Equal("VENDOR_SESSION_NOT_FOUND", result.ErrorCode);
        Assert.Null(result.Session);
    }

    /// <summary>
    /// Verifies that ticket-only calculate can still map when HikCentral omits plateLicense.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenTicketOnlyCalculateOmitsPlateLicense_ReturnsUnknownPlate()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "cardNum": "3519351207107",
                "parkingInTime": "2026-06-17T11:19:12+08:00",
                "parkingDuration": 3539,
                "fee": "50.00"
              }
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest(null, "3519351207107", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal("Unknown", result.Session?.PlateNumber);
        Assert.Equal(5000, result.Session?.TariffQuote?.AmountMinor);
    }

    /// <summary>
    /// Verifies that HikCentral array payloads with one candidate still map into one provider-neutral session.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsSingleArrayCandidate_ReturnsProviderNeutralSession()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": [
                {
                  "plateLicense": "ABC123",
                  "parkingInTime": "2026-05-15T09:00:00+08:00",
                  "parkingDuration": 3600,
                  "feeRuleType": 1,
                  "feeRuleIndexCode": "RULE-1",
                  "feeRuleName": "Standard Parking",
                  "fee": "125.00"
                }
              ]
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        Assert.Equal("HIKCENTRAL", result.Session?.VendorProviderCode);
        Assert.Equal("ABC123", result.Session?.PlateNumber);
        Assert.Equal(12500, result.Session?.TariffQuote?.AmountMinor);
    }

    /// <summary>
    /// Verifies that multiple HikCentral candidates are not guessed into one ExitPass session.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsMultipleCandidates_ReturnsAmbiguous()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": [
                {
                  "plateLicense": "ABC123",
                  "parkingInTime": "2026-05-15T09:00:00+08:00",
                  "parkingDuration": 3600,
                  "feeRuleType": 1,
                  "feeRuleIndexCode": "RULE-1",
                  "feeRuleName": "Standard Parking",
                  "fee": "125.00"
                },
                {
                  "plateLicense": "ABC123",
                  "parkingInTime": "2026-05-15T10:00:00+08:00",
                  "parkingDuration": 1800,
                  "feeRuleType": 1,
                  "feeRuleIndexCode": "RULE-2",
                  "feeRuleName": "Standard Parking",
                  "fee": "75.00"
                }
              ]
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Ambiguous, result.Status);
        Assert.Equal("VENDOR_SESSION_AMBIGUOUS", result.ErrorCode);
        Assert.Null(result.Session);
        Assert.False(result.Retryable);
    }

    /// <summary>
    /// Verifies that nonzero HikCentral response codes map to deterministic adapter diagnostics.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenCodeIsNonZero_ReturnsAdapterErrorWithHikCentralCode()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "128", "msg": "The request resource does not exist. [vehicle is not exist]", "data": {} }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_128", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that missing required parkingInTime maps to malformed payload behavior.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenParkingInTimeMissing_ReturnsMalformedPayload()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "parkingDuration": 3600,
                "fee": "125.00"
              }
            }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that nonnumeric official fee strings map to malformed payload behavior.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_WhenFeeIsNonNumeric_ReturnsMalformedPayload()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "not-a-number")));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that HikCentral nonzero not-found envelope codes map to adapter diagnostics.
    /// </summary>
    [Fact]
    public async Task ResolveSession_WhenHikCentralReturnsNonzeroNotFoundCode_ReturnsAdapterError()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "404", "msg": "vehicle not found", "data": null }
            """)));

        var result = await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("MISSING", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_404", result.ErrorCode);
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
        var handler = new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "1.00"));
        var client = CreateClient(handler);

        await client.ResolveSessionAsync(
            new VendorParkingSessionLookupRequest("ABC123", null, correlationId),
            CancellationToken.None);

        Assert.Equal(correlationId.ToString(), handler.LastRequest?.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("exitpass-adapter", handler.LastRequest?.Headers.GetValues("userId").Single());
        Assert.Equal("/artemis/api/vehicle/v1/parkingfee/calculate", handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    /// <summary>
    /// Verifies that calculate requests include the official HikCentral AK/SK signing headers.
    /// </summary>
    [Fact]
    public async Task HikCentralParkingClient_CalculateParkingFee_SendsAkSkHeaders()
    {
        var handler = new FakeHikCentralHandler(_ => SuccessfulFeeResponse("ABC123", "1.00"));
        var client = CreateClient(handler);

        await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest("ABC123", null, Guid.NewGuid()),
            CancellationToken.None);

        AssertSignedCalculateRequest(handler.LastRequest);
        Assert.DoesNotContain("test-secret", handler.LastRequestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that plate-based confirmations send the official HikCentral V3.1.0 body shape.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenPlateLicenseProvided_SendsOfficialV310Shape()
    {
        var handler = new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("200.00"));
        var client = CreateClient(handler);

        await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 0, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("2700H", body.RootElement.GetProperty("plateLicense").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("immediatelyLeave").GetInt32());
        Assert.Equal("200.00", body.RootElement.GetProperty("fee").GetString());
        Assert.False(body.RootElement.TryGetProperty("cardNum", out _));
        Assert.Equal("exitpass-adapter", handler.LastRequest?.Headers.GetValues("userId").Single());
        AssertSignedRequest(handler.LastRequest, "/artemis/api/vehicle/v1/parkingfee/confirm");
    }

    /// <summary>
    /// Verifies that confirmation fails closed unless explicitly enabled.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenConfirmPaymentDisabled_DoesNotCallHikCentral()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler, confirmPaymentEnabled: false);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest(null, "3519351207107", 0, 5000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_CONFIRMATION_DISABLED", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that card-based confirmations send the official HikCentral V3.1.0 body shape.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenCardNumProvided_SendsOfficialV310Shape()
    {
        var handler = new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("200.00"));
        var client = CreateClient(handler);

        await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest(null, "CARD-9", 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("CARD-9", body.RootElement.GetProperty("cardNum").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("immediatelyLeave").GetInt32());
        Assert.Equal("200.00", body.RootElement.GetProperty("fee").GetString());
        Assert.False(body.RootElement.TryGetProperty("plateLicense", out _));
        AssertSignedRequest(handler.LastRequest, "/artemis/api/vehicle/v1/parkingfee/confirm");
    }

    /// <summary>
    /// Verifies that missing plate and card values fail before HikCentral is called.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenMissingPlateAndCard_ReturnsValidationError()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest(null, null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.Equal("VENDOR_CONFIRMATION_VALIDATION_ERROR", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that missing fee values fail before HikCentral is called.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenFeeMissing_ReturnsValidationError()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, null, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.Equal("VENDOR_CONFIRMATION_VALIDATION_ERROR", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that invalid immediately-leave values fail before HikCentral is called.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenImmediatelyLeaveInvalid_ReturnsValidationError()
    {
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 2, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.ValidationError, result.Status);
        Assert.Equal("VENDOR_CONFIRMATION_VALIDATION_ERROR", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies that successful HikCentral confirmation responses map provider-neutral fee details.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenHikCentralReturnsSuccess_MapsFeeAndFeeTime()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("200.00")));

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Confirmed, result.Status);
        Assert.Equal(20000, result.Confirmation?.AmountMinor);
        Assert.Equal("PHP", result.Confirmation?.Currency);
        Assert.Equal(DateTimeOffset.Parse("2022-04-12T14:48:11+08:00"), result.Confirmation?.FeeTime);
    }

    /// <summary>
    /// Verifies that nonzero HikCentral confirm codes map to deterministic adapter diagnostics.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenCodeIsNonZero_ReturnsAdapterErrorWithHikCentralCode()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""{ "code": "128", "msg": "confirm failed", "data": {} }""")));

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_128", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that missing feeTime maps to malformed payload behavior.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenFeeTimeMissing_ReturnsMalformedPayload()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""{ "code": "0", "msg": "Success", "data": { "fee": "200.00" } }""")));

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that nonnumeric confirm fee values map to malformed payload behavior.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenFeeIsNonNumeric_ReturnsMalformedPayload()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("not-a-number")));

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR", result.ErrorCode);
    }

    /// <summary>
    /// Verifies that confirm requests propagate correlation IDs and do not leak the test secret.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_PropagatesCorrelationId_AndDoesNotLeakSecret()
    {
        var correlationId = Guid.Parse("44444444-5555-6666-7777-888888888888");
        var handler = new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("200.00"));
        var client = CreateClient(handler);

        await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", correlationId),
            CancellationToken.None);

        Assert.Equal(correlationId.ToString(), handler.LastRequest?.Headers.GetValues("X-Correlation-Id").Single());
        AssertSignedRequest(handler.LastRequest, "/artemis/api/vehicle/v1/parkingfee/confirm");
        Assert.DoesNotContain("test-secret", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "test-secret",
            string.Join("|", handler.LastRequest!.Headers.SelectMany(header => header.Value)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies calculate diagnostic logs include safe request and response metadata.
    /// </summary>
    [Fact]
    public async Task CalculateParkingFee_LogsStructuredDiagnostics()
    {
        var correlationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var logger = new CapturingLogger<HikCentralParkingClient>();
        var handler = new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "ABC123",
                "cardNum": "CARD-9",
                "parkingInTime": "2026-05-15T09:00:00+08:00",
                "parkingDuration": 3600,
                "feeRuleType": 1,
                "feeRuleIndexCode": "RULE-1",
                "feeRuleName": "Standard Parking",
                "fee": "10.00"
              }
            }
            """));
        var client = CreateClient(handler, logger: logger);

        var result = await client.ResolveTariffAsync(
            new VendorTariffQuoteRequest(null, "CARD-9", correlationId),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Found, result.Status);
        var responseLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(HikCentralParkingClient.CalculateOperationName, responseLog.Properties["OperationName"]);
        Assert.Equal(HikCentralParkingClient.OutcomeSuccess, responseLog.Properties["Outcome"]);
        Assert.Equal("/artemis/api/vehicle/v1/parkingfee/calculate", responseLog.Properties["EndpointPath"]);
        Assert.Equal("parkingfee.calculate", responseLog.Properties["EndpointName"]);
        Assert.Equal(correlationId, responseLog.Properties["CorrelationId"]);
        Assert.Equal("CARD-9", responseLog.Properties["CardNum"]);
        Assert.Equal("0", responseLog.Properties["HikCentralCode"]);
        Assert.Equal("Success", responseLog.Properties["HikCentralMessage"]);
        Assert.Equal("10.00", responseLog.Properties["ResponseFee"]);
        Assert.True(Convert.ToDouble(responseLog.Properties["ElapsedMs"], CultureInfo.InvariantCulture) >= 0);

        var loggedText = logger.JoinedText();
        Assert.DoesNotContain("test-secret", loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Ca-Signature", loggedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", loggedText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies confirm diagnostic logs include request fee, response feeTime, and vendor envelope metadata.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_LogsStructuredDiagnostics()
    {
        var correlationId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var logger = new CapturingLogger<HikCentralParkingClient>();
        var handler = new FakeHikCentralHandler(_ => SuccessfulConfirmResponse("200.00"));
        var client = CreateClient(handler, logger: logger);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest(null, "CARD-9", 1, 20000, "PHP", correlationId),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.Confirmed, result.Status);
        var responseLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(HikCentralParkingClient.ConfirmOperationName, responseLog.Properties["OperationName"]);
        Assert.Equal(HikCentralParkingClient.OutcomeSuccess, responseLog.Properties["Outcome"]);
        Assert.Equal("/artemis/api/vehicle/v1/parkingfee/confirm", responseLog.Properties["EndpointPath"]);
        Assert.Equal("CARD-9", responseLog.Properties["CardNum"]);
        Assert.Equal("200.00", responseLog.Properties["RequestFee"]);
        Assert.Equal("0", responseLog.Properties["HikCentralCode"]);
        Assert.Equal("Success", responseLog.Properties["HikCentralMessage"]);
        Assert.Equal("200.00", responseLog.Properties["ResponseFee"]);
        Assert.Equal("2022-04-12T14:48:11+08:00", responseLog.Properties["FeeTime"]);
        Assert.True(Convert.ToDouble(responseLog.Properties["ElapsedMs"], CultureInfo.InvariantCulture) >= 0);
    }

    /// <summary>
    /// Verifies nonzero HikCentral response codes are reported with the dedicated diagnostic outcome.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenCodeIsNonZero_LogsHikCentralNonzeroOutcome()
    {
        var logger = new CapturingLogger<HikCentralParkingClient>();
        var client = CreateClient(
            new FakeHikCentralHandler(_ => JsonResponse("""{ "code": "12345", "msg": "confirm failed", "data": {} }""")),
            logger: logger);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest("2700H", null, 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        var responseLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(HikCentralParkingClient.OutcomeHikCentralNonZeroCode, responseLog.Properties["Outcome"]);
        Assert.Equal("12345", responseLog.Properties["HikCentralCode"]);
        Assert.Equal("confirm failed", responseLog.Properties["HikCentralMessage"]);
        Assert.Equal("VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_12345", responseLog.Properties["ErrorCode"]);
        Assert.DoesNotContain("test-secret", logger.JoinedText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the explicit confirmation guard is logged without calling HikCentral confirm.
    /// </summary>
    [Fact]
    public async Task ConfirmParkingFee_WhenConfirmDisabled_LogsGuardBlockedWithoutVendorCall()
    {
        var logger = new CapturingLogger<HikCentralParkingClient>();
        var handler = new FakeHikCentralHandler(_ => throw new InvalidOperationException("Vendor should not be called."));
        var client = CreateClient(handler, confirmPaymentEnabled: false, logger: logger);

        var result = await client.ConfirmParkingFeeAsync(
            new VendorParkingFeeConfirmationRequest(null, "CARD-9", 1, 20000, "PHP", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(VendorParkingLookupStatus.AdapterError, result.Status);
        Assert.Equal("VENDOR_CONFIRMATION_DISABLED", result.ErrorCode);
        Assert.Null(handler.LastRequest);
        var responseLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(HikCentralParkingClient.OutcomeConfirmGuardBlocked, responseLog.Properties["Outcome"]);
        Assert.Equal(HikCentralParkingClient.ConfirmOperationName, responseLog.Properties["OperationName"]);
        Assert.Equal("CARD-9", responseLog.Properties["CardNum"]);
        Assert.Equal("200.00", responseLog.Properties["RequestFee"]);
    }

    private static HikCentralParkingClient CreateClient(
        HttpMessageHandler handler,
        bool confirmPaymentEnabled = true,
        ILogger<HikCentralParkingClient>? logger = null)
    {
        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"),
            () => DateTimeOffset.FromUnixTimeMilliseconds(1479968678000));
        return new HikCentralParkingClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://hikcentral.fake")
            },
            signer,
            "exitpass-adapter",
            confirmPaymentEnabled,
            logger);
    }

    private static void AssertSignedCalculateRequest(HttpRequestMessage? request)
    {
        AssertSignedRequest(request, "/artemis/api/vehicle/v1/parkingfee/calculate");
    }

    private static void AssertSignedRequest(HttpRequestMessage? request, string expectedPath)
    {
        Assert.NotNull(request);
        Assert.Equal("test-ak", request.Headers.GetValues("X-Ca-Key").Single());
        Assert.Equal("1479968678000", request.Headers.GetValues("X-Ca-Timestamp").Single());
        Assert.Equal("x-ca-key,x-ca-timestamp", request.Headers.GetValues("X-Ca-Signature-Headers").Single());
        Assert.NotEmpty(request.Headers.GetValues("X-Ca-Signature").Single());
        Assert.Equal("*/*", request.Headers.Accept.Single().ToString());
        Assert.Equal(expectedPath, request.RequestUri?.AbsolutePath);
        Assert.False(request.Content?.Headers.Contains("Content-MD5"));
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage SuccessfulFeeResponse(string plateLicense, string fee)
    {
        return JsonResponse($$"""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "plateLicense": "{{plateLicense}}",
                "parkingInTime": "2026-05-15T09:00:00+08:00",
                "parkingDuration": 3600,
                "feeRuleType": 1,
                "feeRuleIndexCode": "RULE-1",
                "feeRuleName": "Standard Parking",
                "fee": "{{fee}}"
              }
            }
            """);
    }

    private static HttpResponseMessage SuccessfulConfirmResponse(string fee)
    {
        return JsonResponse($$"""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "fee": "{{fee}}",
                "feeTime": "2022-04-12T14:48:11+08:00"
              }
            }
            """);
    }

    private sealed class FakeHikCentralHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHikCentralHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>>
                ?? [];
            Entries.Add(new CapturedLogEntry(
                logLevel,
                formatter(state, exception),
                properties
                    .Where(property => property.Key != "{OriginalFormat}")
                    .ToDictionary(property => property.Key, property => property.Value)));
        }

        public string JoinedText()
        {
            return string.Join(
                "|",
                Entries.Select(entry =>
                    $"{entry.Message}|{string.Join("|", entry.Properties.Select(property => $"{property.Key}={property.Value}"))}"));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
