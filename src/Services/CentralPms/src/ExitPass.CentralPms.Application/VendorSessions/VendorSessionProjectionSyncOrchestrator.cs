using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Centralized scheduler/manual orchestration for site-scoped vendor session projection sync targets.
/// </summary>
public sealed class VendorSessionProjectionSyncOrchestrator(
    IVendorSessionProjectionSyncTargetRepository targetRepository,
    IVendorSessionProjectionSyncService syncService,
    ISystemClock clock,
    IOptions<VendorSessionProjectionOptions> options,
    ILogger<VendorSessionProjectionSyncOrchestrator> logger) : IVendorSessionProjectionSyncOrchestrator
{
    /// <inheritdoc />
    public async Task<VendorSessionProjectionSchedulerRunResult> RunDueTargetsOnceAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        var targets = await targetRepository.ListDueTargetsAsync(startedAt, cancellationToken);
        var results = new List<VendorSessionProjectionTargetRunResult>();
        var maxParallelism = options.Value.EffectiveMaxParallelSiteJobs();

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (target, token) =>
            {
                var result = await RunTargetAsync(
                    target,
                    lookbackOverrideMinutes: null,
                    pageSizeOverride: null,
                    correlationId: Guid.NewGuid(),
                    token);

                lock (results)
                {
                    results.Add(result);
                }
            });

        var completedAt = clock.UtcNow;
        return new VendorSessionProjectionSchedulerRunResult(
            TargetsLoaded: targets.Count,
            TargetsRun: results.Count,
            TargetsSucceeded: results.Count(result => result.Succeeded),
            TargetsFailed: results.Count(result => !result.Succeeded),
            startedAt,
            completedAt,
            results.OrderBy(result => result.StartedAt).ToArray());
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionTargetRunResult> RunManualAsync(
        RunVendorSessionProjectionSyncCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SiteId is null && string.IsNullOrWhiteSpace(command.ParkingLotIndexCode))
        {
            throw new ArgumentException("Manual projection sync requires site_id or parking_lot_index_code scope.", nameof(command));
        }

        var target = await targetRepository.FindEnabledTargetAsync(
            command.SiteId,
            Normalize(command.ParkingLotIndexCode),
            cancellationToken);

        if (target is null)
        {
            throw new InvalidOperationException("VENDOR_SESSION_PROJECTION_SYNC_TARGET_NOT_FOUND");
        }

        return await RunTargetAsync(
            target,
            command.LookbackWindowMinutes,
            command.PageSize,
            command.CorrelationId == Guid.Empty ? Guid.NewGuid() : command.CorrelationId,
            cancellationToken);
    }

    private async Task<VendorSessionProjectionTargetRunResult> RunTargetAsync(
        VendorSessionProjectionSyncTarget target,
        int? lookbackOverrideMinutes,
        int? pageSizeOverride,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        try
        {
            var lookbackMinutes = options.Value.EffectiveLookbackWindowMinutes(
                lookbackOverrideMinutes ?? target.LookbackWindowMinutes);
            var pageSize = options.Value.EffectivePageSize(pageSizeOverride ?? target.PageSize);
            var command = new SyncVendorSessionProjectionsCommand(
                target.VendorSystemId,
                target.SiteId,
                target.SiteGroupId,
                target.ParkingLotIndexCode,
                BeginTime: startedAt.AddMinutes(-lookbackMinutes),
                EndTime: startedAt,
                pageSize,
                options.Value.EffectiveMaxPagesPerRun(),
                correlationId);

            var syncResult = await syncService.SyncAsync(command, cancellationToken);
            var completedAt = clock.UtcNow;

            await targetRepository.UpdateHealthAsync(
                new VendorSessionProjectionSyncTargetHealthUpdate(
                    target.ProjectionSyncTargetId,
                    completedAt,
                    Succeeded: true,
                    ErrorCode: null,
                    ErrorMessage: null,
                    options.Value.FailingFailureCountThreshold,
                    correlationId),
                cancellationToken);

            logger.LogInformation(
                "Vendor session projection target sync succeeded. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode} records_seen={RecordsSeen} records_upserted={RecordsUpserted}",
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.ParkingLotIndexCode,
                syncResult.RecordsSeen,
                syncResult.RecordsUpserted);

            return new VendorSessionProjectionTargetRunResult(
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.SiteGroupId,
                target.VendorSystemId,
                target.ParkingLotIndexCode,
                Succeeded: true,
                RecordsRead: syncResult.RecordsSeen,
                RecordsUpserted: syncResult.RecordsUpserted,
                RecordsSkipped: syncResult.RecordsSkipped,
                PagesPulled: syncResult.PagesPulled,
                startedAt,
                completedAt,
                ErrorCode: null,
                ErrorMessage: null,
                correlationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var completedAt = clock.UtcNow;
            await targetRepository.UpdateHealthAsync(
                new VendorSessionProjectionSyncTargetHealthUpdate(
                    target.ProjectionSyncTargetId,
                    completedAt,
                    Succeeded: false,
                    ErrorCode: ex.GetType().Name,
                    ErrorMessage: Truncate(ex.Message, 500),
                    options.Value.FailingFailureCountThreshold,
                    correlationId),
                cancellationToken);

            logger.LogError(
                ex,
                "Vendor session projection target sync failed. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode}",
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.ParkingLotIndexCode);

            return new VendorSessionProjectionTargetRunResult(
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.SiteGroupId,
                target.VendorSystemId,
                target.ParkingLotIndexCode,
                Succeeded: false,
                RecordsRead: 0,
                RecordsUpserted: 0,
                RecordsSkipped: 0,
                PagesPulled: 0,
                startedAt,
                completedAt,
                ErrorCode: ex.GetType().Name,
                ErrorMessage: Truncate(ex.Message, 500),
                correlationId);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
