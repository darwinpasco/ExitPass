using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Read-only HikCentral passageway record client used to build ExitPass-owned projection snapshots.
/// </summary>
public interface IHikCentralPassagewayRecordClient
{
    /// <summary>
    /// Pulls one page of HikCentral passageway records.
    /// </summary>
    Task<HikCentralPassagewayRecordPage> GetPassagewayRecordsAsync(
        HikCentralPassagewayRecordRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request for one HikCentral passageway record page.
/// </summary>
public sealed record HikCentralPassagewayRecordRequest(
    string ParkingLotIndexCode,
    DateTimeOffset BeginTime,
    DateTimeOffset EndTime,
    int PageIndex,
    int PageSize,
    Guid CorrelationId);

/// <summary>
/// One page of HikCentral passageway records.
/// </summary>
public sealed record HikCentralPassagewayRecordPage(
    HttpStatusCode HttpStatusCode,
    string? Code,
    string? Message,
    int PageIndex,
    int PageSize,
    int? Total,
    IReadOnlyList<HikCentralPassagewayRecord> Records);

/// <summary>
/// HikCentral passageway record fields relevant to an ExitPass continuity projection.
/// </summary>
public sealed record HikCentralPassagewayRecord(
    [property: JsonPropertyName("guid")] string? Guid,
    [property: JsonPropertyName("parkingLotInfo")] HikCentralNamedIndex? ParkingLotInfo,
    [property: JsonPropertyName("passagewayInfo")] HikCentralNamedIndex? PassagewayInfo,
    [property: JsonPropertyName("laneInfo")] HikCentralLaneInfo? LaneInfo,
    [property: JsonPropertyName("personInfo")] HikCentralPersonInfo? PersonInfo,
    [property: JsonPropertyName("carInfo")] HikCentralCarInfo? CarInfo,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("enterTime")] string? EnterTime,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("exitTime")] string? ExitTime,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("allowType")] string? AllowType,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("allowResult")] string? AllowResult);

/// <summary>
/// HikCentral index/name pair.
/// </summary>
public sealed record HikCentralNamedIndex(
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("indexCode")] string? IndexCode,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("parkingLotIndexCode")] string? ParkingLotIndexCode = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("parkingLotName")] string? ParkingLotName = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("passagewayIndexCode")] string? PassagewayIndexCode = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("passagewayName")] string? PassagewayName = null);

/// <summary>
/// HikCentral lane information.
/// </summary>
public sealed record HikCentralLaneInfo(
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("indexCode")] string? IndexCode,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("laneDirection")] string? LaneDirection,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("laneIndexCode")] string? LaneIndexCode = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("laneName")] string? LaneName = null);

/// <summary>
/// HikCentral person/card information.
/// </summary>
public sealed record HikCentralPersonInfo(
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("cardNum")] string? CardNum,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("ownerName")] string? OwnerName,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("ownerPhoneNum")] string? OwnerPhoneNum);

/// <summary>
/// HikCentral vehicle information.
/// </summary>
public sealed record HikCentralCarInfo(
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("plateLicense")] string? PlateLicense,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("carType")] string? CarType = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("ImageUrl")] string? ImageUrl = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("EnterTime")] string? EnterTime = null,
    [property: JsonConverter(typeof(HikCentralFlexibleStringJsonConverter))]
    [property: JsonPropertyName("ExitTime")] string? ExitTime = null);

/// <summary>
/// Signed HikCentral client for the read-only passageway record API.
/// </summary>
public sealed class HikCentralPassagewayRecordClient : IHikCentralPassagewayRecordClient
{
    /// <summary>
    /// HikCentral V3.1.0 passageway record endpoint path.
    /// </summary>
    public const string PassagewayRecordPath = "/artemis/api/vehicle/v1/parkinglot/passageway/record";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IHikCentralRequestSigner _requestSigner;
    private readonly string _userId;
    private readonly ILogger<HikCentralPassagewayRecordClient> _logger;

