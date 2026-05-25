using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Eventing;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for event dead-letter recovery and consumer checkpoint operations.
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
/// - Event recovery endpoints mutate only events-owned recovery tables.
/// - Replay and checkpoint operations never mutate payment, provider, exit, gate, or settlement truth.
/// </summary>
public static class InternalEventRecoveryEndpoints
{
    private const string DeadLetterViewerPolicy = "EventRecoveryViewer";
    private const string DeadLetterReplayPolicy = "EventDeadLetterReplayer";
    private const string CheckpointViewerPolicy = "EventCheckpointViewer";
    private const string CheckpointOperatorPolicy = "EventCheckpointOperator";

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.EventRecovery");

    public static IEndpointRouteBuilder MapInternalEventRecoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/events")
            .WithTags("InternalEvents")
            .RequireInternalServiceMtls();

        group.MapGet("/dead-letters", ListDeadLettersAsync)
            .WithName("ListEventDeadLetters")
            .WithMetadata(new ReconciliationPolicyMetadata(DeadLetterViewerPolicy))
            .Produces<DeadLetterRecordsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/dead-letters/{deadLetterRecordId:guid}", GetDeadLetterAsync)
            .WithName("GetEventDeadLetter")
            .WithMetadata(new ReconciliationPolicyMetadata(DeadLetterViewerPolicy))
            .Produces<DeadLetterRecordResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/dead-letters/{deadLetterRecordId:guid}/replay", RequestDeadLetterReplayAsync)
            .WithName("RequestEventDeadLetterReplay")
            .WithMetadata(new ReconciliationPolicyMetadata(DeadLetterReplayPolicy))
            .Produces<DeadLetterReplayResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/dead-letters/{deadLetterRecordId:guid}/replay/outcome", MarkDeadLetterReplayOutcomeAsync)
            .WithName("MarkEventDeadLetterReplayOutcome")
            .WithMetadata(new ReconciliationPolicyMetadata(DeadLetterReplayPolicy))
            .Produces<DeadLetterReplayResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/consumer-checkpoints", ListConsumerCheckpointsAsync)
            .WithName("ListEventConsumerCheckpoints")
            .WithMetadata(new ReconciliationPolicyMetadata(CheckpointViewerPolicy))
            .Produces<ConsumerCheckpointsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/consumer-checkpoints/{consumerName}", GetConsumerCheckpointAsync)
            .WithName("GetEventConsumerCheckpoint")
            .WithMetadata(new ReconciliationPolicyMetadata(CheckpointViewerPolicy))
            .Produces<ConsumerCheckpointResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/consumer-checkpoints/{consumerName}/status", UpdateConsumerCheckpointStatusAsync)
            .WithName("UpdateEventConsumerCheckpointStatus")
            .WithMetadata(new ReconciliationPolicyMetadata(CheckpointOperatorPolicy))
            .Produces<ConsumerCheckpointResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ListDeadLettersAsync(
        int? limit,
        string? status,
        string? consumerName,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP ListEventDeadLetters", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var records = await service.ListDeadLettersAsync(
                new ListDeadLettersQuery(limit ?? 25, status, consumerName),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new DeadLetterRecordsResponse(records.Count, records.Select(ToContract).ToArray()));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_DEAD_LETTER_LIST_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> GetDeadLetterAsync(
        Guid deadLetterRecordId,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetEventDeadLetter", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var record = await service.GetDeadLetterAsync(new GetDeadLetterQuery(deadLetterRecordId), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new DeadLetterRecordResponse(ToContract(record)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEAD_LETTER_RECORD_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Dead-letter record was not found.", correlationId));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_DEAD_LETTER_READ_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> RequestDeadLetterReplayAsync(
        Guid deadLetterRecordId,
        RequestDeadLetterReplayRequest body,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP RequestEventDeadLetterReplay", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var record = await service.RequestDeadLetterReplayAsync(
                new RequestDeadLetterReplayCommand(
                    deadLetterRecordId,
                    body.RequestedByUserId,
                    body.RequestedByServiceIdentityId,
                    body.ReasonCode,
                    correlationId == Guid.Empty ? null : correlationId),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToReplayContract(record));
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEAD_LETTER_RECORD_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Dead-letter record was not found.", correlationId));
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEAD_LETTER_REPLAY_NOT_ALLOWED")
        {
            return Results.Conflict(BuildError(ex.Message, "Dead-letter replay cannot be requested for the current status.", correlationId));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_DEAD_LETTER_REPLAY_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> MarkDeadLetterReplayOutcomeAsync(
        Guid deadLetterRecordId,
        MarkDeadLetterReplayOutcomeRequest body,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP MarkEventDeadLetterReplayOutcome", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var record = await service.MarkDeadLetterReplayOutcomeAsync(
                new MarkDeadLetterReplayOutcomeCommand(
                    deadLetterRecordId,
                    body.OutcomeStatus,
                    body.ResolvedByUserId,
                    body.ResolvedByServiceIdentityId,
                    body.ReasonCode,
                    correlationId == Guid.Empty ? null : correlationId),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToReplayContract(record));
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEAD_LETTER_RECORD_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Dead-letter record was not found.", correlationId));
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEAD_LETTER_REPLAY_OUTCOME_NOT_ALLOWED")
        {
            return Results.Conflict(BuildError(ex.Message, "Dead-letter replay outcome cannot be marked from the current status.", correlationId));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError(ex.Message, ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_DEAD_LETTER_REPLAY_OUTCOME_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> ListConsumerCheckpointsAsync(
        int? limit,
        string? status,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP ListEventConsumerCheckpoints", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var records = await service.ListConsumerCheckpointsAsync(
                new ListConsumerCheckpointsQuery(limit ?? 25, status),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new ConsumerCheckpointsResponse(records.Count, records.Select(ToContract).ToArray()));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_CONSUMER_CHECKPOINT_LIST_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> GetConsumerCheckpointAsync(
        string consumerName,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetEventConsumerCheckpoint", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var record = await service.GetConsumerCheckpointAsync(new GetConsumerCheckpointQuery(consumerName), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new ConsumerCheckpointResponse(ToContract(record)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CONSUMER_CHECKPOINT_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Consumer checkpoint was not found.", correlationId));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CONSUMER_CHECKPOINT_AMBIGUOUS")
        {
            return Results.Conflict(BuildError(ex.Message, "Consumer checkpoint lookup matched more than one scope.", correlationId));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_CONSUMER_CHECKPOINT_READ_INTERNAL_ERROR");
        }
    }

    private static async Task<IResult> UpdateConsumerCheckpointStatusAsync(
        string consumerName,
        UpdateConsumerCheckpointStatusRequest body,
        HttpRequest request,
        IEventRecoveryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP UpdateEventConsumerCheckpointStatus", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        try
        {
            var record = await service.UpdateConsumerCheckpointStatusAsync(
                new UpdateConsumerCheckpointStatusCommand(
                    consumerName,
                    body.CheckpointStatus,
                    body.UpdatedByServiceIdentityId,
                    body.FailureReasonCode,
                    correlationId == Guid.Empty ? null : correlationId),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new ConsumerCheckpointResponse(ToContract(record)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CONSUMER_CHECKPOINT_NOT_FOUND")
        {
            return Results.NotFound(BuildError(ex.Message, "Consumer checkpoint was not found.", correlationId));
        }
        catch (InvalidOperationException ex) when (ex.Message is "CONSUMER_CHECKPOINT_AMBIGUOUS" or "CONSUMER_CHECKPOINT_STATUS_UPDATE_NOT_ALLOWED" or "CONSUMER_CHECKPOINT_TERMINAL")
        {
            return Results.Conflict(BuildError(ex.Message, "Consumer checkpoint status update is not allowed.", correlationId));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            return InternalError(ex, loggerFactory, correlationId, "EVENT_CONSUMER_CHECKPOINT_STATUS_INTERNAL_ERROR");
        }
    }

    private static DeadLetterRecordDto ToContract(DeadLetterRecord record) =>
        new(
            record.DeadLetterRecordId,
            record.OutboxEventId,
            record.EventPublicationId,
            record.ConsumerName,
            record.DeadLetterType,
            record.DeadLetterStatus,
            record.FailureReasonCode,
            record.FailureDetailRef,
            record.PayloadHash,
            record.DeadLetteredAt,
            record.ReplayRequestedAt,
            record.ResolvedAt,
            record.ResolutionReasonCode,
            record.CorrelationId,
            record.CreatedAt,
            record.UpdatedAt);

    private static DeadLetterReplayResponse ToReplayContract(DeadLetterRecord record) =>
        new(
            record.DeadLetterRecordId,
            record.DeadLetterStatus,
            record.ReplayRequestedAt,
            record.ResolvedAt,
            record.CorrelationId);

    private static ConsumerCheckpointDto ToContract(ConsumerCheckpointRecord record) =>
        new(
            record.ConsumerCheckpointId,
            record.ConsumerName,
            record.ConsumerGroup,
            record.SubscriptionName,
            record.EventType,
            record.AggregateType,
            record.LastOutboxEventId,
            record.LastDomainEventId,
            record.LastBrokerOffset,
            record.CheckpointStatus,
            record.ProcessedCount,
            record.FailureCount,
            record.LastProcessedAt,
            record.LastFailedAt,
            record.FailureReasonCode,
            record.LockedAt,
            record.LockedByServiceIdentityId,
            record.UpdatedByServiceIdentityId,
            record.CreatedAt,
            record.UpdatedAt,
            record.CorrelationId);

    private static IResult InternalError(
        Exception exception,
        ILoggerFactory loggerFactory,
        Guid correlationId,
        string errorCode)
    {
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalEventRecoveryEndpoints");
        logger.LogError(exception, "Unexpected internal event recovery failure. error_code={ErrorCode}", errorCode);
        return Results.Json(
            BuildError(errorCode, "An unexpected error occurred while processing the event recovery request.", correlationId),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
        Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
}
