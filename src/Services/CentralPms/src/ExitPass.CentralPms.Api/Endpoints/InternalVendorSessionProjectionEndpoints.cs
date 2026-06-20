using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Contracts.Common;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for scoped vendor session projection operations.
/// </summary>
public static class InternalVendorSessionProjectionEndpoints
{
    private const string ProjectionSyncPolicy = "VendorSessionProjectionSyncOperator";

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.VendorSessionProjections");

    /// <summary>
    /// Maps internal vendor session projection endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalVendorSessionProjectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/vendor-session-projections")
            .WithTags("InternalVendorSessionProjections")
            .RequireInternalServiceMtls();

        group.MapPost("/sync", SyncAsync)
            .WithName("RunVendorSessionProjectionSync")
            .WithMetadata(new ReconciliationPolicyMetadata(ProjectionSyncPolicy))
            .Produces<VendorSessionProjectionSyncResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> SyncAsync(
        VendorSessionProjectionSyncRequest request,
        HttpRequest httpRequest,
        IVendorSessionProjectionSyncOrchestrator orchestrator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP RunVendorSessionProjectionSync", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalVendorSessionProjectionEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest, request.CorrelationId);

        activity?.SetTag("http.route", "/v1/internal/vendor-session-projections/sync");
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("site_id", request.SiteId);
        activity?.SetTag("parking_lot_index_code", request.ParkingLotIndexCode);

        if (request.SiteId is null && string.IsNullOrWhiteSpace(request.ParkingLotIndexCode))
        {
            return Results.BadRequest(BuildError(
                "VENDOR_SESSION_PROJECTION_SYNC_SCOPE_REQUIRED",
                "Projection sync requires site_id or parking_lot_index_code scope.",
                correlationId,
                retryable: false));
        }

        try
        {
            var result = await orchestrator.RunManualAsync(
                new RunVendorSessionProjectionSyncCommand(
                    request.SiteId,
                    request.ParkingLotIndexCode,
                    request.LookbackWindowMinutes,
                    request.PageSize,
                    request.Force,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.SetTag("records_read", result.RecordsRead);
            activity?.SetTag("records_upserted", result.RecordsUpserted);
            activity?.SetTag("records_skipped", result.RecordsSkipped);

            logger.LogInformation(
                "Manual vendor session projection sync completed. projection_sync_target_id={ProjectionSyncTargetId} site_id={SiteId} parking_lot_index_code={ParkingLotIndexCode} succeeded={Succeeded}",
                result.ProjectionSyncTargetId,
                result.SiteId,
                result.ParkingLotIndexCode,
                result.Succeeded);

            return Results.Ok(ToContract(result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "VENDOR_SESSION_PROJECTION_SYNC_TARGET_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Projection sync target was not found for the requested scope.", correlationId, false));
        }
        catch (InvalidOperationException ex) when (ex.Message == "VENDOR_SESSION_PROJECTION_SYNC_TARGET_AMBIGUOUS")
        {
            return Results.Conflict(BuildError(ex.Message, "Projection sync scope matched more than one target.", correlationId, false));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId, false));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Unexpected vendor session projection sync trigger failure.");
            return Results.Json(
                BuildError(
                    "VENDOR_SESSION_PROJECTION_SYNC_INTERNAL_ERROR",
                    "An unexpected error occurred while running the scoped projection sync.",
                    correlationId,
                    true),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static VendorSessionProjectionSyncResponse ToContract(VendorSessionProjectionTargetRunResult result) =>
        new(
            result.ProjectionSyncTargetId,
            result.SiteId,
            result.SiteGroupId,
            result.VendorSystemId,
            result.ParkingLotIndexCode,
            result.Succeeded,
            result.RecordsRead,
            result.RecordsUpserted,
            result.RecordsSkipped,
            result.PagesPulled,
            result.StartedAt,
            result.CompletedAt,
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId);

    private static Guid ResolveCorrelationId(HttpRequest request, Guid? requestCorrelationId)
    {
        if (requestCorrelationId.HasValue && requestCorrelationId.Value != Guid.Empty)
        {
            return requestCorrelationId.Value;
        }

        return request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var correlationId)
                ? correlationId
                : Guid.NewGuid();
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };
}

/// <summary>
/// Request for a scoped manual vendor session projection sync.
/// </summary>
public sealed record VendorSessionProjectionSyncRequest(
    Guid? SiteId,
    string? ParkingLotIndexCode,
    int? LookbackWindowMinutes,
    int? PageSize,
    bool Force,
    Guid? CorrelationId);

/// <summary>
/// Response from a scoped manual vendor session projection sync.
/// </summary>
public sealed record VendorSessionProjectionSyncResponse(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    bool Succeeded,
    int RecordsRead,
    int RecordsUpserted,
    int RecordsSkipped,
    int PagesPulled,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ErrorCode,
    string? ErrorMessage,
    Guid CorrelationId);
