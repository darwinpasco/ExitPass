using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Prepares HikCentral Professional OpenAPI V3.1.0 AK/SK signed door-control requests without sending them.
/// </summary>
public sealed class HikCentralRequestSigner
{
    /// <summary>
    /// Door control endpoint from HikCentral Professional OpenAPI V3.1.0 section 5.9.1.
    /// </summary>
    public const string DoorControlPath = "/artemis/api/acs/v1/door/doControl";

    private const string Accept = "*/*";
    private const string ContentType = "application/json";
    private const string SignedHeaderNames = "x-ca-key,x-ca-nonce,x-ca-timestamp";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HikCentralGateActionOptions _options;
    private readonly IHikCentralClock _clock;
    private readonly IHikCentralNonceProvider _nonceProvider;

    /// <summary>
    /// Creates a signer for deterministic HikCentral signed request preparation.
    /// </summary>
    public HikCentralRequestSigner(
        HikCentralGateActionOptions options,
        IHikCentralClock? clock = null,
        IHikCentralNonceProvider? nonceProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? new SystemHikCentralClock();
        _nonceProvider = nonceProvider ?? new GuidHikCentralNonceProvider();
    }

    /// <summary>
    /// Builds and signs the HikCentral door-control request body for a gate action.
    /// </summary>
    public HikCentralSignedRequest SignDoorControlRequest(HikCentralGateActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_options.AppKey))
        {
            throw new InvalidOperationException("HIKCENTRAL_APP_KEY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new InvalidOperationException("HIKCENTRAL_APP_SECRET_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(_options.UserId))
        {
            throw new InvalidOperationException("HIKCENTRAL_USER_ID_REQUIRED");
        }

        var body = JsonSerializer.Serialize(
            new HikCentralDoorControlRequestBody(
                [request.DoorIndexCode],
                (int)request.ControlType,
                (int)request.ControlDirection),
            JsonOptions);

        return Sign("POST", DoorControlPath, body);
    }

    /// <summary>
    /// Builds a signed request for deterministic canonical signing tests.
    /// </summary>
    public HikCentralSignedRequest Sign(
        string method,
        string pathAndQuery,
        string body)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("HTTP method is required.", nameof(method));
        }

        if (string.IsNullOrWhiteSpace(pathAndQuery) || !pathAndQuery.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A HikCentral path beginning with '/' is required.", nameof(pathAndQuery));
        }

        body ??= string.Empty;

        var timestamp = _clock.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = _nonceProvider.CreateNonce();
        var contentMd5 = CalculateMd5(body);
        var signedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-ca-key"] = _options.AppKey ?? throw new InvalidOperationException("HIKCENTRAL_APP_KEY_REQUIRED"),
            ["x-ca-nonce"] = nonce,
            ["x-ca-timestamp"] = timestamp
        };
        var canonical = BuildCanonicalRequest(
            method.ToUpperInvariant(),
            pathAndQuery,
            Accept,
            contentMd5,
            ContentType,
            signedHeaders);
        var signature = CalculateSignature(canonical.StringToSign, _options.AppSecret ?? throw new InvalidOperationException("HIKCENTRAL_APP_SECRET_REQUIRED"));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = Accept,
            ["Content-MD5"] = contentMd5,
            ["Content-Type"] = ContentType,
            ["userId"] = _options.UserId ?? string.Empty,
            ["X-Ca-Key"] = signedHeaders["x-ca-key"],
            ["X-Ca-Nonce"] = nonce,
            ["X-Ca-Timestamp"] = timestamp,
            ["X-Ca-Signature-Headers"] = SignedHeaderNames,
            ["X-Ca-Signature"] = signature
        };

        return new HikCentralSignedRequest(
            method.ToUpperInvariant(),
            pathAndQuery,
            body,
            HikCentralSignedRequest.HeadersOf(headers),
            canonical,
            signature);
    }

    /// <summary>
    /// Builds the canonical HikCentral signature input.
    /// </summary>
    public static HikCentralCanonicalRequest BuildCanonicalRequest(
        string method,
        string pathAndQuery,
        string accept,
        string contentMd5,
        string contentType,
        IReadOnlyDictionary<string, string> signedHeaders)
    {
        ArgumentNullException.ThrowIfNull(signedHeaders);

        var normalizedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in signedHeaders)
        {
            normalizedHeaders[header.Key.ToLowerInvariant()] = header.Value.Trim();
        }

        var builder = new StringBuilder();
        builder.Append(method.ToUpperInvariant()).Append('\n');
        builder.Append(accept).Append('\n');
        builder.Append(contentMd5).Append('\n');
        builder.Append(contentType).Append('\n');

        foreach (var header in normalizedHeaders)
        {
            builder.Append(header.Key).Append(':').Append(header.Value).Append('\n');
        }

        builder.Append(pathAndQuery);

        return new HikCentralCanonicalRequest(
            method.ToUpperInvariant(),
            pathAndQuery,
            accept,
            contentMd5,
            contentType,
            normalizedHeaders,
            builder.ToString());
    }

    private static string CalculateMd5(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return Convert.ToBase64String(MD5.HashData(bytes));
    }

    private static string CalculateSignature(string stringToSign, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

    private sealed record HikCentralDoorControlRequestBody(
        [property: JsonPropertyName("doorIndexCodes")] IReadOnlyList<string> DoorIndexCodes,
        [property: JsonPropertyName("controlType")] int ControlType,
        [property: JsonPropertyName("controlDirection")] int ControlDirection);
}
