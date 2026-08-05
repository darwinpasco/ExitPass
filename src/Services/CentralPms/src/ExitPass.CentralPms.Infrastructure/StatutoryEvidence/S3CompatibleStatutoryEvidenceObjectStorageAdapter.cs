using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.StatutoryEvidence;

public sealed class S3CompatibleStatutoryEvidenceObjectStorageAdapter : IStatutoryEvidenceProtectedObjectStorageAdapter
{
    private const string ServiceName = "s3";
    private const string Algorithm = "AWS4-HMAC-SHA256";

    private readonly HttpClient _httpClient;
    private readonly StatutoryEvidenceUploadOptions _options;

    public S3CompatibleStatutoryEvidenceObjectStorageAdapter(
        HttpClient httpClient,
        IOptions<StatutoryEvidenceUploadOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(
        StatutoryEvidenceObjectUploadAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(publicEndpoint: true);
        var now = DateTimeOffset.UtcNow;
        var expires = Math.Max(1, (int)Math.Floor((request.ExpiresAt - now).TotalSeconds));
        var providerChecksum = ToS3ChecksumHeader(request.ChecksumSha256);
        var url = BuildPresignedUrl(
            HttpMethod.Put.Method,
            endpoint,
            request.BucketName,
            request.InternalObjectKey,
            now,
            expires,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["content-type"] = request.ContentType,
                ["x-amz-checksum-sha256"] = providerChecksum
            });

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = request.ContentType,
            ["x-amz-checksum-sha256"] = providerChecksum
        };