    /// <summary>
    /// Creates a signed HikCentral passageway record client.
    /// </summary>
    public HikCentralPassagewayRecordClient(
        HttpClient httpClient,
        IHikCentralRequestSigner requestSigner,
        string userId = "exitpass-adapter",
        ILogger<HikCentralPassagewayRecordClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        _userId = string.IsNullOrWhiteSpace(userId) ? "exitpass-adapter" : userId.Trim();
        _logger = logger ?? NullLogger<HikCentralPassagewayRecordClient>.Instance;
    }

    /// <inheritdoc />
    public async Task<HikCentralPassagewayRecordPage> GetPassagewayRecordsAsync(
        HikCentralPassagewayRecordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ParkingLotIndexCode))
        {
            throw new ArgumentException("ParkingLotIndexCode is required.", nameof(request));
        }

        if (request.PageIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PageIndex must be at least 1.");
        }

        if (request.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PageSize must be between 1 and 500.");
        }

        var body = new PassagewayRecordBody(
            request.PageIndex,
            request.PageSize,
            new PassagewayRecordQueryInfo(
                request.ParkingLotIndexCode.Trim(),
                HikCentralParkingTimeFormatter.Format(request.BeginTime),
                HikCentralParkingTimeFormatter.Format(request.EndTime),
                DirectionType: -1,
                AllowResult: -1,
                SortField: "EnterTime",
                OrderType: 1));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, PassagewayRecordPath)
        {
            Content = CreateJsonContent(body)
        };
        httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", request.CorrelationId.ToString());
        httpRequest.Headers.TryAddWithoutValidation("userId", _userId);
        await _requestSigner.SignAsync(httpRequest, cancellationToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        var page = MapResponse(response.StatusCode, responseBody, request.PageIndex, request.PageSize);
        _logger.LogInformation(
            "HikCentral passageway projection pull completed. endpoint={EndpointPath} correlation_id={CorrelationId} page_index={PageIndex} page_size={PageSize} http_status={HttpStatus} hikcentral_code={HikCentralCode} item_count={ItemCount}",
            PassagewayRecordPath,
            request.CorrelationId,
            request.PageIndex,
            request.PageSize,
            (int)response.StatusCode,
            page.Code,
            page.Records.Count);

        return page;
    }

    private static HikCentralPassagewayRecordPage MapResponse(
        HttpStatusCode statusCode,
        string body,
        int pageIndex,
        int pageSize)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new HikCentralPassagewayRecordPage(statusCode, null, null, pageIndex, pageSize, null, []);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var code = TryGetString(root, "code");
        var message = TryGetString(root, "msg");
        var data = root.TryGetProperty("data", out var dataElement) ? dataElement : root;
        var total = TryGetInt32(data, "total") ?? TryGetInt32(data, "totalCount");
        var records = ExtractRecords(data)
            .Select(record => record.Deserialize<HikCentralPassagewayRecord>(JsonOptions))
            .Where(record => record is not null)
            .Cast<HikCentralPassagewayRecord>()
            .ToArray();

        return new HikCentralPassagewayRecordPage(statusCode, code, message, pageIndex, pageSize, total, records);
    }

    private static IReadOnlyList<JsonElement> ExtractRecords(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Array)
        {
            return data.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        if (data.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        foreach (var propertyName in new[] { "list", "records", "rows" })
        {
            if (data.TryGetProperty(propertyName, out var list) &&
                list.ValueKind is JsonValueKind.Array)
            {
                return list.EnumerateArray().Select(item => item.Clone()).ToArray();
            }
        }

        return [data.Clone()];
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
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

    private sealed record PassagewayRecordBody(
        [property: JsonPropertyName("pageIndex")] int PageIndex,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("queryInfo")] PassagewayRecordQueryInfo QueryInfo);

    private sealed record PassagewayRecordQueryInfo(
        [property: JsonPropertyName("parkingLotIndexCode")] string ParkingLotIndexCode,
        [property: JsonPropertyName("beginTime")] string BeginTime,
        [property: JsonPropertyName("endTime")] string EndTime,
        [property: JsonPropertyName("directionType")] int DirectionType,
        [property: JsonPropertyName("allowResult")] int AllowResult,
        [property: JsonPropertyName("sortField")] string SortField,
        [property: JsonPropertyName("orderType")] int OrderType);
}

internal sealed class HikCentralFlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => null
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
