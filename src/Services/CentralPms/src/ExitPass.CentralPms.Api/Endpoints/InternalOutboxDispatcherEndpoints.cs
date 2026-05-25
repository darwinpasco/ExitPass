using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Eventing;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for dispatching reconciliation outbox events.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Outbox dispatch publishes operational evidence only and never mutates payment, provider, exit, gate, or settlement truth.
/// - Reconciliation outbox records remain durable and retryable in events-owned tables.
/// </summary>
public static class InternalOutboxDispatcherEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.ReconciliationOutboxDispatcher");

    /// <summary>
    /// Maps internal reconciliation outbox dispatcher endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalOutboxDispatcherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/events/outbox")
            .WithTags("InternalEvents")
            .RequireInternalServiceMtls();

        group.MapPost("/dispatch-once", DispatchOnceAsync)
            .WithName("DispatchReconciliationOutboxOnce")
            .Produces<DispatchReconciliationOutboxOnceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/pending", ListPendingAsync)
            .WithName("ListPendingReconciliationOutboxEvents")
            .Produces<PendingReconciliationOutboxEventsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> DispatchOnceAsync(
        DispatchReconciliationOutboxOnceRequest request,
        HttpRequest httpRequest,
        IReconciliationOutboxDispatcherService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP DispatchReconciliationOutboxOnce", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalOutboxDispatcherEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("correlation_id", correlationId);

        try
        {
            var result = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(
                    request.Limit ?? 25,
                    request.PublisherServiceIdentityId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("claimed_count", result.ClaimedCount);
            activity?.SetTag("published_count", result.PublishedCount);
            activity?.SetTag("failed_count", result.FailedCount);

            logger.LogInformation(
                "Reconciliation outbox dispatch completed. claimed_count={ClaimedCount} published_count={PublishedCount} failed_count={FailedCount} dead_lettered_count={DeadLetteredCount}",
                result.ClaimedCount,
                result.PublishedCount,
                result.FailedCount,
                result.DeadLetteredCount);

            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Unexpected reconciliation outbox dispatch failure.");
            return Results.Json(
                BuildError(
                    "RECONCILIATION_OUTBOX_DISPATCH_INTERNAL_ERROR",
                    "An unexpected error occurred while dispatching reconciliation outbox events.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ListPendingAsync(
        int? limit,
        HttpRequest httpRequest,
        IReconciliationOutboxDispatcherService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP ListPendingReconciliationOutboxEvents", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalOutboxDispatcherEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var pending = await service.ListPendingAsync(
                new ListPendingReconciliationOutboxQuery(limit ?? 25),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("pending_count", pending.Count);

            return Results.Ok(new PendingReconciliationOutboxEventsResponse(
                pending.Count,
                pending.Select(ToContract).ToArray()));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Unexpected pending reconciliation outbox query failure.");
            return Results.Json(
                BuildError(
                    "RECONCILIATION_OUTBOX_PENDING_INTERNAL_ERROR",
                    "An unexpected error occurred while listing pending reconciliation outbox events.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
        Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;

    private static DispatchReconciliationOutboxOnceResponse ToContract(ReconciliationOutboxDispatchResult result) =>
        new(
            result.RequestedLimit,
            result.ClaimedCount,
            result.PublishedCount,
            result.FailedCount,
            result.DeadLetteredCount,
            result.Items.Select(item => new ReconciliationOutboxDispatchItemDto(
                item.OutboxEventId,
                item.EventPublicationId,
                item.EventType,
                item.Succeeded,
                item.PublicationStatus,
                item.FailureReasonCode,
                item.BrokerMessageId)).ToArray());

    private static PendingReconciliationOutboxEventDto ToContract(ReconciliationOutboxPendingRecord record) =>
        new(
            record.OutboxEventId,
            record.EventType,
            record.AggregateType,
            record.AggregateId,
            record.PublicationStatus,
            record.AvailableAt,
            record.NextRetryAt,
            record.RetryCount,
            record.MaxRetryCount,
            record.CorrelationId,
            record.CausationId);

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
}
