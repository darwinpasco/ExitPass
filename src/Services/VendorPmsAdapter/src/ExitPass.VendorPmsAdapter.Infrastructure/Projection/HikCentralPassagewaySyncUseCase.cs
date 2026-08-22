using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExitPass.VendorPmsAdapter.Application.Routing;
using ExitPass.VendorPmsAdapter.Contracts.Projection;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

namespace ExitPass.VendorPmsAdapter.Infrastructure.Projection;

/// <summary>Normalizes bounded HikCentral passageway pages inside the Site Integration Adapter boundary.</summary>
public sealed class HikCentralPassagewaySyncUseCase(
    IHikCentralPassagewayRecordClient client,
    SiteAdapterBinding binding,
    SiteAdapterBindingGuard bindingGuard)
{
    /// <summary>Retrieves and normalizes a complete bounded page sequence.</summary>
    public async Task<VendorPassagewaySyncResponse> ExecuteAsync(
        VendorPassagewaySyncRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bindingGuard.EnsureCompatible(request.Context, request.ParkingLotIndexCode);

        if (request.CorrelationId == Guid.Empty || request.PageSize is < 1 or > 500 || request.MaxPages < 1 ||
            request.EndTime <= request.BeginTime)
        {
            return Failed("SITE_ADAPTER_PROJECTION_REQUEST_INVALID", false, request.CorrelationId);
        }

        var accepted = new List<VendorPassagewayRecordDto>();
        var seen = 0;
        var skipped = 0;
        var pages = 0;
        var complete = false;

        try
        {
            for (var pageIndex = 1; pageIndex <= request.MaxPages; pageIndex++)
            {
                var page = await client.GetPassagewayRecordsAsync(
                    new HikCentralPassagewayRecordRequest(
                        binding.ParkingLotIndexCode,
                        request.BeginTime,
                        request.EndTime,
                        pageIndex,
                        request.PageSize,
                        request.CorrelationId),
                    cancellationToken);
                pages++;

                foreach (var record in page.Records)
                {
                    seen++;
                    if (TryNormalize(record, out var normalized))
                    {
                        accepted.Add(normalized!);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                if (page.Total.HasValue)
                {
                    if (seen >= page.Total.Value)
                    {
                        complete = true;
                        break;
                    }

                    if (page.Records.Count < request.PageSize)
                    {
                        return Failed("VENDOR_PAGINATION_INCOMPLETE", true, request.CorrelationId, pages, seen, skipped);
                    }
                }
                else if (page.Records.Count < request.PageSize)
                {
                    complete = true;
                    break;
                }
            }
        }
        catch (HikCentralPassagewayException ex)
        {
            return Failed(ex.Classification, ex.Retryable, request.CorrelationId, pages, seen, skipped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed("VENDOR_ADAPTER_FAILURE", true, request.CorrelationId, pages, seen, skipped);
        }

        if (!complete)
        {
            return Failed("VENDOR_PAGINATION_INCOMPLETE", true, request.CorrelationId, pages, seen, skipped);
        }

        return new VendorPassagewaySyncResponse(
            true, pages, seen, accepted.Count, skipped, accepted, null, false,
            request.CorrelationId, binding.ToResponseContext());
    }

    private bool TryNormalize(HikCentralPassagewayRecord record, out VendorPassagewayRecordDto? normalized)
    {
        normalized = null;
        var parkingLot = First(record.ParkingLotInfo?.ParkingLotIndexCode, record.ParkingLotInfo?.IndexCode);
        var vendorRecord = First(record.Guid);
        var card = UsableIdentifier(record.PersonInfo?.CardNum);
        var plate = UsablePlate(record.CarInfo?.PlateLicense);
        if (!string.Equals(parkingLot, binding.ParkingLotIndexCode, StringComparison.Ordinal) ||
            vendorRecord is null || (card is null && plate is null))
        {
            return false;
        }

        var entry = ParseTimestamp(First(record.CarInfo?.EnterTime, record.EnterTime));
        var exit = ParseTimestamp(First(record.CarInfo?.ExitTime, record.ExitTime));
        var sourceTimestamp = exit ?? entry ?? DateTimeOffset.UtcNow;
        var hashInput = string.Join('|', vendorRecord, parkingLot, card, plate, entry?.ToString("O"), exit?.ToString("O"));

        normalized = new VendorPassagewayRecordDto(
            vendorRecord,
            card,
            plate,
            parkingLot,
            First(record.ParkingLotInfo?.ParkingLotName, record.ParkingLotInfo?.Name),
            First(record.PassagewayInfo?.PassagewayIndexCode, record.PassagewayInfo?.IndexCode),
            First(record.PassagewayInfo?.PassagewayName, record.PassagewayInfo?.Name),
            First(record.LaneInfo?.LaneIndexCode, record.LaneInfo?.IndexCode),
            First(record.LaneInfo?.LaneName, record.LaneInfo?.Name),
            First(record.LaneInfo?.Direction, record.LaneInfo?.LaneDirection),
            entry,
            exit,
            First(record.AllowType),
            First(record.AllowResult),
            HikCentralPassagewayRecordClient.PassagewayRecordPath,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant(),
            sourceTimestamp);
        return true;
    }

    private VendorPassagewaySyncResponse Failed(
        string code, bool retryable, Guid correlationId, int pages = 0, int seen = 0, int skipped = 0) =>
        new(false, pages, seen, 0, skipped, [], Sanitize(code), retryable, correlationId, binding.ToResponseContext());

    private static string Sanitize(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? value.ToUpperInvariant()
            : "VENDOR_ADAPTER_FAILURE";

    private static string? First(params string?[] values) =>
        values.Select(value => string.IsNullOrWhiteSpace(value) ? null : value.Trim()).FirstOrDefault(value => value is not null);

    private static string? UsableIdentifier(string? value) => First(value);

    private static string? UsablePlate(string? value)
    {
        var normalized = First(value);
        return normalized is null || normalized.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
}
