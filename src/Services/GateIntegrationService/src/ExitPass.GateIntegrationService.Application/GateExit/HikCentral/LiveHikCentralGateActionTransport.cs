using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Live HikCentral HTTP transport. Registration is guarded by explicit live configuration.
/// </summary>
public sealed class LiveHikCentralGateActionTransport : IHikCentralGateActionTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly HikCentralGateActionOptions _options;

    /// <summary>
    /// Creates the live HikCentral HTTP transport.
    /// </summary>
    public LiveHikCentralGateActionTransport(
        HttpClient httpClient,
        HikCentralGateActionOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<HikCentralGateActionTransportResult> SendAsync(
        HikCentralSignedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observedAtUtc = DateTimeOffset.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        try
        {
            using var httpRequest = BuildHttpRequest(request);
            using var response = await _httpClient.SendAsync(httpRequest, timeout.Token);
            observedAtUtc = DateTimeOffset.UtcNow;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            return new HikCentralGateActionTransportResult(
                (int)response.StatusCode,
                ParseEnvelope(body),
                ReadHeader(response.Headers, "X-Ca-Request-Id") ?? ReadHeader(response.Headers, "X-Request-Id"),
                ReadHeader(response.Headers, "X-Correlation-Id"),
                TimedOut: false,
                VendorUnavailable: false,
                TransportError: ResolveTransportError(body),
                observedAtUtc);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HikCentralGateActionTransportResult(
                null,
                null,
                VendorRequestId: null,
                VendorCorrelationId: null,
                TimedOut: true,
                VendorUnavailable: false,
                TransportError: "HikCentral live gate action request timed out.",
                DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException exception)
        {
            return new HikCentralGateActionTransportResult(
                null,
                null,
                VendorRequestId: null,
                VendorCorrelationId: null,
                TimedOut: false,
                VendorUnavailable: true,
                TransportError: exception.Message,
                DateTimeOffset.UtcNow);
        }
        catch (JsonException exception)
        {
            return new HikCentralGateActionTransportResult(
                null,
                null,
                VendorRequestId: null,
                VendorCorrelationId: null,
                TimedOut: false,
                VendorUnavailable: false,
                TransportError: $"HikCentral response JSON was invalid: {exception.Message}",
                DateTimeOffset.UtcNow);
        }
    }

    private static HttpRequestMessage BuildHttpRequest(HikCentralSignedRequest request)
    {
        var message = new HttpRequestMessage(
            new HttpMethod(request.Method),
            request.PathAndQuery)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(request.Body))
        };

        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                continue;
            }

            if (string.Equals(header.Key, "Content-MD5", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                continue;
            }

            if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.Accept.ParseAdd(header.Value);
                continue;
            }

            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return message;
    }

    private static HikCentralGateActionEnvelope? ParseEnvelope(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var code = ReadString(document.RootElement, "code");
        var message = ReadString(document.RootElement, "msg") ?? ReadString(document.RootElement, "message");
        var results = document.RootElement.TryGetProperty("data", out var data)
            ? ReadDoorResults(data)
            : Array.Empty<HikCentralDoorControlResult>();

        return new HikCentralGateActionEnvelope(code, message, results);
    }

    private static IReadOnlyList<HikCentralDoorControlResult> ReadDoorResults(JsonElement data)
    {
        var results = new List<HikCentralDoorControlResult>();
        ReadDoorResults(data, results);
        return results;
    }

    private static void ReadDoorResults(JsonElement element, List<HikCentralDoorControlResult> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var doorIndexCode = ReadString(element, "doorIndexCode");
                if (doorIndexCode is not null)
                {
                    results.Add(new HikCentralDoorControlResult(
                        doorIndexCode,
                        ReadInt(element, "controlResultCode") ?? -1,
                        ReadString(element, "controlResultDesc")));
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ReadDoorResults(item, results);
                }
                break;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind is JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static string? ReadHeader(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static string? ResolveTransportError(string body) =>
        string.IsNullOrWhiteSpace(body)
            ? "HikCentral response body was empty."
            : null;
}
