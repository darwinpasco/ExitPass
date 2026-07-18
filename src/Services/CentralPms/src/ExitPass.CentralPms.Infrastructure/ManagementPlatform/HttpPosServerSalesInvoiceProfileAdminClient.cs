using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class HttpPosServerSalesInvoiceProfileAdminClient : IPosServerSalesInvoiceProfileAdminClient
{
    internal const string AdminKeyHeaderName = "X-PosServer-Admin-Key";
    internal const string CorrelationHeaderName = "X-Correlation-Id";
    internal const string PermissionHeaderName = "X-PosServer-Admin-Permission";

    private const string FiscalIdentitiesPath = "/v1/admin/fiscal-identities";
    private const string ProfilesPath = "/v1/admin/sales-invoice-header-profiles";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PosServerSalesInvoiceProfileAdministrationOptions _options;

    public HttpPosServerSalesInvoiceProfileAdminClient(
        HttpClient httpClient,
        PosServerSalesInvoiceProfileAdministrationOptions options)
    {
        _httpClient = httpClient;
        _options = options ?? new PosServerSalesInvoiceProfileAdministrationOptions();
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformFiscalIdentity>(
            HttpMethod.Post,
            FiscalIdentitiesPath,
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformFiscalIdentity>(
            HttpMethod.Get,
            $"{FiscalIdentitiesPath}/{fiscalIdentityId:D}",
            body: null,
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformFiscalIdentity>(
            HttpMethod.Put,
            $"{FiscalIdentitiesPath}/{fiscalIdentityId:D}",
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfile>(
            HttpMethod.Post,
            ProfilesPath,
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfile>(
            HttpMethod.Get,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}",
            body: null,
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
        ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new List<string>();
        if (request.SiteId is { } siteId && siteId != Guid.Empty)
        {
            query.Add($"siteId={Uri.EscapeDataString(siteId.ToString("D"))}");
        }

        if (request.SitePosServerId is { } sitePosServerId && sitePosServerId != Guid.Empty)
        {
            query.Add($"sitePosServerId={Uri.EscapeDataString(sitePosServerId.ToString("D"))}");
        }

        if (!string.IsNullOrWhiteSpace(request.LifecycleState))
        {
            query.Add($"lifecycleState={Uri.EscapeDataString(request.LifecycleState)}");
        }

        var path = query.Count == 0 ? ProfilesPath : $"{ProfilesPath}?{string.Join("&", query)}";
        return SendAsync<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>(
            HttpMethod.Get,
            path,
            body: null,
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfile>(
            HttpMethod.Put,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}/draft",
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfileValidation>(
            HttpMethod.Post,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}/validate",
            body: new { },
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfile>(
            HttpMethod.Post,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}/approve",
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfile>(
            HttpMethod.Post,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}/retire",
            request,
            context,
            isMutation: true,
            cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
        ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = $"{ProfilesPath}/effective-readiness?siteId={Uri.EscapeDataString(request.SiteId.ToString("D"))}" +
            $"&sitePosServerId={Uri.EscapeDataString(request.SitePosServerId.ToString("D"))}" +
            $"&effectiveAt={Uri.EscapeDataString(request.EffectiveAt.ToString("O"))}";

        return SendAsync<ManagementPlatformSalesInvoiceHeaderProfileReadiness>(
            HttpMethod.Get,
            path,
            body: null,
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        SendAsync<ManagementPlatformSalesInvoiceHeaderProfileUsage>(
            HttpMethod.Get,
            $"{ProfilesPath}/{salesInvoiceHeaderProfileId:D}/usage",
            body: null,
            context: context,
            isMutation: false,
            cancellationToken: cancellationToken);

    private async Task<PosServerSalesInvoiceProfileAdminResult<T>> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        ManagementPlatformPosServerAdminRequestContext context,
        bool isMutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var correlationId = context.GetOrCreateCorrelationId();

        if (!_options.Enabled)
        {
            return PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                PosServerSalesInvoiceProfileAdminOutcome.Disabled,
                "pos_server_sales_invoice_profile_admin_disabled",
                "POS Server Sales Invoice profile administration integration is disabled.",
                correlationId);
        }

        var validationErrors = _options.Validate();
        if (validationErrors.Count > 0)
        {
            return PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                PosServerSalesInvoiceProfileAdminOutcome.InvalidConfiguration,
                validationErrors[0],
                "POS Server Sales Invoice profile administration integration configuration is invalid.",
                correlationId);
        }

        var attempts = !isMutation && method == HttpMethod.Get ? 2 : 1;
        var retried = false;
        PosServerSalesInvoiceProfileAdminResult<T>? lastResult = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = BuildRequest(method, relativePath, body, correlationId);
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseCorrelationId = ResolveCorrelationId(response, responseBody, correlationId);
                var result = MapResponse<T>(
                    (int)response.StatusCode,
                    responseBody,
                    responseCorrelationId,
                    isMutation,
                    retried);

                if (result.Succeeded || isMutation || !IsTransient(result.Outcome) || attempt == attempts)
                {
                    return result;
                }

                retried = true;
                lastResult = result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastResult = PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                    PosServerSalesInvoiceProfileAdminOutcome.Timeout,
                    "pos_server_sales_invoice_profile_admin_timeout",
                    "POS Server Sales Invoice profile administration request timed out.",
                    correlationId,
                    mutationSent: isMutation,
                    retried: retried);

                if (isMutation || attempt == attempts)
                {
                    return lastResult;
                }

                retried = true;
            }
            catch (HttpRequestException)
            {
                lastResult = PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                    PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure,
                    "pos_server_sales_invoice_profile_admin_network_failure",
                    "POS Server Sales Invoice profile administration endpoint was unavailable.",
                    correlationId,
                    mutationSent: isMutation,
                    retried: retried);

                if (isMutation || attempt == attempts)
                {
                    return lastResult;
                }

                retried = true;
            }
        }

        return lastResult ?? PosServerSalesInvoiceProfileAdminResult<T>.Failure(
            PosServerSalesInvoiceProfileAdminOutcome.UnknownFailure,
            "pos_server_sales_invoice_profile_admin_unknown_failure",
            "POS Server Sales Invoice profile administration request failed.",
            correlationId);
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string relativePath,
        object? body,
        Guid correlationId)
    {
        var uri = new Uri(new Uri(_options.BaseUrl!, UriKind.Absolute), relativePath);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation(AdminKeyHeaderName, _options.ApiKey);
        request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId.ToString("D"));
        request.Headers.Remove(PermissionHeaderName);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private static PosServerSalesInvoiceProfileAdminResult<T> MapResponse<T>(
        int httpStatusCode,
        string responseBody,
        Guid correlationId,
        bool isMutation,
        bool retried)
    {
        if (httpStatusCode >= 200 && httpStatusCode <= 299)
        {
            if (TryDeserializeValue<T>(responseBody, out var value))
            {
                return PosServerSalesInvoiceProfileAdminResult<T>.Success(
                    value!,
                    correlationId,
                    httpStatusCode,
                    mutationSent: isMutation,
                    retried: retried);
            }

            return PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                PosServerSalesInvoiceProfileAdminOutcome.MalformedResponse,
                "pos_server_sales_invoice_profile_admin_malformed_response",
                "POS Server Sales Invoice profile administration response could not be mapped.",
                correlationId,
                httpStatusCode,
                isMutation,
                retried);
        }

        var error = TryDeserializeError(responseBody);
        var outcome = MapFailureOutcome(httpStatusCode);
        return PosServerSalesInvoiceProfileAdminResult<T>.Failure(
            outcome,
            string.IsNullOrWhiteSpace(error.Code) ? DefaultErrorCode(outcome) : error.Code,
            string.IsNullOrWhiteSpace(error.Message) ? DefaultErrorMessage(outcome) : error.Message,
            correlationId,
            httpStatusCode,
            isMutation,
            retried);
    }

    private static bool TryDeserializeValue<T>(string responseBody, out T? value)
    {
        value = default;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var data = TryGetPayloadElement(root);
            value = JsonSerializer.Deserialize<T>(data.GetRawText(), JsonOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement TryGetPayloadElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "data", "fiscalIdentity", "profile", "validation", "readiness", "usage", "profiles" })
            {
                if (root.TryGetProperty(propertyName, out var nested))
                {
                    return nested;
                }
            }
        }

        return root;
    }

    private static PosServerErrorEnvelope TryDeserializeError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                return new PosServerErrorEnvelope(
                    TryGetString(error, "code") ?? TryGetString(error, "errorCode"),
                    TryGetString(error, "message"));
            }

            return new PosServerErrorEnvelope(
                TryGetString(root, "code") ?? TryGetString(root, "errorCode"),
                TryGetString(root, "message"));
        }
        catch (JsonException)
        {
            return new PosServerErrorEnvelope(null, null);
        }
    }

    private static Guid ResolveCorrelationId(
        HttpResponseMessage response,
        string responseBody,
        Guid fallback)
    {
        if (response.Headers.TryGetValues(CorrelationHeaderName, out var values) &&
            Guid.TryParse(values.FirstOrDefault(), out var headerCorrelationId) &&
            headerCorrelationId != Guid.Empty)
        {
            return headerCorrelationId;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetGuid(document.RootElement, "correlationId") is { } bodyCorrelationId &&
                bodyCorrelationId != Guid.Empty)
            {
                return bodyCorrelationId;
            }
        }
        catch (JsonException)
        {
            // Safe fallback to the outbound correlation id.
        }

        return fallback;
    }

    private static PosServerSalesInvoiceProfileAdminOutcome MapFailureOutcome(int httpStatusCode) =>
        httpStatusCode switch
        {
            400 => PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest,
            401 => PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed,
            403 => PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied,
            404 => PosServerSalesInvoiceProfileAdminOutcome.NotFound,
            409 => PosServerSalesInvoiceProfileAdminOutcome.Conflict,
            422 => PosServerSalesInvoiceProfileAdminOutcome.ValidationFailure,
            429 => PosServerSalesInvoiceProfileAdminOutcome.Throttled,
            >= 500 => PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable,
            _ => PosServerSalesInvoiceProfileAdminOutcome.UnknownFailure
        };

    private static bool IsTransient(PosServerSalesInvoiceProfileAdminOutcome outcome) =>
        outcome is PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable
            or PosServerSalesInvoiceProfileAdminOutcome.Timeout
            or PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure
            or PosServerSalesInvoiceProfileAdminOutcome.Throttled;

    private static string DefaultErrorCode(PosServerSalesInvoiceProfileAdminOutcome outcome) =>
        outcome switch
        {
            PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest => "pos_server_sales_invoice_profile_admin_invalid_request",
            PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed => "pos_server_sales_invoice_profile_admin_authentication_failed",
            PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied => "pos_server_sales_invoice_profile_admin_permission_denied",
            PosServerSalesInvoiceProfileAdminOutcome.NotFound => "pos_server_sales_invoice_profile_admin_not_found",
            PosServerSalesInvoiceProfileAdminOutcome.Conflict => "pos_server_sales_invoice_profile_admin_conflict",
            PosServerSalesInvoiceProfileAdminOutcome.ValidationFailure => "pos_server_sales_invoice_profile_admin_validation_failure",
            PosServerSalesInvoiceProfileAdminOutcome.Throttled => "pos_server_sales_invoice_profile_admin_throttled",
            PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable => "pos_server_sales_invoice_profile_admin_unavailable",
            _ => "pos_server_sales_invoice_profile_admin_failed"
        };

    private static string DefaultErrorMessage(PosServerSalesInvoiceProfileAdminOutcome outcome) =>
        outcome switch
        {
            PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed => "POS Server administration authentication failed.",
            PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied => "Management Platform service identity lacks POS Server administration permission.",
            PosServerSalesInvoiceProfileAdminOutcome.NotFound => "POS Server Fiscal Identity or Sales Invoice Header Profile was not found.",
            PosServerSalesInvoiceProfileAdminOutcome.Conflict => "POS Server rejected the request because of a governed lifecycle, overlap, duplicate, immutability, or state conflict.",
            PosServerSalesInvoiceProfileAdminOutcome.Throttled => "POS Server administration endpoint throttled the request.",
            PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable => "POS Server administration endpoint failed or was unavailable.",
            _ => "POS Server Sales Invoice profile administration request failed."
        };

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Guid? TryGetGuid(JsonElement root, string propertyName) =>
        TryGetString(root, propertyName) is { } value && Guid.TryParse(value, out var parsed)
            ? parsed
            : null;

    private sealed record PosServerErrorEnvelope(string? Code, string? Message);
}