        return Task.FromResult(new StatutoryEvidenceObjectUploadAuthorization(url, headers));
    }

    public async Task<StatutoryEvidenceObjectUploadResult> UploadObjectAsync(
        StatutoryEvidenceObjectUploadRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(publicEndpoint: false);
        var uri = BuildObjectUri(endpoint, request.BucketName, request.InternalObjectKey);
        using var message = new HttpRequestMessage(HttpMethod.Put, uri);
        var providerChecksum = ToS3ChecksumHeader(request.ChecksumSha256);
        message.Content = new StreamContent(request.Content);
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        message.Content.Headers.ContentLength = request.ContentLength;
        SignHeaderAuthorization(
            message,
            request.BucketName,
            request.InternalObjectKey,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["content-type"] = request.ContentType,
                ["x-amz-checksum-sha256"] = providerChecksum,
                ["x-amz-content-sha256"] = "UNSIGNED-PAYLOAD"
            });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("Protected storage upload failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Protected storage upload failed.", exception);
        }

        using (response)
        {
            return response.IsSuccessStatusCode
                ? new StatutoryEvidenceObjectUploadResult("ACCEPTED", false)
                : new StatutoryEvidenceObjectUploadResult("PROVIDER_UNAVAILABLE", true);
        }
    }

    public async Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(
        StatutoryEvidenceObjectMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(publicEndpoint: false);
        var uri = BuildObjectUri(endpoint, request.BucketName, request.InternalObjectKey);
        using var message = new HttpRequestMessage(HttpMethod.Head, uri);
        SignHeaderAuthorization(message, request.BucketName, request.InternalObjectKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("Protected storage metadata verification failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Protected storage metadata verification failed.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Protected storage metadata verification failed.");
            }

            response.Content.Headers.TryGetValues("x-amz-checksum-sha256", out var checksumValues);
            if (checksumValues is null)
            {
                response.Headers.TryGetValues("x-amz-checksum-sha256", out checksumValues);
            }

            response.Headers.TryGetValues("x-amz-version-id", out var versionValues);
            response.Headers.TryGetValues("x-amz-server-side-encryption", out var encryptionValues);

            return new StatutoryEvidenceObjectMetadata(
                response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                response.Content.Headers.ContentLength ?? 0,
                NormalizeS3ChecksumHeader(checksumValues?.FirstOrDefault()),
                versionValues?.FirstOrDefault(),
                encryptionValues?.FirstOrDefault());
        }
    }

    public async Task<StatutoryEvidenceObjectContent> GetObjectContentAsync(
        StatutoryEvidenceObjectContentRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(publicEndpoint: false);
        var uri = BuildObjectUri(endpoint, request.BucketName, request.InternalObjectKey);
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        SignHeaderAuthorization(message, request.BucketName, request.InternalObjectKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("Protected storage object retrieval failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Protected storage object retrieval failed.", exception);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new InvalidOperationException("Protected storage object is unavailable.");
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException("Protected storage object retrieval failed.");
        }

        var contentLength = response.Content.Headers.ContentLength ?? 0;
        if (contentLength <= 0 || contentLength > request.MaxContentLengthBytes)
        {
            response.Dispose();
            throw new InvalidOperationException("Protected storage object length is outside the configured limit.");
        }

        response.Content.Headers.TryGetValues("x-amz-checksum-sha256", out var checksumValues);
        if (checksumValues is null)
        {
            response.Headers.TryGetValues("x-amz-checksum-sha256", out checksumValues);
        }

        response.Headers.TryGetValues("x-amz-version-id", out var versionValues);
        response.Headers.TryGetValues("x-amz-server-side-encryption", out var encryptionValues);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        await using var providerStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var memory = new MemoryStream(capacity: (int)Math.Min(contentLength, int.MaxValue));
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await providerStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > request.MaxContentLengthBytes)
            {
                response.Dispose();
                await memory.DisposeAsync();
                throw new InvalidOperationException("Protected storage object length is outside the configured limit.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        memory.Position = 0;
        response.Dispose();
        return new StatutoryEvidenceObjectContent(
            memory,
            contentType,
            total,
            NormalizeS3ChecksumHeader(checksumValues?.FirstOrDefault()),
            versionValues?.FirstOrDefault(),
            encryptionValues?.FirstOrDefault());
    }

    private Uri BuildPresignedUrl(
        string method,
        Uri endpoint,
        string bucketName,
        string objectKey,
        DateTimeOffset now,
        int expiresSeconds,
        SortedDictionary<string, string> signedHeaders)
    {
        var accessKey = Require(_options.AccessKeyId, "Evidence storage access key is not configured.");
        var region = string.IsNullOrWhiteSpace(_options.Region) ? "us-east-1" : _options.Region!;
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{region}/{ServiceName}/aws4_request";
        var credential = $"{accessKey}/{scope}";
        var host = endpoint.IsDefaultPort ? endpoint.Host : $"{endpoint.Host}:{endpoint.Port}";
        var canonicalHeaders = new SortedDictionary<string, string>(signedHeaders, StringComparer.Ordinal)
        {
            ["host"] = host
        };
        var signedHeaderNames = string.Join(';', canonicalHeaders.Keys);
        var path = CanonicalPath(bucketName, objectKey);

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = credential,
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = expiresSeconds.ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = signedHeaderNames
        };

        var canonicalHeadersBuilder = new StringBuilder();
        foreach (var header in canonicalHeaders)
        {
            canonicalHeadersBuilder.Append(header.Key).Append(':').Append(header.Value.Trim()).Append('\n');
        }

        var canonicalRequest = string.Join('\n',
        [
            method,
            path,
            CanonicalQuery(query),
            canonicalHeadersBuilder.ToString(),
            signedHeaderNames,
            "UNSIGNED-PAYLOAD"
        ]);
        var stringToSign = string.Join('\n',
        [
            Algorithm,
            amzDate,
            scope,
            Sha256Hex(canonicalRequest)
        ]);
        query["X-Amz-Signature"] = ToHex(Hmac(SigningKey(date, region), stringToSign));
        return new Uri(endpoint, $"{path}?{CanonicalQuery(query)}");
    }

    private void SignHeaderAuthorization(HttpRequestMessage message, string bucketName, string objectKey)
    {
        SignHeaderAuthorization(
            message,
            bucketName,
            objectKey,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["x-amz-checksum-mode"] = "ENABLED",
                ["x-amz-content-sha256"] = "UNSIGNED-PAYLOAD"
            });
    }

    private void SignHeaderAuthorization(
        HttpRequestMessage message,
        string bucketName,
        string objectKey,
        SortedDictionary<string, string> signedHeaders)
    {
        var now = DateTimeOffset.UtcNow;
        var region = string.IsNullOrWhiteSpace(_options.Region) ? "us-east-1" : _options.Region!;
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{region}/{ServiceName}/aws4_request";
        var host = message.RequestUri!.IsDefaultPort ? message.RequestUri.Host : $"{message.RequestUri.Host}:{message.RequestUri.Port}";
        message.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        foreach (var header in signedHeaders)
        {
            if (header.Key.Equals("content-type", StringComparison.Ordinal))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var canonicalHeaders = new SortedDictionary<string, string>(signedHeaders, StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-date"] = amzDate
        };
        var canonicalHeadersBuilder = new StringBuilder();
        foreach (var header in canonicalHeaders)
        {
            canonicalHeadersBuilder.Append(header.Key).Append(':').Append(header.Value.Trim()).Append('\n');
        }

        var signedHeaderNames = string.Join(';', canonicalHeaders.Keys);
        var canonicalRequest = string.Join('\n',
        [
            message.Method.Method,
            CanonicalPath(bucketName, objectKey),
            string.Empty,
            canonicalHeadersBuilder.ToString(),
            signedHeaderNames,
            "UNSIGNED-PAYLOAD"
        ]);
        var stringToSign = string.Join('\n',
        [
            Algorithm,
            amzDate,
            scope,
            Sha256Hex(canonicalRequest)
        ]);
        var signature = ToHex(Hmac(SigningKey(date, region), stringToSign));
        message.Headers.TryAddWithoutValidation(
            "Authorization",
            $"{Algorithm} Credential={Require(_options.AccessKeyId, "Evidence storage access key is not configured.")}/{scope}, SignedHeaders={signedHeaderNames}, Signature={signature}");
    }

    private Uri ResolveEndpoint(bool publicEndpoint)
    {
        var value = publicEndpoint && !string.IsNullOrWhiteSpace(_options.PublicUploadEndpoint)
            ? _options.PublicUploadEndpoint
            : _options.Endpoint;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Evidence storage endpoint is not configured.");
        }

        return endpoint;
    }

    private static Uri BuildObjectUri(Uri endpoint, string bucketName, string objectKey) =>
        new(endpoint, CanonicalPath(bucketName, objectKey));

    private static string CanonicalPath(string bucketName, string objectKey) =>
        "/" + Uri.EscapeDataString(bucketName).Replace("%2F", "/", StringComparison.Ordinal) +
        "/" + string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));

    private static string CanonicalQuery(SortedDictionary<string, string> values) =>
        string.Join('&', values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private byte[] SigningKey(string date, string region)
    {
        var secret = Require(_options.SecretAccessKey, "Evidence storage secret is not configured.");
        var kDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), date);
        var kRegion = Hmac(kDate, region);
        var kService = Hmac(kRegion, ServiceName);
        return Hmac(kService, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string Sha256Hex(string value) =>
        ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ToHex(byte[] value) =>
        Convert.ToHexString(value).ToLowerInvariant();

    private static string ToS3ChecksumHeader(string sha256Hex) =>
        Convert.ToBase64String(Convert.FromHexString(sha256Hex));

    private static string? NormalizeS3ChecksumHeader(string? checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
        {
            return null;
        }

        try
        {
            return ToHex(Convert.FromBase64String(checksum));
        }
        catch (FormatException)
        {
            return checksum;
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value;
}
