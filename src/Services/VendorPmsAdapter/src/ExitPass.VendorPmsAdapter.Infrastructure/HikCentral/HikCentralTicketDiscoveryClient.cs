using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Read-only HikCentral ticket discovery client for local equipment diagnostics.
/// </summary>
public sealed class HikCentralTicketDiscoveryClient
{
    public const string VersionPath = "/artemis/api/common/v1/version";
    public const string ParkingLotListPath = "/artemis/api/vehicle/v1/parkinglot/list";
    public const string ParkingFeeCalculatePath = "/artemis/api/vehicle/v1/parkingfee/calculate";
    public const string PassagewayRecordPath = "/artemis/api/vehicle/v1/parkinglot/passageway/record";
    public const string ParkingSpaceRecordPath = "/artemis/api/vehicle/v1/parkingspace/record";
    public const string CrossRecordsPagePath = "/artemis/api/pms/v1/crossRecords/page";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> CandidateFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cardNum",
        "ticketNo",
        "ticketNumber",
        "serialNo",
        "parkingSerial",
        "parkingSpaceSerial",
        "crossRecordSyscode",
        "guid",
        "recordId",
        "barcode",
        "barcodePayload",
        "qr",
        "qrPayload"
    };

    private static readonly HashSet<string> DiagnosticFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "plateLicense",
        "passagewayIndexCode",
        "laneIndexCode",
        "parkingInTime",
        "parkingDuration",
        "inTime",
        "outTime",
        "enterTime",
        "exitTime",
        "parkingLotIndexCode",
        "parkingLotName"
    };

    private readonly HttpClient _httpClient;
    private readonly IHikCentralRequestSigner _requestSigner;

    public HikCentralTicketDiscoveryClient(
        HttpClient httpClient,
        IHikCentralRequestSigner requestSigner)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
    }

    public Task<HikCentralReadOnlyEndpointResult> GetVersionAsync(CancellationToken cancellationToken) =>
        SendReadOnlyPostAsync(VersionPath, new { }, cancellationToken);

    public Task<HikCentralReadOnlyEndpointResult> ListParkingLotsAsync(CancellationToken cancellationToken) =>
        SendReadOnlyPostAsync(ParkingLotListPath, new { pageNo = 1, pageSize = 100 }, cancellationToken);

    public async Task<HikCentralTicketDiscoveryResult> DiscoverTicketAsync(
        HikCentralTicketDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpointSummaries = new List<HikCentralEndpointSummary>();
        var calculate = await SendReadOnlyPostAsync(
            ParkingFeeCalculatePath,
            new { cardNum = request.PrintedTicketNumber },
            cancellationToken);
        endpointSummaries.Add(calculate.ToSummary(request.PrintedTicketNumber));

        if (calculate.Code is "0")
        {
            return BuildCardNumAcceptedResult(request, calculate, endpointSummaries);
        }

        if (calculate.Code is not ("128" or "404" or "0x00072002") &&
            calculate.Message?.Contains("not exist", StringComparison.OrdinalIgnoreCase) != true &&
            calculate.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) != true)
        {
            return BuildNoCandidateResult(request, calculate, endpointSummaries);
        }

        CandidateIdentifier? discoveredCandidate = null;
        string? discoveredEndpointPath = null;
        JsonElement? discoveredRoot = null;
        foreach (var endpoint in BuildRecordEndpointRequests(request))
        {
            if (endpoint.SkipReason is not null)
            {
                endpointSummaries.Add(HikCentralEndpointSummary.Skipped(endpoint.Path, endpoint.SkipReason));
                continue;
            }

            var recordResult = await SendReadOnlyPostAsync(endpoint.Path, endpoint.Body!, cancellationToken);
            var candidate = FindCandidate(recordResult.Root, request.PrintedTicketNumber);
            endpointSummaries.Add(recordResult.ToSummary(request.PrintedTicketNumber, candidate is not null));
            if (candidate is not null && discoveredCandidate is null)
            {
                discoveredCandidate = candidate;
                discoveredEndpointPath = endpoint.Path;
                discoveredRoot = recordResult.Root;
            }
        }

        if (discoveredCandidate is not null)
        {
            return new HikCentralTicketDiscoveryResult(
                request.PrintedTicketNumber,
                CardNumAccepted: false,
                discoveredCandidate.IdentifierType,
                discoveredCandidate.IdentifierValue,
                discoveredEndpointPath,
                request.ParkingLotIndexCode,
                FindString(discoveredRoot, "passagewayIndexCode"),
                FindString(discoveredRoot, "laneIndexCode"),
                FindString(discoveredRoot, "plateLicense"),
                Fee: null,
                ParkingInTime: FindString(discoveredRoot, "parkingInTime"),
                ParkingDuration: FindString(discoveredRoot, "parkingDuration"),
                calculate.Code,
                calculate.Message,
                endpointSummaries,
                "Printed ticket number was not accepted as cardNum, but a matching read-only record identifier was found.");
        }

        return BuildNoCandidateResult(request, calculate, endpointSummaries);
    }

    internal static bool FindCandidateForSummary(JsonElement? root, string ticketNumber) =>
        FindCandidate(root, ticketNumber) is not null;

    internal static string BuildEndpointOutcomeForSummary(
        HttpStatusCode httpStatusCode,
        string? code,
        int itemCount,
        bool matchedTicketIdentifier,
        bool ticketSearchApplied) =>
        BuildEndpointOutcome(httpStatusCode, code, itemCount, matchedTicketIdentifier, ticketSearchApplied);

    internal static IReadOnlyList<string> BuildSanitizedRecordSamplesForSummary(JsonElement? root) =>
        BuildSanitizedRecordSamples(root);

    private async Task<HikCentralReadOnlyEndpointResult> SendReadOnlyPostAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = CreateJsonContent(body)
        };
        await _requestSigner.SignAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = TryParse(responseBody);
        var root = document?.RootElement.Clone();

        return new HikCentralReadOnlyEndpointResult(
            path,
            response.StatusCode,
            ReadCode(root),
            ReadMessage(root),
            root,
            CountItems(root));
    }

    private static StringContent CreateJsonContent(object value)
    {
        var content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static IReadOnlyList<RecordEndpointRequest> BuildRecordEndpointRequests(
        HikCentralTicketDiscoveryRequest request)
    {
        var beginTime = HikCentralParkingTimeFormatter.Format(request.BeginTime);
        var endTime = HikCentralParkingTimeFormatter.Format(request.EndTime);
        var queryInfo = new
        {
            parkingLotIndexCode = request.ParkingLotIndexCode,
            beginTime,
            endTime
        };

        var recordEndpoints = new List<RecordEndpointRequest>
        {
            new(
                PassagewayRecordPath,
                new
                {
                    pageIndex = 1,
                    pageSize = 50,
                    queryInfo
                }),
            new(
                ParkingSpaceRecordPath,
                new
                {
                    pageIndex = 1,
                    pageSize = 50,
                    queryInfo
                })
        };

        recordEndpoints.Add(string.IsNullOrWhiteSpace(request.CameraIndexCode)
            ? new RecordEndpointRequest(
                CrossRecordsPagePath,
                Body: null,
                "skipped, missing HIKCENTRAL_TEST_CAMERA_INDEX_CODE")
            : new RecordEndpointRequest(
                CrossRecordsPagePath,
                new
                {
                    cameraIndexCode = request.CameraIndexCode,
                    startTime = beginTime,
                    endTime,
                    pageNo = 1,
                    pageSize = 50
                }));

        return recordEndpoints;
    }

    private static HikCentralTicketDiscoveryResult BuildCardNumAcceptedResult(
        HikCentralTicketDiscoveryRequest request,
        HikCentralReadOnlyEndpointResult calculate,
        IReadOnlyList<HikCentralEndpointSummary> endpointSummaries) =>
        new(
            request.PrintedTicketNumber,
            CardNumAccepted: true,
            "cardNum",
            request.PrintedTicketNumber,
            ParkingFeeCalculatePath,
            request.ParkingLotIndexCode,
            FindString(calculate.Root, "passagewayIndexCode"),
            FindString(calculate.Root, "laneIndexCode"),
            FindString(calculate.Root, "plateLicense"),
            FindString(calculate.Root, "fee"),
            FindString(calculate.Root, "parkingInTime"),
            FindString(calculate.Root, "parkingDuration"),
            calculate.Code,
            calculate.Message,
            endpointSummaries,
            "Printed ticket number was accepted as HikCentral cardNum.");

    private static HikCentralTicketDiscoveryResult BuildNoCandidateResult(
        HikCentralTicketDiscoveryRequest request,
        HikCentralReadOnlyEndpointResult calculate,
        IReadOnlyList<HikCentralEndpointSummary> endpointSummaries) =>
        new(
            request.PrintedTicketNumber,
            CardNumAccepted: false,
            DiscoveredIdentifierType: null,
            DiscoveredIdentifierValue: null,
            EndpointSource: null,
            request.ParkingLotIndexCode,
            PassagewayIndexCode: null,
            LaneIndexCode: null,
            PlateLicense: null,
            Fee: null,
            ParkingInTime: null,
            ParkingDuration: null,
            calculate.Code,
            calculate.Message,
            endpointSummaries,
            "Printed ticket number was not accepted as cardNum and no matching read-only record identifier was found.");

    private static CandidateIdentifier? FindCandidate(JsonElement? root, string ticketNumber)
    {
        if (root is null)
        {
            return null;
        }

        return FindCandidate(root.Value, Normalize(ticketNumber));
    }

    private static CandidateIdentifier? FindCandidate(JsonElement element, string normalizedTicket)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (IsCandidateField(property.Name) && MatchesTicket(value, normalizedTicket))
                    {
                        return new CandidateIdentifier(property.Name, value!);
                    }
                }

                var nested = FindCandidate(property.Value, normalizedTicket);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindCandidate(item, normalizedTicket);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool IsCandidateField(string fieldName) =>
        CandidateFieldNames.Contains(fieldName) ||
        fieldName.Contains("ticket", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Contains("card", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Contains("barcode", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Contains("qr", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Contains("session", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesTicket(string? value, string normalizedTicket)
    {
        var normalizedValue = Normalize(value);
        if (normalizedValue.Length == 0 || normalizedTicket.Length == 0)
        {
            return false;
        }

        return normalizedValue == normalizedTicket ||
               normalizedValue.Contains(normalizedTicket, StringComparison.Ordinal) ||
               normalizedTicket.Contains(normalizedValue, StringComparison.Ordinal);
    }

    private static string? FindString(JsonElement? root, string propertyName)
    {
        if (root is null)
        {
            return null;
        }

        return FindString(root.Value, propertyName);
    }

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => null
                    };
                }

                var nested = FindString(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static JsonDocument? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadCode(JsonElement? root)
    {
        if (root?.TryGetProperty("code", out var code) != true)
        {
            return null;
        }

        return code.ValueKind switch
        {
            JsonValueKind.String => code.GetString(),
            JsonValueKind.Number => code.GetRawText(),
            _ => null
        };
    }

    private static string? ReadMessage(JsonElement? root) =>
        root?.TryGetProperty("msg", out var message) == true && message.ValueKind is JsonValueKind.String
            ? message.GetString()
            : null;

    private static int CountItems(JsonElement? root) =>
        ExtractRecords(root).Count;

    private static IReadOnlyList<string> BuildSanitizedRecordSamples(JsonElement? root)
    {
        var records = ExtractRecords(root);
        if (records.Count == 0)
        {
            return [];
        }

        return records
            .Take(3)
            .Select(FormatSanitizedRecordSample)
            .ToArray();
    }

    private static string BuildEndpointOutcome(
        HttpStatusCode httpStatusCode,
        string? code,
        int itemCount,
        bool matchedTicketIdentifier,
        bool ticketSearchApplied)
    {
        if ((int)httpStatusCode is < 200 or >= 300)
        {
            return "endpoint failed";
        }

        if (!string.IsNullOrWhiteSpace(code) && code is not "0")
        {
            return "endpoint failed";
        }

        if (itemCount == 0)
        {
            return "returned empty";
        }

        if (!ticketSearchApplied)
        {
            return "returned records";
        }

        return matchedTicketIdentifier
            ? "returned records with matching ticket identifier"
            : "returned records with no matching ticket identifier";
    }

    private static IReadOnlyList<JsonElement> ExtractRecords(JsonElement? root)
    {
        if (root is null)
        {
            return [];
        }

        var data = root.Value.TryGetProperty("data", out var dataElement)
            ? dataElement
            : root.Value;

        if (data.ValueKind is JsonValueKind.Array)
        {
            return data.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        if (data.ValueKind is JsonValueKind.Object)
        {
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

        return [];
    }

    private static string FormatSanitizedRecordSample(JsonElement record)
    {
        var fields = new List<string>();
        CollectDiagnosticFields(record, fields);

        return fields.Count == 0
            ? "no ticket/card/session-like scalar fields in sample record"
            : string.Join(", ", fields.Order(StringComparer.OrdinalIgnoreCase).Take(20));
    }

    private static void CollectDiagnosticFields(JsonElement element, ICollection<string> fields, string? prefix = null)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = string.IsNullOrWhiteSpace(prefix)
                    ? property.Name
                    : $"{prefix}.{property.Name}";
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    CollectDiagnosticFields(property.Value, fields, name);
                    continue;
                }

                if ((IsCandidateField(property.Name) || DiagnosticFieldNames.Contains(property.Name)) &&
                    TryReadScalar(property.Value, out var value))
                {
                    fields.Add($"{name}={SanitizeDiagnosticValue(value)}");
                }
            }
        }

        if (element.ValueKind is JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray().Take(3))
            {
                var name = string.IsNullOrWhiteSpace(prefix)
                    ? $"[{index}]"
                    : $"{prefix}[{index}]";
                CollectDiagnosticFields(item, fields, name);
                index++;
            }
        }
    }

    private static bool TryReadScalar(JsonElement element, out string value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string SanitizeDiagnosticValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= 160)
        {
            return trimmed;
        }

        return trimmed[..160] + "...";
    }

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

    private sealed record RecordEndpointRequest(string Path, object? Body, string? SkipReason = null);

    private sealed record CandidateIdentifier(string IdentifierType, string IdentifierValue);
}

