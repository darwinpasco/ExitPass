using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// HikCentral Professional parking client for the adapter contract slice.
/// </summary>
/// <remarks>
/// Uses HikCentral Professional OpenAPI Developer Guide V3.1.0 parking fee calculation as the vendor API baseline.
/// </remarks>
/// <param name="httpClient">HTTP client configured by the caller.</param>
public sealed class HikCentralParkingClient(HttpClient httpClient) : IVendorParkingDataClient
{
    /// <summary>
    /// Provider code emitted by the HikCentral adapter.
    /// </summary>
    public const string ProviderCode = "HIKCENTRAL";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CalculateParkingFeeAsync(
            ResolveVendorLookupValue(request.PlateNumber, request.TicketReference),
            request.CorrelationId,
            cancellationToken);

        return result.ToSessionResponse(request.CorrelationId);
    }

    /// <inheritdoc />
    public async Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CalculateParkingFeeAsync(
            ResolveVendorLookupValue(request.PlateNumber, request.TicketReference),
            request.CorrelationId,
            cancellationToken);

        return result.ToTariffResponse(request.CorrelationId);
    }

    private static string ResolveVendorLookupValue(string? plateNumber, string? ticketReference)
    {
        return !string.IsNullOrWhiteSpace(plateNumber)
            ? plateNumber.Trim()
            : ticketReference?.Trim() ?? string.Empty;
    }

    private async Task<HikCentralParkingFeeLookupResult> CalculateParkingFeeAsync(
        string plateLicense,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/artemis/api/vehicle/v1/parkingfee/calculate");
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
            request.Content = JsonContent.Create(new HikCentralParkingFeeCalculateRequest(plateLicense), options: JsonOptions);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                return HikCentralParkingFeeLookupResult.NotFound();
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return HikCentralParkingFeeLookupResult.UnavailableRetryable();
            }

            if (!response.IsSuccessStatusCode)
            {
                return HikCentralParkingFeeLookupResult.AdapterError();
            }

            var envelope = await response.Content.ReadFromJsonAsync<HikCentralResponse<HikCentralParkingFeeCalculateData>>(
                JsonOptions,
                cancellationToken);

            if (envelope is null)
            {
                return HikCentralParkingFeeLookupResult.AdapterError();
            }

            if (!envelope.IsSuccess())
            {
                return envelope.IsNotFound()
                    ? HikCentralParkingFeeLookupResult.NotFound()
                    : HikCentralParkingFeeLookupResult.AdapterError();
            }

            return envelope.Data is null
                ? HikCentralParkingFeeLookupResult.NotFound()
                : HikCentralParkingFeeMapper.Map(envelope.Data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HikCentralParkingFeeLookupResult.UnavailableRetryable();
        }
        catch (HttpRequestException)
        {
            return HikCentralParkingFeeLookupResult.UnavailableRetryable();
        }
        catch (JsonException)
        {
            return HikCentralParkingFeeLookupResult.AdapterError();
        }
    }

    private sealed record HikCentralParkingFeeCalculateRequest(
        [property: JsonPropertyName("plateLicense")] string PlateLicense);

    private sealed record HikCentralResponse<T>(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("msg")] string? Message,
        [property: JsonPropertyName("data")] T? Data)
    {
        public bool IsSuccess()
        {
            return string.IsNullOrWhiteSpace(Code) || Code is "0" or "200";
        }

        public bool IsNotFound()
        {
            return Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                   Code is "404" or "0x00072002";
        }
    }

    private sealed record HikCentralParkingFeeCalculateData(
        [property: JsonPropertyName("plateLicense")] string? PlateLicense,
        [property: JsonPropertyName("parkingInTime")] string? ParkingInTime,
        [property: JsonPropertyName("parkingDuration")] int? ParkingDuration,
        [property: JsonPropertyName("feeRuleType")] int? FeeRuleType,
        [property: JsonPropertyName("feeRuleIndexCode")] string? FeeRuleIndexCode,
        [property: JsonPropertyName("feeRuleName")] string? FeeRuleName,
        [property: JsonPropertyName("fee")] string? Fee);

    private sealed record HikCentralParkingFeeLookupResult(
        VendorParkingLookupStatus Status,
        VendorParkingSessionDto? Session,
        VendorTariffQuoteDto? Quote,
        string? ErrorCode,
        bool Retryable)
    {
        public static HikCentralParkingFeeLookupResult Found(
            VendorParkingSessionDto session,
            VendorTariffQuoteDto tariffQuote)
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.Found,
                session,
                tariffQuote,
                null,
                false);
        }

        public static HikCentralParkingFeeLookupResult NotFound()
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.NotFound,
                null,
                null,
                "VENDOR_SESSION_NOT_FOUND",
                false);
        }

        public static HikCentralParkingFeeLookupResult UnavailableRetryable()
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.UnavailableRetryable,
                null,
                null,
                "VENDOR_PMS_UNAVAILABLE",
                true);
        }

        public static HikCentralParkingFeeLookupResult AdapterError()
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.AdapterError,
                null,
                null,
                "VENDOR_PMS_ADAPTER_ERROR",
                false);
        }

        public VendorParkingSessionLookupResponse ToSessionResponse(Guid correlationId)
        {
            return new VendorParkingSessionLookupResponse(Status, Session, ErrorCode, Retryable, correlationId);
        }

        public VendorTariffQuoteResponse ToTariffResponse(Guid correlationId)
        {
            return new VendorTariffQuoteResponse(Status, Quote, ErrorCode, Retryable, correlationId);
        }
    }

    private static class HikCentralParkingFeeMapper
    {
        public static HikCentralParkingFeeLookupResult Map(HikCentralParkingFeeCalculateData data)
        {
            if (string.IsNullOrWhiteSpace(data.PlateLicense) ||
                string.IsNullOrWhiteSpace(data.ParkingInTime) ||
                string.IsNullOrWhiteSpace(data.Fee) ||
                !DateTimeOffset.TryParse(data.ParkingInTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var entryTime) ||
                !TryParseAmountMinor(data.Fee, out var amountMinor))
            {
                return HikCentralParkingFeeLookupResult.AdapterError();
            }

            var tariffQuote = new VendorTariffQuoteDto(
                amountMinor,
                "PHP",
                data.FeeRuleIndexCode,
                data.FeeRuleName,
                DateTimeOffset.UtcNow);

            var session = new VendorParkingSessionDto(
                ProviderCode,
                BuildSessionReference(data.PlateLicense, entryTime),
                data.PlateLicense,
                entryTime,
                data.ParkingDuration,
                "ACTIVE",
                tariffQuote);

            return HikCentralParkingFeeLookupResult.Found(session, tariffQuote);
        }

        private static string BuildSessionReference(string plateLicense, DateTimeOffset entryTime)
        {
            return $"{ProviderCode}:{plateLicense}:{entryTime:yyyyMMddHHmmss}";
        }

        private static bool TryParseAmountMinor(string fee, out long amountMinor)
        {
            if (!decimal.TryParse(fee, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                amountMinor = 0;
                return false;
            }

            amountMinor = decimal.ToInt64(decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero));
            return amountMinor >= 0;
        }
    }
}
