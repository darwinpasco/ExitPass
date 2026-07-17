using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Sends already-built signed HikCentral HTTP requests without owning credentials, commands, or audits.
/// </summary>
public sealed class HikCentralHttpTransport : IHikCentralHttpTransport
{
    private static readonly string[] RequiredRequestHeaders =
    [
        HikCentralRequestSigningMaterialConstants.HeaderAccept,
        HikCentralRequestSigningMaterialConstants.HeaderClientKey,
        HikCentralRequestSigningMaterialConstants.HeaderNonce,
        HikCentralRequestSigningMaterialConstants.HeaderTimestamp,
        HikCentralRequestSigningMaterialConstants.HeaderSignatureMethod,
        HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders,
        HikCentralRequestSigningMaterialConstants.HeaderSignature
    ];

    private readonly HttpClient _httpClient;
    private readonly HikCentralHttpTransportOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a HikCentral HTTP transport over an injected client.
    /// </summary>
    public HikCentralHttpTransport(
        HttpClient httpClient,
        HikCentralHttpTransportOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = ValidateOptions(options ?? new HikCentralHttpTransportOptions());
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HikCentralHttpTransportResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await ReadBoundedBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return BuildResponseResult(response, body, started);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return BuildTransportResult(
                HikCentralHttpTransportOutcome.TimedOut,
                timedOut: true,
                transportFailure: false,
                vendorUnavailable: false,
                started);
        }
        catch (HttpRequestException)
        {
            return BuildTransportResult(
                HikCentralHttpTransportOutcome.TransportFailure,
                timedOut: false,
                transportFailure: true,
                vendorUnavailable: true,
                started);
        }
    }

    private HikCentralHttpTransportResult BuildResponseResult(
        HttpResponseMessage response,
        BoundedResponseBody body,
        long started)
    {
        var statusCode = (int)response.StatusCode;
        if (body.TooLarge)
        {
            return new HikCentralHttpTransportResult(
                statusCode,
                response.IsSuccessStatusCode,
                HikCentralHttpTransportOutcome.ResponseBodyTooLarge,
                TimedOut: false,
                TransportFailure: false,
                VendorUnavailable: false,
                ResponseBodyTooLarge: true,
                body.ByteCount,
                ResponseBodySha256: null,
                VendorResultCode: null,
                VendorResultMessage: null,
                VendorCorrelationId: null,
                ElapsedMilliseconds(started),
                _timeProvider.GetUtcNow());
        }

        var responseHash = Sha256Hex(body.Body);
        var parsed = TryParseVendorEnvelope(body.Body);
        var outcome = parsed.Malformed
            ? HikCentralHttpTransportOutcome.MalformedResponse
            : ClassifyStatus(response.StatusCode);

        return new HikCentralHttpTransportResult(
            statusCode,
            response.IsSuccessStatusCode,
            outcome,
            TimedOut: response.StatusCode == HttpStatusCode.RequestTimeout,
            TransportFailure: false,
            VendorUnavailable: response.StatusCode == HttpStatusCode.ServiceUnavailable || (int)response.StatusCode >= 500,
            ResponseBodyTooLarge: false,
            body.ByteCount,
            responseHash,
            parsed.VendorResultCode,
            parsed.VendorResultMessage,
            VendorCorrelationId: null,
            ElapsedMilliseconds(started),
            _timeProvider.GetUtcNow());
    }

    private HikCentralHttpTransportResult BuildTransportResult(
        HikCentralHttpTransportOutcome outcome,
        bool timedOut,
        bool transportFailure,
        bool vendorUnavailable,
        long started) =>
        new(
            HttpStatusCode: null,
            IsSuccessStatusCode: false,
            outcome,
            timedOut,
            transportFailure,
            vendorUnavailable,
            ResponseBodyTooLarge: false,
            ResponseBodyByteCount: 0,
            ResponseBodySha256: null,
            VendorResultCode: null,
            VendorResultMessage: null,
            VendorCorrelationId: null,
            ElapsedMilliseconds(started),
            _timeProvider.GetUtcNow());

    private static HikCentralHttpTransportOutcome ClassifyStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code >= 200 && code <= 299)
        {
            return HikCentralHttpTransportOutcome.Succeeded;
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => HikCentralHttpTransportOutcome.Unauthorized,
            HttpStatusCode.Forbidden => HikCentralHttpTransportOutcome.Forbidden,
            HttpStatusCode.RequestTimeout => HikCentralHttpTransportOutcome.RequestTimeout,
            (HttpStatusCode)429 => HikCentralHttpTransportOutcome.Throttled,
            _ when code >= 400 && code <= 499 => HikCentralHttpTransportOutcome.ClientError,
            _ when code >= 500 => HikCentralHttpTransportOutcome.VendorFailure,
            _ => HikCentralHttpTransportOutcome.ClientError
        };
    }

    private async Task<BoundedResponseBody> ReadBoundedBodyAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return new BoundedResponseBody([], ByteCount: 0, TooLarge: false);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(4096, _options.MaxResponseBodyBytes)];
        var totalBytes = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > _options.MaxResponseBodyBytes)
            {
                return new BoundedResponseBody([], totalBytes, TooLarge: true);
            }

            buffer.Write(chunk, 0, read);
        }

        return new BoundedResponseBody(buffer.ToArray(), totalBytes, TooLarge: false);
    }

    private static VendorEnvelopeParseResult TryParseVendorEnvelope(byte[] body)
    {
        if (body.Length == 0)
        {
            return new VendorEnvelopeParseResult(Malformed: false, VendorResultCode: null, VendorResultMessage: null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new VendorEnvelopeParseResult(Malformed: true, VendorResultCode: null, VendorResultMessage: null);
            }

            return new VendorEnvelopeParseResult(
                Malformed: false,
                VendorResultCode: TryGetString(root, "code"),
                VendorResultMessage: TryGetString(root, "msg"));
        }
        catch (JsonException)
        {
            return new VendorEnvelopeParseResult(Malformed: true, VendorResultCode: null, VendorResultMessage: null);
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static void ValidateRequest(HttpRequestMessage request)
    {
        if (request is null)
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_REQUIRED", "HikCentral HTTP request is required.");
        }

        var requestUri = request.RequestUri;
        if (requestUri is null || !requestUri.IsAbsoluteUri)
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_URI_ABSOLUTE_REQUIRED", "HikCentral HTTP request URI must be absolute.");
        }

        if (!string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_HTTPS_REQUIRED", "HikCentral HTTP request URI must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(requestUri.UserInfo))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_URI_CREDENTIALS_UNSUPPORTED", "HikCentral HTTP request URI must not include credentials.");
        }

        if (!string.IsNullOrEmpty(requestUri.Query) || !string.IsNullOrEmpty(requestUri.Fragment))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_URI_QUERY_FRAGMENT_UNSUPPORTED", "HikCentral HTTP request URI must not include query or fragment components.");
        }

        if (!string.Equals(requestUri.AbsolutePath, HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_PATH_UNAPPROVED", "HikCentral HTTP request URI path is not approved.");
        }

        if (request.Method != HttpMethod.Post)
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_METHOD_UNSUPPORTED", "HikCentral HTTP request method is unsupported.");
        }

        if (request.Content is null)
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_CONTENT_REQUIRED", "HikCentral HTTP request content is required.");
        }

        if (request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_SECRET_HEADER_UNSUPPORTED", "HikCentral HTTP request contains unsupported secret-bearing headers.");
        }

        foreach (var headerName in RequiredRequestHeaders)
        {
            _ = RequiredHeaderValue(request, headerName);
        }

        ValidateExpectedHeaderValue(
            request,
            HikCentralRequestSigningMaterialConstants.HeaderAccept,
            HikCentralRequestSigningMaterialConstants.Accept);
        ValidateExpectedHeaderValue(
            request,
            HikCentralRequestSigningMaterialConstants.HeaderSignatureMethod,
            HikCentralRequestSigningMaterialConstants.SignatureMethod);
        ValidateExpectedHeaderValue(
            request,
            HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders,
            HikCentralRequestSigningMaterialConstants.SignedHeaderNames);

        ValidateRequiredContentHeader(request.Content, HikCentralRequestSigningMaterialConstants.HeaderContentMd5);

        var contentType = request.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, HikCentralGateActionRequestPlanConstants.JsonContentType, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_CONTENT_TYPE_UNSUPPORTED", "HikCentral HTTP request content type is unsupported.");
        }
    }

    private static void ValidateExpectedHeaderValue(
        HttpRequestMessage request,
        string headerName,
        string expectedValue)
    {
        if (!string.Equals(RequiredHeaderValue(request, headerName), expectedValue, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_HEADER_UNSUPPORTED", "HikCentral HTTP request header value is unsupported.");
        }
    }

    private static string RequiredHeaderValue(HttpRequestMessage request, string headerName)
    {
        if (!request.Headers.TryGetValues(headerName, out var values))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_HEADER_REQUIRED", "Required HikCentral HTTP request header is missing.");
        }

        return ValidateSingleSafeHeaderValue(values);
    }

    private static void ValidateRequiredContentHeader(HttpContent content, string headerName)
    {
        if (!content.Headers.TryGetValues(headerName, out var values))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_CONTENT_HEADER_REQUIRED", "Required HikCentral HTTP request content header is missing.");
        }

        ValidateSingleSafeHeaderValue(values);
    }

    private static string ValidateSingleSafeHeaderValue(IEnumerable<string> values)
    {
        var materialized = values.ToArray();
        if (materialized.Length != 1 || string.IsNullOrWhiteSpace(materialized[0]) || ContainsLineBreak(materialized[0]))
        {
            throw Rejected("HIKCENTRAL_HTTP_REQUEST_HEADER_INVALID", "HikCentral HTTP request header is invalid.");
        }

        return materialized[0];
    }

    private static HikCentralHttpTransportOptions ValidateOptions(HikCentralHttpTransportOptions options)
    {
        if (options.MaxResponseBodyBytes <= 0 ||
            options.MaxResponseBodyBytes > HikCentralHttpTransportOptions.MaximumAllowedResponseBodyBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "HikCentral maximum response body bytes must be positive and bounded.");
        }

        return options;
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static long ElapsedMilliseconds(long started) =>
        Math.Max(0, Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond);

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);

    private sealed record BoundedResponseBody(byte[] Body, int ByteCount, bool TooLarge);

    private sealed record VendorEnvelopeParseResult(
        bool Malformed,
        string? VendorResultCode,
        string? VendorResultMessage);
}
