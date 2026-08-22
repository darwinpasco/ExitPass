using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Centralized scheduler/manual orchestration for site-scoped vendor session projection sync targets.
/// </summary>
public sealed class VendorSessionProjectionSyncOrchestrator(
    IVendorSessionProjectionSyncTargetRepository targetRepository,
    IVendorSessionProjectionExecutionLock executionLock,
    IVendorSessionProjectionSyncService syncService,
    ISystemClock clock,
    IOptions<VendorSessionProjectionOptions> options,
    CentralPmsMetrics metrics,
    ILogger<VendorSessionProjectionSyncOrchestrator> logger) : IVendorSessionProjectionSyncOrchestrator
{
    /// <inheritdoc />
    public async Task<VendorSessionProjectionSchedulerRunResult> RunDueTargetsOnceAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        var dueTargets = await targetRepository.ListDueTargetsAsync(startedAt, cancellationToken);
        var targets = dueTargets.ToArray();
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
            TargetsLoaded: targets.Length,
            TargetsRun: results.Count,
            TargetsSucceeded: results.Count(result => result.Succeeded),
            TargetsFailed: results.Count(result => !result.Succeeded && !result.Deferred),
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
        await using var lease = await executionLock.TryAcquireAsync(
            target.ProjectionSyncTargetId,
            cancellationToken);
        if (lease is null)
        {
            await targetRepository.RecordLockContentionAsync(
                target.ProjectionSyncTargetId,
                startedAt,
                correlationId,
                cancellationToken);
            metrics.VendorSessionProjectionLockContended();
            logger.LogWarning(
                "Vendor session projection target cycle deferred by distributed lock contention. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode}",
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
                startedAt,
                ErrorCode: "PROJECTION_LOCK_CONTENDED",
                ErrorMessage: "Projection was deferred because another scheduler owns this target.",
                correlationId)
            {
                Deferred = true
            };
        }

        metrics.VendorSessionProjectionAttempted();
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
                "Vendor session projection target sync succeeded. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode} result={Result} records_seen={RecordsSeen} records_upserted={RecordsUpserted} duration_ms={DurationMilliseconds}",
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.ParkingLotIndexCode,
                syncResult.RecordsUpserted == 0 ? "ZERO_ROWS" : "RECORDS_COMMITTED",
                syncResult.RecordsSeen,
                syncResult.RecordsUpserted,
                Math.Max(0, (completedAt - startedAt).TotalMilliseconds));
            metrics.VendorSessionProjectionCompleted(syncResult.RecordsUpserted, completedAt - startedAt);

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
            var classification = ex is VendorSessionProjectionException projectionException
                ? SanitizeClassification(projectionException.Classification)
                : "PROJECTION_UNEXPECTED_FAILURE";
            var retryable = ex is not VendorSessionProjectionException bounded || bounded.Retryable;
            await targetRepository.UpdateHealthAsync(
                new VendorSessionProjectionSyncTargetHealthUpdate(
                    target.ProjectionSyncTargetId,
                    completedAt,
                    Succeeded: false,
                    ErrorCode: classification,
                    ErrorMessage: "Projection failed safely; retry according to the bounded classification.",
                    options.Value.FailingFailureCountThreshold,
                    correlationId),
                cancellationToken);

            metrics.VendorSessionProjectionFailed(classification, retryable);
            logger.LogError(
                "Vendor session projection target sync failed safely. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode} failure_classification={FailureClassification} retryable={Retryable}",
                target.ProjectionSyncTargetId,
                target.SiteId,
                target.ParkingLotIndexCode,
                classification,
                retryable);

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
                ErrorCode: classification,
                ErrorMessage: "Projection failed safely; retry according to the bounded classification.",
                correlationId);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeClassification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PROJECTION_FAILURE";
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= 64 && normalized.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_')
                ? normalized
                : "PROJECTION_FAILURE";
    }

}
