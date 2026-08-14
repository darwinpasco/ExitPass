using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// Normalizes HikCentral passageway records into ExitPass-owned projection snapshots.
/// </summary>
public sealed class HikCentralPassagewayProjectionNormalizer
{
    /// <summary>
    /// Source API path used to build the projection.
    /// </summary>
    public const string SourceApi = HikCentralPassagewayRecordClient.PassagewayRecordPath;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> UnusablePlateValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "-",
        "?",
        "N/A",
        "NA",
        "NONE",
        "NO PLATE",
        "NO_PLATE",
        "NOT AVAILABLE",
        "NULL",
        "UNKNOWN",
        "UNREADABLE"
    };

    /// <summary>
    /// Attempts to normalize one HikCentral passageway record into a projection.
    /// </summary>
    public bool TryNormalize(
        HikCentralPassagewayRecord record,
        Guid? vendorSystemId,
        Guid? siteId,
        Guid? siteGroupId,
        Guid correlationId,
        DateTimeOffset observedAt,
        out VendorSessionProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(record);

        var parkingLotIndexCode = NamedIndexCode(record.ParkingLotInfo, preferParkingLotFields: true);
        var cardNum = Normalize(record.PersonInfo?.CardNum);
        var plateLicense = NormalizePlateLicense(record.CarInfo?.PlateLicense);
        var enterTime = ParseTimestamp(record.CarInfo?.EnterTime) ?? ParseTimestamp(record.EnterTime);
        var exitTime = ParseTimestamp(record.CarInfo?.ExitTime) ?? ParseTimestamp(record.ExitTime);

        if (!TryBuildStableIdentity(
            record,
            parkingLotIndexCode,
            cardNum,
            plateLicense,
            enterTime,
            out var identityType,
            out var identityKey))
        {
            projection = null;
            return false;
        }

        var payloadHash = HashPayload(record);
        var sourceEventAt = exitTime ?? enterTime;
        projection = new VendorSessionProjection(
            VendorSessionProjectionId: Guid.NewGuid(),
            vendorSystemId,
            siteId,
            siteGroupId,
            parkingLotIndexCode,
            NamedIndexName(record.ParkingLotInfo, preferParkingLotFields: true),
            NamedIndexCode(record.PassagewayInfo, preferParkingLotFields: false),
            NamedIndexName(record.PassagewayInfo, preferParkingLotFields: false),
            LaneIndexCode(record.LaneInfo),
            LaneName(record.LaneInfo),
            Normalize(record.LaneInfo?.LaneDirection) ?? Normalize(record.LaneInfo?.Direction),
            Normalize(record.Guid),
            cardNum,
            plateLicense,
            enterTime,
            exitTime,
            Normalize(record.AllowType),
            Normalize(record.AllowResult),
            Normalize(record.CarInfo?.ImageUrl) ?? Normalize(record.ImageUrl),
            SourceApi,
            payloadHash,
            BuildPayloadReference(record, identityKey),
            sourceEventAt,
            identityType,
            identityKey,
            observedAt,
            observedAt,
            observedAt,
            exitTime.HasValue ? VendorSessionProjectionStatus.Exited : VendorSessionProjectionStatus.Active,
            correlationId,
            observedAt,
            observedAt);
        return true;
    }

    private static bool TryBuildStableIdentity(
        HikCentralPassagewayRecord record,
        string? parkingLotIndexCode,
        string? cardNum,
        string? plateLicense,
        DateTimeOffset? enterTime,
        out string identityType,
        out string identityKey)
    {
        var guid = Normalize(record.Guid);
        if (!string.IsNullOrWhiteSpace(guid))
        {
            identityType = "VENDOR_RECORD_GUID";
            identityKey = $"HIKCENTRAL|GUID|{guid.ToUpperInvariant()}";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parkingLotIndexCode) &&
            !string.IsNullOrWhiteSpace(cardNum) &&
            enterTime.HasValue)
        {
            identityType = "PARKING_LOT_CARD_ENTER_TIME";
            identityKey = string.Join(
                "|",
                "HIKCENTRAL",
                "LOT_CARD_ENTER",
                parkingLotIndexCode.ToUpperInvariant(),
                cardNum.ToUpperInvariant(),
                enterTime.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parkingLotIndexCode) &&
            !string.IsNullOrWhiteSpace(plateLicense) &&
            enterTime.HasValue)
        {
            identityType = "PARKING_LOT_PLATE_ENTER_TIME";
            identityKey = string.Join(
                "|",
                "HIKCENTRAL",
                "LOT_PLATE_ENTER",
                parkingLotIndexCode.ToUpperInvariant(),
                plateLicense.ToUpperInvariant(),
                enterTime.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }

        identityType = string.Empty;
        identityKey = string.Empty;
        return false;
    }

    private static string? BuildPayloadReference(HikCentralPassagewayRecord record, string identityKey)
    {
        var guid = Normalize(record.Guid);
        return string.IsNullOrWhiteSpace(guid)
            ? $"hikcentral:passageway-record:{HashText(identityKey)}"
            : $"hikcentral:passageway-record:{guid}";
    }

    private static string HashPayload(HikCentralPassagewayRecord record)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        return HashText(json);
    }

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizePlateLicense(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null || UnusablePlateValues.Contains(normalized)
            ? null
            : normalized;
    }

    private static string? NamedIndexCode(HikCentralNamedIndex? value, bool preferParkingLotFields)
    {
        if (value is null)
        {
            return null;
        }

        return preferParkingLotFields
            ? Normalize(value.ParkingLotIndexCode) ?? Normalize(value.IndexCode)
            : Normalize(value.PassagewayIndexCode) ?? Normalize(value.IndexCode);
    }

    private static string? NamedIndexName(HikCentralNamedIndex? value, bool preferParkingLotFields)
    {
        if (value is null)
        {
            return null;
        }

        return preferParkingLotFields
            ? Normalize(value.ParkingLotName) ?? Normalize(value.Name)
            : Normalize(value.PassagewayName) ?? Normalize(value.Name);
    }

    private static string? LaneIndexCode(HikCentralLaneInfo? value)
    {
        return Normalize(value?.LaneIndexCode) ?? Normalize(value?.IndexCode);
    }

    private static string? LaneName(HikCentralLaneInfo? value)
    {
        return Normalize(value?.LaneName) ?? Normalize(value?.Name);
    }
}
