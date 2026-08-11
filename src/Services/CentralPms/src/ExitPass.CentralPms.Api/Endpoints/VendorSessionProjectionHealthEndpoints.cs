using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Operations;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Read-only operational visibility endpoints for vendor session projection health.
/// </summary>
public static class VendorSessionProjectionHealthEndpoints
{
    private const string ViewerPolicy = "VendorSessionProjectionHealthViewer";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.VendorSessionProjectionHealth");

    /// <summary>
    /// Maps read-only vendor session projection health endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapVendorSessionProjectionHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/vendor-session-projections")
            .WithTags("OpsVendorSessionProjections");

        group.MapGet("/targets", ListTargetsAsync)
            .WithName("ListVendorSessionProjectionHealthTargets")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<VendorSessionProjectionHealthTargetsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("List vendor session projection sync target health")
            .WithDescription("Returns read-only HikCentral projection target health, freshness, and projection counts. This endpoint does not mutate scheduler, projection, payment, tariff, or exit state.");

        group.MapGet("/targets/{projectionSyncTargetId:guid}", GetTargetAsync)
            .WithName("GetVendorSessionProjectionHealthTarget")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<VendorSessionProjectionHealthTargetDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Get one vendor session projection sync target health detail")
            .WithDescription("Returns read-only target health, freshness, projection counts, and limited latest projection rows. This endpoint does not expose raw HikCentral payloads or credentials.");

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetVendorSessionProjectionHealthSummary")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<VendorSessionProjectionHealthSummaryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Get vendor session projection health dashboard summary")
            .WithDescription("Returns read-only aggregate projection health and safe scheduler/fallback configuration visibility.");

        return app;
    }

    private static async Task<IResult> ListTargetsAsync(
        IVendorSessionProjectionHealthService service,
        IOptions<VendorSessionProjectionOptions> options,
        HttpRequest request,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP ListVendorSessionProjectionHealthTargets", request);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.VendorSessionProjectionHealthEndpoints");

        try
        {
            var targets = await service.ListTargetsAsync(cancellationToken);
            activity?.SetTag("projection_target_count", targets.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new VendorSessionProjectionHealthTargetsResponse(
                targets.Select(ToContract).ToArray(),
                ToConfigContract(options.Value)));
        }
        catch (Exception ex)
        {
            return Error(ex, logger, "VENDOR_SESSION_PROJECTION_HEALTH_LIST_FAILED", ResolveCorrelationId(request));
        }
    }

    private static async Task<IResult> GetTargetAsync(
        Guid projectionSyncTargetId,
        int? latestRecordLimit,
        IVendorSessionProjectionHealthService service,
        IOptions<VendorSessionProjectionOptions> options,
        HttpRequest request,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetVendorSessionProjectionHealthTarget", request);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.VendorSessionProjectionHealthEndpoints");
        activity?.SetTag("projection_sync_target_id", projectionSyncTargetId);

        try
        {
            var detail = await service.GetTargetAsync(
                projectionSyncTargetId,
                latestRecordLimit ?? 20,
                cancellationToken);
            if (detail is null)
            {
                return Results.NotFound(BuildError(
                    "VENDOR_SESSION_PROJECTION_TARGET_NOT_FOUND",
                    "Vendor session projection sync target was not found.",
                    ResolveCorrelationId(request),
                    retryable: false));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new VendorSessionProjectionHealthTargetDetailResponse(
                ToContract(detail.Target),
                detail.LatestProjectedRecords.Select(ToContract).ToArray(),
                ToConfigContract(options.Value)));
        }
        catch (Exception ex)
        {
            return Error(ex, logger, "VENDOR_SESSION_PROJECTION_HEALTH_DETAIL_FAILED", ResolveCorrelationId(request));
        }
    }

    private static async Task<IResult> GetSummaryAsync(
        IVendorSessionProjectionHealthService service,
        HttpRequest request,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetVendorSessionProjectionHealthSummary", request);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.VendorSessionProjectionHealthEndpoints");

        try
        {
            var summary = await service.GetSummaryAsync(cancellationToken);
            activity?.SetTag("projection_total_targets", summary.TotalTargets);
            activity?.SetTag("projection_stale_targets", summary.StaleTargets);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(summary));
        }
        catch (Exception ex)
        {
            return Error(ex, logger, "VENDOR_SESSION_PROJECTION_HEALTH_SUMMARY_FAILED", ResolveCorrelationId(request));
        }
    }

    private static Activity? StartActivity(string name, HttpRequest request)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Server);
        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        return activity;
    }

    private static VendorSessionProjectionHealthTargetDto ToContract(VendorSessionProjectionHealthTarget target) =>
        new(
            target.ProjectionSyncTargetId,
            target.SiteId,
            target.SiteGroupId,
            target.VendorSystemId,
            target.ParkingLotIndexCode,
            target.ParkingLotName,
            target.Enabled,
            target.HealthStatus.ToString(),
            target.LastAttemptAt,
            target.LastSuccessAt,
            target.LastFailureAt,
            target.FailureCount,
            target.LastErrorCode,
            target.LastErrorMessage,
            target.LastLockContentionAt,
            target.LockContentionCount,
            target.PollIntervalSeconds,
            target.LookbackWindowMinutes,
            target.PageSize,
            target.LatestProjectionLastRefreshedAt,
            target.FreshnessAge?.TotalSeconds,
            target.FreshnessClassification,
            target.IsStale,
            target.TotalProjectionCount,
            target.ActiveProjectionCount,
            target.ExitedProjectionCount,
            target.CardNumProjectionCount,
            target.PlateLicenseProjectionCount);

    private static VendorSessionProjectionHealthLatestRecordDto ToContract(
        VendorSessionProjectionHealthLatestRecord record) =>
        new(
            record.VendorSessionProjectionId,
            record.VendorRecordGuid,
            record.CardNum,
            record.PlateLicense,
            record.EnterTime,
            record.ExitTime,
            record.ProjectionStatus.ToString(),
            record.LastRefreshedAt,
            record.SourceEventAt,
            record.CorrelationId);

    private static VendorSessionProjectionHealthSummaryResponse ToContract(
        VendorSessionProjectionHealthSummary summary) =>
        new(
            summary.TotalTargets,
            summary.EnabledTargets,
            summary.DisabledTargets,
            summary.HealthyTargets,
            summary.DegradedTargets,
            summary.FailingTargets,
            summary.UnknownTargets,
            summary.StaleTargets,
            summary.TargetsWithLastFailure,
            summary.LatestSuccessfulProjectionSyncAt,
            summary.TotalActiveProjections,
            summary.TotalExitedProjections,
            ToContract(summary.Config));

    private static VendorSessionProjectionHealthConfigDto ToConfigContract(VendorSessionProjectionOptions options) =>
        new(
            options.SchedulerEnabled,
            options.RequiredForEnvironment,
            options.DegradedResolveFallbackEnabled,
            options.NormalFreshnessTargetSeconds,
            options.MaxProjectionAgeMinutes,
            options.MaxParallelSiteJobs,
            options.SchedulerScanIntervalSeconds);

    private static VendorSessionProjectionHealthConfigDto ToContract(VendorSessionProjectionHealthConfig config) =>
        new(
            config.SchedulerEnabled,
            config.RequiredForEnvironment,
            config.DegradedResolveFallbackEnabled,
            config.NormalFreshnessTargetSeconds,
            config.MaxProjectionAgeMinutes,
            config.MaxParallelSiteJobs,
            config.SchedulerScanIntervalSeconds);

    private static IResult Error(Exception ex, ILogger logger, string errorCode, Guid correlationId)
    {
        logger.LogError(ex, "Vendor session projection health read failed. error_code={ErrorCode}", errorCode);
        return Results.Json(
            BuildError(
                errorCode,
                "Vendor session projection health could not be read.",
                correlationId,
                retryable: true),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId, bool retryable) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private static Guid ResolveCorrelationId(HttpRequest request)
    {
        return request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;
    }
}