public sealed record HikCentralTicketDiscoveryRequest(
    string PrintedTicketNumber,
    string ParkingLotIndexCode,
    DateTimeOffset? BeginTime = null,
    DateTimeOffset? EndTime = null,
    string? CameraIndexCode = null);

public sealed record HikCentralTicketDiscoveryResult(
    string TicketNumber,
    bool CardNumAccepted,
    string? DiscoveredIdentifierType,
    string? DiscoveredIdentifierValue,
    string? EndpointSource,
    string ParkingLotIndexCode,
    string? PassagewayIndexCode,
    string? LaneIndexCode,
    string? PlateLicense,
    string? Fee,
    string? ParkingInTime,
    string? ParkingDuration,
    string? HikCentralCode,
    string? HikCentralMessage,
    IReadOnlyList<HikCentralEndpointSummary> EndpointSummaries,
    string Conclusion);

public sealed record HikCentralEndpointSummary(
    string EndpointPath,
    int HttpStatusCode,
    string? HikCentralCode,
    string? HikCentralMessage,
    int ItemCount,
    string Outcome,
    IReadOnlyList<string> SanitizedRecordSamples)
{
    public static HikCentralEndpointSummary Skipped(string endpointPath, string reason) =>
        new(
            endpointPath,
            HttpStatusCode: 0,
            HikCentralCode: null,
            HikCentralMessage: reason,
            ItemCount: 0,
            Outcome: reason,
            SanitizedRecordSamples: []);
}

public sealed record HikCentralReadOnlyEndpointResult(
    string EndpointPath,
    HttpStatusCode HttpStatusCode,
    string? Code,
    string? Message,
    JsonElement? Root,
    int ItemCount)
{
    public HikCentralEndpointSummary ToSummary(
        string? ticketNumber = null,
        bool matchedTicketIdentifier = false)
    {
        var itemCount = ItemCount;
        var matched = matchedTicketIdentifier ||
            (!string.IsNullOrWhiteSpace(ticketNumber) &&
             HikCentralTicketDiscoveryClient.FindCandidateForSummary(Root, ticketNumber));

        return new(
            EndpointPath,
            (int)HttpStatusCode,
            Code,
            Message,
            itemCount,
            HikCentralTicketDiscoveryClient.BuildEndpointOutcomeForSummary(
                HttpStatusCode,
                Code,
                itemCount,
                matched,
                !string.IsNullOrWhiteSpace(ticketNumber)),
            HikCentralTicketDiscoveryClient.BuildSanitizedRecordSamplesForSummary(Root));
    }
}
