using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// HikCentral Professional OpenAPI V3.1.0 AK/SK request signer.
/// </summary>
/// <remarks>
/// Implements the canonical string described by section 3.2, Signature and Authentication.
/// </remarks>
public sealed class HikCentralRequestSigner : IHikCentralRequestSigner
{
    private const string SignedHeaderNames = "x-ca-key,x-ca-timestamp";
    private readonly HikCentralCredentialOptions _credentials;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the HikCentral AK/SK request signer.
    /// </summary>
    /// <param name="credentials">HikCentral app key and secret key.</param>
    /// <param name="utcNow">Optional deterministic clock for tests.</param>
    public HikCentralRequestSigner(HikCentralCredentialOptions credentials, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (string.IsNullOrWhiteSpace(credentials.AccessKey))
        {
            throw new InvalidOperationException("HikCentral access key is required.");
        }

        if (string.IsNullOrWhiteSpace(credentials.SecretKey))
        {
            throw new InvalidOperationException("HikCentral secret key is required.");
        }

        _credentials = credentials;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task SignAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureStandardHeaders(request);
        await EnsureContentMd5Async(request, cancellationToken);

        var timestamp = _utcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        request.Headers.Remove("X-Ca-Key");
        request.Headers.Remove("X-Ca-Timestamp");
        request.Headers.Remove("X-Ca-Signature-Headers");
        request.Headers.Remove("X-Ca-Signature");
        request.Headers.TryAddWithoutValidation("X-Ca-Key", _credentials.AccessKey);
        request.Headers.TryAddWithoutValidation("X-Ca-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Ca-Signature-Headers", SignedHeaderNames);

        var stringToSign = BuildStringToSign(request);
        var signature = CalculateSignature(stringToSign, _credentials.SecretKey);
        request.Headers.TryAddWithoutValidation("X-Ca-Signature", signature);
    }

    /// <summary>
    /// Builds the canonical signature string for a prepared HikCentral request.
    /// </summary>
    /// <param name="request">Request containing the headers to sign.</param>
    /// <returns>The canonical string used as HMAC input.</returns>
    public static string BuildStringToSign(HttpRequestMessage request)
    {
        var builder = new StringBuilder();
        builder.Append(request.Method.Method.ToUpperInvariant()).Append('\n');
        AppendHeaderValue(builder, request.Headers.Accept);
        AppendContentHeaderValue(builder, request.Content?.Headers, "Content-MD5");
        AppendContentHeaderValue(builder, request.Content?.Headers, "Content-Type");
        AppendHeaderValue(builder, request.Headers.Date);
        AppendSignedHeader(builder, request, "x-ca-key");
        AppendSignedHeader(builder, request, "x-ca-timestamp");
        builder.Append(BuildUri(request));
        return builder.ToString();
    }

    private static void EnsureStandardHeaders(HttpRequestMessage request)
    {
        if (!request.Headers.Accept.Any())
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }
    }

    private static async Task EnsureContentMd5Async(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Content.Headers.Contains("Content-MD5"))
        {
            return;
        }

        var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var digest = MD5.HashData(body);
        request.Content.Headers.TryAddWithoutValidation("Content-MD5", Convert.ToBase64String(digest));
    }

    private static void AppendHeaderValue(StringBuilder builder, HttpHeaderValueCollection<MediaTypeWithQualityHeaderValue> values)
    {
        if (values.Any())
        {
            builder.Append(string.Join(",", values.Select(value => value.ToString()))).Append('\n');
        }
    }

    private static void AppendHeaderValue(StringBuilder builder, DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            builder.Append(value.Value.ToString("r", CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static void AppendContentHeaderValue(
        StringBuilder builder,
        HttpContentHeaders? headers,
        string headerName)
    {
        if (headers?.TryGetValues(headerName, out var values) == true)
        {
            builder.Append(string.Join(",", values)).Append('\n');
        }
    }

    private static void AppendSignedHeader(
        StringBuilder builder,
        HttpRequestMessage request,
        string headerName)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            builder.Append(headerName).Append(':').Append(string.Join(",", values).Trim()).Append('\n');
        }
    }

    private static string BuildUri(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            return "/";
        }

        return request.RequestUri.IsAbsoluteUri
            ? request.RequestUri.PathAndQuery
            : request.RequestUri.OriginalString;
    }

    private static string CalculateSignature(string stringToSign, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(digest);
    }
}
