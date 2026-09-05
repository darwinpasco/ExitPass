using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// HikCentral Professional parking client for the adapter contract slice.
/// </summary>
/// <remarks>
/// Uses HikCentral Professional OpenAPI Developer Guide V3.1.0 parking fee calculation as the vendor API baseline.
/// </remarks>
/// <param name="httpClient">HTTP client configured by the caller.</param>
/// <param name="requestSigner">HikCentral AK/SK request signer.</param>
/// <param name="userId">HikCentral required userId header value.</param>
/// <param name="confirmPaymentEnabled">Whether the mutating HikCentral fee confirmation call is allowed.</param>
/// <param name="logger">Structured diagnostics logger.</param>
public sealed class HikCentralParkingClient(
    HttpClient httpClient,
    IHikCentralRequestSigner requestSigner,
    string userId = "exitpass-adapter",
    bool? confirmPaymentEnabled = null,
    ILogger<HikCentralParkingClient>? logger = null) : IVendorParkingDataClient
{
    /// <summary>
    /// Provider code emitted by the HikCentral adapter.
    /// </summary>
    public const string ProviderCode = "HIKCENTRAL";

    /// <summary>
    /// Structured operation name for HikCentral parking fee calculation.
    /// </summary>
    public const string CalculateOperationName = "hikcentral.parkingfee.calculate";

    /// <summary>
    /// Structured operation name for HikCentral parking fee confirmation.
    /// </summary>
    public const string ConfirmOperationName = "hikcentral.parkingfee.confirm";

    /// <summary>
    /// Diagnostic outcome for successful HikCentral responses.
    /// </summary>
    public const string OutcomeSuccess = "success";

    /// <summary>
    /// Diagnostic outcome for nonzero HikCentral response codes.
    /// </summary>
    public const string OutcomeHikCentralNonZeroCode = "hikcentral_nonzero_code";

    /// <summary>
    /// Diagnostic outcome for transport, HTTP, validation, or mapping failures.
    /// </summary>
    public const string OutcomeRequestFailed = "request_failed";

    /// <summary>
    /// Diagnostic outcome for local guards blocking mutating confirmation calls.
    /// </summary>
    public const string OutcomeConfirmGuardBlocked = "confirm_guard_blocked";

    private const string ParkingFeeCalculatePath = "/artemis/api/vehicle/v1/parkingfee/calculate";
    private const string ParkingFeeConfirmPath = "/artemis/api/vehicle/v1/parkingfee/confirm";
    private const string CalculateEndpointName = "parkingfee.calculate";
    private const string ConfirmEndpointName = "parkingfee.confirm";

    private readonly IHikCentralRequestSigner _requestSigner =
        requestSigner ?? throw new InvalidOperationException("HikCentral request signer is required.");
    private readonly ILogger<HikCentralParkingClient> _logger =
        logger ?? NullLogger<HikCentralParkingClient>.Instance;

    private static readonly char[] UserIdForbiddenCharacters = ['\'', '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

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
            HikCentralParkingFeeCalculateRequest.FromProviderNeutral(
                request.PlateNumber,
                request.TicketReference),
            request.CorrelationId,
            cancellationToken);

        if (result.Status == VendorParkingLookupStatus.Found &&
            !MatchesRequestedIdentifier(result.Session, request.PlateNumber, request.TicketReference))
        {
            return HikCentralParkingFeeLookupResult.NotFound().ToSessionResponse(request.CorrelationId);
        }

        return result.ToSessionResponse(request.CorrelationId);
    }

    /// <inheritdoc />
    public async Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CalculateParkingFeeAsync(
            HikCentralParkingFeeCalculateRequest.FromProviderNeutral(
                request.PlateNumber,
                request.TicketReference),
            request.CorrelationId,
            cancellationToken);

        if (result.Status == VendorParkingLookupStatus.Found &&
            !MatchesRequestedIdentifier(result.Session, request.PlateNumber, request.TicketReference))
        {
            return HikCentralParkingFeeLookupResult.NotFound().ToTariffResponse(request.CorrelationId);
        }

        return result.ToTariffResponse(request.CorrelationId);
    }

    private static bool MatchesRequestedIdentifier(
        VendorParkingSessionDto? session,
        string? requestedPlateNumber,
        string? requestedTicketReference)
    {
        if (session is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedTicketReference))
        {
            var prefix = $"{ProviderCode}:{requestedTicketReference.Trim()}:";
            return session.VendorSessionReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(requestedPlateNumber) &&
            string.Equals(session.PlateNumber, requestedPlateNumber.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        var confirmRequest = HikCentralParkingFeeConfirmRequest.FromProviderNeutral(request);
        if (!(confirmPaymentEnabled ?? HikCentralOptions.ReadConfirmPaymentEnabledFromEnvironment()))
        {
            var disabled = HikCentralParkingFeeConfirmationResult.Disabled();
            var stopwatch = Stopwatch.StartNew();
            LogRequest(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                request.CorrelationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee);
            LogCompletion(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                OutcomeConfirmGuardBlocked,
                request.CorrelationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee,
                responseMetadata: null,
                httpStatusCode: null,
                disabled.Status,
                disabled.ErrorCode,
                stopwatch.Elapsed);
            return disabled.ToResponse(request.CorrelationId);
        }

        var result = await ConfirmParkingFeeAsync(
            confirmRequest,
            request.CorrelationId,
            cancellationToken);

        return result.ToResponse(request.CorrelationId);
    }

    private async Task<HikCentralParkingFeeLookupResult> CalculateParkingFeeAsync(
        HikCentralParkingFeeCalculateRequest calculateRequest,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogRequest(
            CalculateOperationName,
            CalculateEndpointName,
            ParkingFeeCalculatePath,
            correlationId,
            calculateRequest.PlateLicense,
            calculateRequest.CardNum,
            requestFee: null);

        try
        {
            var validationError = ValidateCalculateRequest(calculateRequest);
            if (validationError is not null)
            {
                LogCompletion(
                    CalculateOperationName,
                    CalculateEndpointName,
                    ParkingFeeCalculatePath,
                    OutcomeRequestFailed,
                    correlationId,
                    calculateRequest.PlateLicense,
                    calculateRequest.CardNum,
                    requestFee: null,
                    responseMetadata: null,
                    httpStatusCode: null,
                    validationError.Status,
                    validationError.ErrorCode,
                    stopwatch.Elapsed);
                return validationError;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                ParkingFeeCalculatePath);
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
            request.Headers.TryAddWithoutValidation("userId", userId);
            request.Content = CreateJsonContent(calculateRequest);
            await _requestSigner.SignAsync(request, cancellationToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseMetadata = HikCentralVendorResponseMetadata.FromBody(responseBody);

            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                var result = HikCentralParkingFeeLookupResult.NotFound();
                LogCompletion(
                    CalculateOperationName,
                    CalculateEndpointName,
                    ParkingFeeCalculatePath,
                    OutcomeRequestFailed,
                    correlationId,
                    calculateRequest.PlateLicense,
                    calculateRequest.CardNum,
                    requestFee: null,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                var result = HikCentralParkingFeeLookupResult.UnavailableRetryable();
                LogCompletion(
                    CalculateOperationName,
                    CalculateEndpointName,
                    ParkingFeeCalculatePath,
                    OutcomeRequestFailed,
                    correlationId,
                    calculateRequest.PlateLicense,
                    calculateRequest.CardNum,
                    requestFee: null,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var result = HikCentralParkingFeeLookupResult.AdapterError();
                LogCompletion(
                    CalculateOperationName,
                    CalculateEndpointName,
                    ParkingFeeCalculatePath,
                    OutcomeRequestFailed,
                    correlationId,
                    calculateRequest.PlateLicense,
                    calculateRequest.CardNum,
                    requestFee: null,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            var mappedResult = MapCalculateResponse(responseBody);
            LogCompletion(
                CalculateOperationName,
                CalculateEndpointName,
                ParkingFeeCalculatePath,
                DetermineOutcome(responseMetadata, mappedResult.Status),
                correlationId,
                calculateRequest.PlateLicense,
                calculateRequest.CardNum,
                requestFee: null,
                responseMetadata,
                response.StatusCode,
                mappedResult.Status,
                mappedResult.ErrorCode,
                stopwatch.Elapsed);
            return mappedResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var result = HikCentralParkingFeeLookupResult.UnavailableRetryable();
            LogCompletion(
                CalculateOperationName,
                CalculateEndpointName,
                ParkingFeeCalculatePath,
                OutcomeRequestFailed,
                correlationId,
                calculateRequest.PlateLicense,
                calculateRequest.CardNum,
                requestFee: null,
                responseMetadata: null,
                httpStatusCode: null,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
        catch (HttpRequestException)
        {
            var result = HikCentralParkingFeeLookupResult.UnavailableRetryable();
            LogCompletion(
                CalculateOperationName,
                CalculateEndpointName,
                ParkingFeeCalculatePath,
                OutcomeRequestFailed,
                correlationId,
                calculateRequest.PlateLicense,
                calculateRequest.CardNum,
                requestFee: null,
                responseMetadata: null,
                httpStatusCode: null,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
        catch (JsonException)
        {
            var result = HikCentralParkingFeeLookupResult.AdapterError();
            LogCompletion(
                CalculateOperationName,
                CalculateEndpointName,
                ParkingFeeCalculatePath,
                OutcomeRequestFailed,
                correlationId,
                calculateRequest.PlateLicense,
                calculateRequest.CardNum,
                requestFee: null,
                responseMetadata: null,
                httpStatusCode: HttpStatusCode.OK,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
    }

    private async Task<HikCentralParkingFeeConfirmationResult> ConfirmParkingFeeAsync(
        HikCentralParkingFeeConfirmRequest confirmRequest,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogRequest(
            ConfirmOperationName,
            ConfirmEndpointName,
            ParkingFeeConfirmPath,
            correlationId,
            confirmRequest.PlateLicense,
            confirmRequest.CardNum,
            confirmRequest.Fee);

        try
        {
            var validationError = ValidateConfirmRequest(confirmRequest);
            if (validationError is not null)
            {
                LogCompletion(
                    ConfirmOperationName,
                    ConfirmEndpointName,
                    ParkingFeeConfirmPath,
                    OutcomeConfirmGuardBlocked,
                    correlationId,
                    confirmRequest.PlateLicense,
                    confirmRequest.CardNum,
                    confirmRequest.Fee,
                    responseMetadata: null,
                    httpStatusCode: null,
                    validationError.Status,
                    validationError.ErrorCode,
                    stopwatch.Elapsed);
                return validationError;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                ParkingFeeConfirmPath);
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
            request.Headers.TryAddWithoutValidation("userId", userId);
            request.Content = CreateJsonContent(confirmRequest);
            await _requestSigner.SignAsync(request, cancellationToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseMetadata = HikCentralVendorResponseMetadata.FromBody(responseBody);

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                var result = HikCentralParkingFeeConfirmationResult.UnavailableRetryable();
                LogCompletion(
                    ConfirmOperationName,
                    ConfirmEndpointName,
                    ParkingFeeConfirmPath,
                    OutcomeRequestFailed,
                    correlationId,
                    confirmRequest.PlateLicense,
                    confirmRequest.CardNum,
                    confirmRequest.Fee,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                var result = HikCentralParkingFeeConfirmationResult.AdapterError();
                LogCompletion(
                    ConfirmOperationName,
                    ConfirmEndpointName,
                    ParkingFeeConfirmPath,
                    OutcomeRequestFailed,
                    correlationId,
                    confirmRequest.PlateLicense,
                    confirmRequest.CardNum,
                    confirmRequest.Fee,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            var envelope = JsonSerializer.Deserialize<HikCentralResponse<HikCentralParkingFeeConfirmData>>(
                responseBody,
                JsonOptions);

            if (envelope is null)
            {
                var result = HikCentralParkingFeeConfirmationResult.AdapterError();
                LogCompletion(
                    ConfirmOperationName,
                    ConfirmEndpointName,
                    ParkingFeeConfirmPath,
                    OutcomeRequestFailed,
                    correlationId,
                    confirmRequest.PlateLicense,
                    confirmRequest.CardNum,
                    confirmRequest.Fee,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            if (!envelope.IsSuccess())
            {
                var result = HikCentralParkingFeeConfirmationResult.AdapterError(envelope.Code);
                LogCompletion(
                    ConfirmOperationName,
                    ConfirmEndpointName,
                    ParkingFeeConfirmPath,
                    DetermineOutcome(responseMetadata, result.Status),
                    correlationId,
                    confirmRequest.PlateLicense,
                    confirmRequest.CardNum,
                    confirmRequest.Fee,
                    responseMetadata,
                    response.StatusCode,
                    result.Status,
                    result.ErrorCode,
                    stopwatch.Elapsed);
                return result;
            }

            var mappedResult = envelope.Data is null
                ? HikCentralParkingFeeConfirmationResult.AdapterError()
                : HikCentralParkingFeeConfirmationMapper.Map(envelope.Data);
            LogCompletion(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                DetermineOutcome(responseMetadata, mappedResult.Status),
                correlationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee,
                responseMetadata,
                response.StatusCode,
                mappedResult.Status,
                mappedResult.ErrorCode,
                stopwatch.Elapsed);
            return mappedResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var result = HikCentralParkingFeeConfirmationResult.UnavailableRetryable();
            LogCompletion(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                OutcomeRequestFailed,
                correlationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee,
                responseMetadata: null,
                httpStatusCode: null,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
        catch (HttpRequestException)
        {
            var result = HikCentralParkingFeeConfirmationResult.UnavailableRetryable();
            LogCompletion(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                OutcomeRequestFailed,
                correlationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee,
                responseMetadata: null,
                httpStatusCode: null,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
        catch (JsonException)
        {
            var result = HikCentralParkingFeeConfirmationResult.AdapterError();
            LogCompletion(
                ConfirmOperationName,
                ConfirmEndpointName,
                ParkingFeeConfirmPath,
                OutcomeRequestFailed,
                correlationId,
                confirmRequest.PlateLicense,
                confirmRequest.CardNum,
                confirmRequest.Fee,
                responseMetadata: null,
                httpStatusCode: HttpStatusCode.OK,
                result.Status,
                result.ErrorCode,
                stopwatch.Elapsed);
            return result;
        }
    }

    private static string DetermineOutcome(
        HikCentralVendorResponseMetadata? responseMetadata,
        VendorParkingLookupStatus vendorStatus)
    {
        if (responseMetadata?.Code is not null && responseMetadata.Code != "0")
        {
            return OutcomeHikCentralNonZeroCode;
        }

        return responseMetadata?.Code == "0" &&
               vendorStatus is not VendorParkingLookupStatus.AdapterError and not VendorParkingLookupStatus.UnavailableRetryable
            ? OutcomeSuccess
            : OutcomeRequestFailed;
    }

    private void LogRequest(
        string operationName,
        string endpointName,
        string endpointPath,
        Guid correlationId,
        string? plateLicense,
        string? cardNum,
        string? requestFee)
    {
        _logger.LogInformation(
            "HikCentral parking fee request {OperationName} {EndpointName} {EndpointPath} {CorrelationId} {PlateLicense} {CardNum} {RequestFee}",
            operationName,
            endpointName,
            endpointPath,
            correlationId,
            plateLicense,
            cardNum,
            requestFee);
    }

    private void LogCompletion(
        string operationName,
        string endpointName,
        string endpointPath,
        string outcome,
        Guid correlationId,
        string? plateLicense,
        string? cardNum,
        string? requestFee,
        HikCentralVendorResponseMetadata? responseMetadata,
        HttpStatusCode? httpStatusCode,
        VendorParkingLookupStatus vendorStatus,
        string? errorCode,
        TimeSpan elapsed)
    {
        _logger.LogInformation(
            "HikCentral parking fee response {OperationName} {Outcome} {EndpointName} {EndpointPath} {CorrelationId} {PlateLicense} {CardNum} {RequestFee} {HikCentralCode} {HikCentralMessage} {ResponseFee} {FeeTime} {ElapsedMs} {HttpStatusCode} {VendorStatus} {ErrorCode}",
            operationName,
            outcome,
            endpointName,
            endpointPath,
            correlationId,
            plateLicense,
            cardNum,
            requestFee,
            responseMetadata?.Code,
            responseMetadata?.Message,
            responseMetadata?.Fee,
            responseMetadata?.FeeTime,
            elapsed.TotalMilliseconds,
            httpStatusCode.HasValue ? (int)httpStatusCode.Value : null,
            vendorStatus.ToString(),
            errorCode);
    }

    private sealed record HikCentralVendorResponseMetadata(
        string? Code,
        string? Message,
        string? Fee,
        string? FeeTime)
    {
        public static HikCentralVendorResponseMetadata? FromBody(string? responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    return null;
                }

                var code = TryGetString(document.RootElement, "code");
                var message = TryGetString(document.RootElement, "msg");
                var (fee, feeTime) = ExtractFeeMetadata(document.RootElement);
                return new HikCentralVendorResponseMetadata(code, message, fee, feeTime);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static (string? Fee, string? FeeTime) ExtractFeeMetadata(JsonElement root)
        {
            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return (null, null);
            }

            if (data.ValueKind is JsonValueKind.Array)
            {
                data = data.EnumerateArray().FirstOrDefault();
            }

            if (data.ValueKind is not JsonValueKind.Object)
            {
                return (null, null);
            }

            return (
                TryGetString(data, "fee"),
                TryGetString(data, "feeTime"));
        }
    }

    private sealed record HikCentralParkingFeeCalculateRequest(
        [property: JsonPropertyName("plateLicense")] string? PlateLicense,
        [property: JsonPropertyName("cardNum")] string? CardNum)
    {
        public static HikCentralParkingFeeCalculateRequest FromProviderNeutral(
            string? plateNumber,
            string? ticketReference)
        {
            return new HikCentralParkingFeeCalculateRequest(
                Normalize(plateNumber),
                Normalize(ticketReference));
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    private sealed record HikCentralParkingFeeConfirmRequest(
        [property: JsonPropertyName("plateLicense")] string? PlateLicense,
        [property: JsonPropertyName("cardNum")] string? CardNum,
        [property: JsonPropertyName("immediatelyLeave")] int ImmediatelyLeave,
        [property: JsonPropertyName("fee")] string? Fee)
    {
        public static HikCentralParkingFeeConfirmRequest FromProviderNeutral(
            VendorParkingFeeConfirmationRequest request)
        {
            return new HikCentralParkingFeeConfirmRequest(
                Normalize(request.PlateNumber),
                Normalize(request.TicketReference),
                request.ImmediatelyLeave,
                FormatFee(request.AmountMinor));
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? FormatFee(long? amountMinor)
        {
            return amountMinor.HasValue
                ? (amountMinor.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture)
                : null;
        }
    }

    private sealed record HikCentralResponse<T>(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("msg")] string? Message,
        [property: JsonPropertyName("data")] T? Data)
    {
        public bool IsSuccess()
        {
            return Code is "0";
        }
    }

    private static HikCentralParkingFeeLookupResult MapCalculateResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return HikCentralParkingFeeLookupResult.AdapterError();
        }

        var code = TryGetString(document.RootElement, "code");
        var message = TryGetString(document.RootElement, "msg");

        if (code is "128" &&
            message?.Contains("vehicle is not exist", StringComparison.OrdinalIgnoreCase) == true)
        {
            return HikCentralParkingFeeLookupResult.NotFound();
        }

        if (code is not "0")
        {
            return HikCentralParkingFeeLookupResult.AdapterError(code);
        }

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return HikCentralParkingFeeLookupResult.NotFound();
        }

        return data.ValueKind switch
        {
            JsonValueKind.Object => MapCalculateData(data),
            JsonValueKind.Array => MapCalculateArray(data),
            _ => HikCentralParkingFeeLookupResult.AdapterError()
        };
    }

    private static HikCentralParkingFeeLookupResult MapCalculateArray(JsonElement data)
    {
        var matchCount = data.GetArrayLength();
        if (matchCount == 0)
        {
            return HikCentralParkingFeeLookupResult.NotFound();
        }

        // ExitPass v1.2 invariant: vendor IDs remain external references; ambiguous vendor matches are not collapsed
        // into a guessed ExitPass session.
        if (matchCount > 1)
        {
            return HikCentralParkingFeeLookupResult.Ambiguous();
        }

        return data[0].ValueKind is JsonValueKind.Object
            ? MapCalculateData(data[0])
            : HikCentralParkingFeeLookupResult.AdapterError();
    }

    private static HikCentralParkingFeeLookupResult MapCalculateData(JsonElement data)
    {
        var calculateData = data.Deserialize<HikCentralParkingFeeCalculateData>(JsonOptions);
        return calculateData is null
            ? HikCentralParkingFeeLookupResult.AdapterError()
            : HikCentralParkingFeeMapper.Map(calculateData);
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static StringContent CreateJsonContent(object value)
    {
        var content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private sealed record HikCentralParkingFeeCalculateData(
        [property: JsonPropertyName("plateLicense")] string? PlateLicense,
        [property: JsonPropertyName("cardNum")] string? CardNum,
        [property: JsonPropertyName("parkingInTime")] string? ParkingInTime,
        [property: JsonPropertyName("parkingDuration")] int? ParkingDuration,
        [property: JsonPropertyName("feeRuleType")] int? FeeRuleType,
        [property: JsonPropertyName("feeRuleIndexCode")] string? FeeRuleIndexCode,
        [property: JsonPropertyName("feeRuleName")] string? FeeRuleName,
        [property: JsonPropertyName("fee")] string? Fee);

    private sealed record HikCentralParkingFeeConfirmData(
        [property: JsonPropertyName("fee")] string? Fee,
        [property: JsonPropertyName("feeTime")] string? FeeTime);

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

        public static HikCentralParkingFeeLookupResult AdapterError(string? hikCentralCode = null)
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.AdapterError,
                null,
                null,
                BuildAdapterErrorCode(hikCentralCode),
                false);
        }

        public static HikCentralParkingFeeLookupResult ValidationError()
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.ValidationError,
                null,
                null,
                "VENDOR_LOOKUP_VALIDATION_ERROR",
                false);
        }

        public static HikCentralParkingFeeLookupResult Ambiguous()
        {
            return new HikCentralParkingFeeLookupResult(
                VendorParkingLookupStatus.Ambiguous,
                null,
                null,
                "VENDOR_SESSION_AMBIGUOUS",
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

        private static string BuildAdapterErrorCode(string? hikCentralCode)
        {
            if (string.IsNullOrWhiteSpace(hikCentralCode))
            {
                return "VENDOR_PMS_ADAPTER_ERROR";
            }

            var safeCode = new string(hikCentralCode.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(safeCode)
                ? "VENDOR_PMS_ADAPTER_ERROR"
                : $"VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_{safeCode}";
        }
    }

    private sealed record HikCentralParkingFeeConfirmationResult(
        VendorParkingLookupStatus Status,
        VendorParkingFeeConfirmationDto? Confirmation,
        string? ErrorCode,
        bool Retryable)
    {
        public static HikCentralParkingFeeConfirmationResult Confirmed(
            VendorParkingFeeConfirmationDto confirmation)
        {
            return new HikCentralParkingFeeConfirmationResult(
                VendorParkingLookupStatus.Confirmed,
                confirmation,
                null,
                false);
        }

        public static HikCentralParkingFeeConfirmationResult UnavailableRetryable()
        {
            return new HikCentralParkingFeeConfirmationResult(
                VendorParkingLookupStatus.UnavailableRetryable,
                null,
                "VENDOR_PMS_UNAVAILABLE",
                true);
        }

        public static HikCentralParkingFeeConfirmationResult AdapterError(string? hikCentralCode = null)
        {
            return new HikCentralParkingFeeConfirmationResult(
                VendorParkingLookupStatus.AdapterError,
                null,
                BuildAdapterErrorCode(hikCentralCode),
                false);
        }

        public static HikCentralParkingFeeConfirmationResult Disabled()
        {
            return new HikCentralParkingFeeConfirmationResult(
                VendorParkingLookupStatus.AdapterError,
                null,
                "VENDOR_CONFIRMATION_DISABLED",
                false);
        }

        public static HikCentralParkingFeeConfirmationResult ValidationError()
        {
            return new HikCentralParkingFeeConfirmationResult(
                VendorParkingLookupStatus.ValidationError,
                null,
                "VENDOR_CONFIRMATION_VALIDATION_ERROR",
                false);
        }

        public VendorParkingFeeConfirmationResponse ToResponse(Guid correlationId)
        {
            return new VendorParkingFeeConfirmationResponse(Status, Confirmation, ErrorCode, Retryable, correlationId);
        }

        private static string BuildAdapterErrorCode(string? hikCentralCode)
        {
            if (string.IsNullOrWhiteSpace(hikCentralCode))
            {
                return "VENDOR_PMS_ADAPTER_ERROR";
            }

            var safeCode = new string(hikCentralCode.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(safeCode)
                ? "VENDOR_PMS_ADAPTER_ERROR"
                : $"VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_{safeCode}";
        }
    }

    private HikCentralParkingFeeLookupResult? ValidateCalculateRequest(
        HikCentralParkingFeeCalculateRequest calculateRequest)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            userId.Length > 32 ||
            userId.IndexOfAny(UserIdForbiddenCharacters) >= 0)
        {
            return HikCentralParkingFeeLookupResult.ValidationError();
        }

        if (calculateRequest is { PlateLicense: null, CardNum: null })
        {
            return HikCentralParkingFeeLookupResult.ValidationError();
        }

        if (calculateRequest.PlateLicense?.Length > 32 || calculateRequest.CardNum?.Length > 32)
        {
            return HikCentralParkingFeeLookupResult.ValidationError();
        }

        return null;
    }

    private HikCentralParkingFeeConfirmationResult? ValidateConfirmRequest(
        HikCentralParkingFeeConfirmRequest confirmRequest)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            userId.Length > 32 ||
            userId.IndexOfAny(UserIdForbiddenCharacters) >= 0)
        {
            return HikCentralParkingFeeConfirmationResult.ValidationError();
        }

        if (confirmRequest is { PlateLicense: null, CardNum: null })
        {
            return HikCentralParkingFeeConfirmationResult.ValidationError();
        }

        if (confirmRequest.PlateLicense?.Length > 32 || confirmRequest.CardNum?.Length > 32)
        {
            return HikCentralParkingFeeConfirmationResult.ValidationError();
        }

        if (confirmRequest.ImmediatelyLeave is not (0 or 1))
        {
            return HikCentralParkingFeeConfirmationResult.ValidationError();
        }

        if (string.IsNullOrWhiteSpace(confirmRequest.Fee) || confirmRequest.Fee.Length > 32)
        {
            return HikCentralParkingFeeConfirmationResult.ValidationError();
        }

        return null;
    }

    private static class HikCentralParkingFeeMapper
    {
        public static HikCentralParkingFeeLookupResult Map(HikCentralParkingFeeCalculateData data)
        {
            if (string.IsNullOrWhiteSpace(data.ParkingInTime) ||
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

            var plateNumber = NormalizePlateLicense(data.PlateLicense);
            var sessionReferenceIdentifier = string.IsNullOrWhiteSpace(data.CardNum)
                ? plateNumber
                : data.CardNum.Trim();

            var session = new VendorParkingSessionDto(
                ProviderCode,
                BuildSessionReference(sessionReferenceIdentifier, entryTime),
                plateNumber,
                entryTime,
                data.ParkingDuration,
                "ACTIVE",
                tariffQuote,
                NormalizeTicketReference(data.CardNum));

            return HikCentralParkingFeeLookupResult.Found(session, tariffQuote);
        }

        private static string NormalizePlateLicense(string? plateLicense)
        {
            return string.IsNullOrWhiteSpace(plateLicense)
                ? "Unknown"
                : plateLicense.Trim();
        }

        private static string? NormalizeTicketReference(string? cardNum) =>
            string.IsNullOrWhiteSpace(cardNum) ? null : cardNum.Trim();

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

    private static class HikCentralParkingFeeConfirmationMapper
    {
        public static HikCentralParkingFeeConfirmationResult Map(HikCentralParkingFeeConfirmData data)
        {
            if (string.IsNullOrWhiteSpace(data.Fee) ||
                string.IsNullOrWhiteSpace(data.FeeTime) ||
                !DateTimeOffset.TryParse(data.FeeTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var feeTime) ||
                !TryParseAmountMinor(data.Fee, out var amountMinor))
            {
                return HikCentralParkingFeeConfirmationResult.AdapterError();
            }

            return HikCentralParkingFeeConfirmationResult.Confirmed(
                new VendorParkingFeeConfirmationDto(amountMinor, "PHP", feeTime));
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
