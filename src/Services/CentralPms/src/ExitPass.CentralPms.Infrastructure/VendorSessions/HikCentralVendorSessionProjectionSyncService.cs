using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// On-demand HikCentral projection sync service.
/// </summary>
public sealed class HikCentralVendorSessionProjectionSyncService(
    IHikCentralPassagewayRecordClient passagewayClient,
    HikCentralPassagewayProjectionNormalizer normalizer,
    IVendorSessionProjectionRepository repository,
    ISystemClock clock,
    ILogger<HikCentralVendorSessionProjectionSyncService> logger) : IVendorSessionProjectionSyncService
{
    /// <inheritdoc />
    public async Task<SyncVendorSessionProjectionsResult> SyncAsync(
        SyncVendorSessionProjectionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ParkingLotIndexCode))
        {
            throw new ArgumentException("ParkingLotIndexCode is required.", nameof(command));
        }

        if (command.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "PageSize must be between 1 and 500.");
        }

        if (command.MaxPages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "MaxPages must be at least 1.");
        }

        var recordsSeen = 0;
        var recordsProjected = 0;
        var recordsSkipped = 0;
        var pagesPulled = 0;

        for (var pageIndex = 1; pageIndex <= command.MaxPages; pageIndex++)
        {
            var page = await passagewayClient.GetPassagewayRecordsAsync(
                new HikCentralPassagewayRecordRequest(
                    command.ParkingLotIndexCode,
                    command.BeginTime,
                    command.EndTime,
                    pageIndex,
                    command.PageSize,
                    command.CorrelationId),
                cancellationToken);
            pagesPulled++;

            if (page.Code is not null && page.Code is not "0")
            {
                logger.LogWarning(
                    "HikCentral passageway projection sync stopped on non-success vendor code. correlation_id={CorrelationId} page_index={PageIndex} hikcentral_code={HikCentralCode} hikcentral_message={HikCentralMessage}",
                    command.CorrelationId,
                    pageIndex,
                    page.Code,
                    page.Message);
                break;
            }

            foreach (var record in page.Records)
            {
                recordsSeen++;
                var observedAt = clock.UtcNow;
                if (!normalizer.TryNormalize(
                    record,
                    command.VendorSystemId,
                    command.SiteId,
                    command.SiteGroupId,
                    command.CorrelationId,
                    observedAt,
                    out var projection))
                {
                    recordsSkipped++;
                    continue;
                }

                await repository.UpsertAsync(projection!, cancellationToken);
                recordsProjected++;
            }

            if (page.Records.Count < command.PageSize)
            {
                break;
            }
        }

        logger.LogInformation(
            "HikCentral passageway projection sync completed. correlation_id={CorrelationId} parking_lot_index_code={ParkingLotIndexCode} pages_pulled={PagesPulled} records_seen={RecordsSeen} records_projected={RecordsProjected} records_skipped={RecordsSkipped}",
            command.CorrelationId,
            command.ParkingLotIndexCode,
            pagesPulled,
            recordsSeen,
            recordsProjected,
            recordsSkipped);

        return new SyncVendorSessionProjectionsResult(
            pagesPulled,
            recordsSeen,
            recordsProjected,
            recordsSkipped,
            command.CorrelationId);
    }
}
