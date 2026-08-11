using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// On-demand HikCentral projection sync service.
/// </summary>
public sealed class HikCentralVendorSessionProjectionSyncService(
    IHikCentralLiveActivationGate activationGate,
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

        await activationGate.EnsureActivatedAsync(cancellationToken);

        var recordsSeen = 0;
        var projections = new List<VendorSessionProjection>();
        var pagesPulled = 0;
        var completedPagination = false;

        for (var pageIndex = 1; pageIndex <= command.MaxPages; pageIndex++)
        {
            HikCentralPassagewayRecordPage page;
            try
            {
                page = await passagewayClient.GetPassagewayRecordsAsync(
                    new HikCentralPassagewayRecordRequest(
                        command.ParkingLotIndexCode,
                        command.BeginTime,
                        command.EndTime,
                        pageIndex,
                        command.PageSize,
                        command.CorrelationId),
                    cancellationToken);
            }
            catch (HikCentralPassagewayException ex)
            {
                throw new VendorSessionProjectionException(ex.Classification, ex.Retryable);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new VendorSessionProjectionException(
                    "HIKCENTRAL_ADAPTER_FAILURE",
                    retryable: false);
            }

            pagesPulled++;
            foreach (var record in page.Records)
            {
                try
                {
                    recordsSeen++;
                    if (!normalizer.TryNormalize(
                        record,
                        command.VendorSystemId,
                        command.SiteId,
                        command.SiteGroupId,
                        command.CorrelationId,
                        clock.UtcNow,
                        out var projection))
                    {
                        throw new VendorSessionProjectionException(
                            "HIKCENTRAL_MAPPING_FAILURE",
                            retryable: false);
                    }

                    projections.Add(projection!);
                }
                catch (VendorSessionProjectionException)
                {
                    throw;
                }
                catch (Exception)
                {
                    throw new VendorSessionProjectionException(
                        "HIKCENTRAL_MAPPING_FAILURE",
                        retryable: false);
                }
            }

            if (page.Total.HasValue)
            {
                if (recordsSeen >= page.Total.Value)
                {
                    completedPagination = true;
                    break;
                }

                if (page.Records.Count < command.PageSize)
                {
                    throw new VendorSessionProjectionException(
                        "HIKCENTRAL_PAGINATION_INCOMPLETE",
                        retryable: true);
                }
            }
            else if (page.Records.Count < command.PageSize)
            {
                completedPagination = true;
                break;
            }
        }

        if (!completedPagination)
        {
            throw new VendorSessionProjectionException(
                "HIKCENTRAL_PAGINATION_INCOMPLETE",
                retryable: true);
        }

        try
        {
            await repository.UpsertBatchAsync(projections, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new VendorSessionProjectionException(
                "PROJECTION_PERSISTENCE_FAILURE",
                retryable: true);
        }

        logger.LogInformation(
            "HikCentral passageway projection sync completed. correlation_id={CorrelationId} parking_lot_index_code={ParkingLotIndexCode} pages_pulled={PagesPulled} records_seen={RecordsSeen} records_projected={RecordsProjected} records_skipped={RecordsSkipped}",
            command.CorrelationId,
            command.ParkingLotIndexCode,
            pagesPulled,
            recordsSeen,
            projections.Count,
            0);

        return new SyncVendorSessionProjectionsResult(
            pagesPulled,
            recordsSeen,
            projections.Count,
            0,
            command.CorrelationId);
    }
}
