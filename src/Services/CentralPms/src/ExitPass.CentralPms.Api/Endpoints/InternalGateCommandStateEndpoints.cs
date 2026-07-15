using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Contracts.Common;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal read-only endpoints for canonical gate command state inventory.
/// </summary>
public static class InternalGateCommandStateEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.GateCommandState");

    /// <summary>
    /// Maps the internal read-only gate command state endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalGateCommandStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/gates")
            .WithTags("InternalGates")
            .RequireInternalServiceMtls();

        group.MapGet(
                "/authorization-consumptions/{gateAuthorizationConsumptionId:guid}/command-state",
                GetCommandStateAsync)
            .WithName("GetGateAuthorizationConsumptionCommandState")
            .Produces<GateCommandStateResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Get gate command state")
            .WithDescription("Reads canonical gate consumption, consumed-processing, command lifecycle, retry/failure, and HikCentral attempt audit state. This endpoint is read-only and does not imply that a physical gate opened.");

        return app;
    }

    private static async Task<IResult> GetCommandStateAsync(
        Guid gateAuthorizationConsumptionId,
        HttpRequest request,
        IGateCommandStateReadRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetGateCommandState", ActivityKind.Server);
        var correlationId = ResolveCorrelationId(request);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalGateCommandStateEndpoints");

        activity?.SetTag("http.route", "/v1/internal/gates/authorization-consumptions/{gateAuthorizationConsumptionId}/command-state");
        activity?.SetTag("gate_authorization_consumption_id", gateAuthorizationConsumptionId);
        activity?.SetTag("correlation_id", correlationId);

        if (gateAuthorizationConsumptionId == Guid.Empty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Gate authorization consumption id is required.");
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "gateAuthorizationConsumptionId is required.",
                correlationId));
        }

        try
        {
            var state = await repository.GetByConsumptionIdAsync(gateAuthorizationConsumptionId, cancellationToken);
            if (state is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Gate authorization consumption was not found.");
                return Results.NotFound(BuildError(
                    "GATE_AUTHORIZATION_CONSUMPTION_NOT_FOUND",
                    "Gate authorization consumption was not found.",
                    correlationId));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToResponse(state));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected failure reading gate command state. gate_authorization_consumption_id={GateAuthorizationConsumptionId}",
                gateAuthorizationConsumptionId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            return Results.Json(
                BuildError(
                    "GATE_COMMAND_STATE_READ_INTERNAL_ERROR",
                    "An unexpected error occurred while reading gate command state.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static GateCommandStateResponse ToResponse(GateCommandStateReadModel state) =>
        new(
            ToResponse(state.Consumption),
            state.ConsumedProcessing is null ? null : ToResponse(state.ConsumedProcessing),
            state.GateCommand is null ? null : ToResponse(state.GateCommand),
            state.HikCentralActionAttempts.Select(ToResponse).ToArray());

    private static GateAuthorizationConsumptionResponse ToResponse(GateAuthorizationConsumptionReadModel consumption) =>
        new(
            consumption.GateAuthorizationConsumptionId,
            consumption.ExitAuthorizationId,
            consumption.GateDeviceId,
            consumption.SiteId,
            consumption.LaneId,
            consumption.ConsumeStatus,
            consumption.ConsumedAt,
            consumption.CorrelationId);

    private static GateAuthorizationConsumedProcessingResponse ToResponse(GateAuthorizationConsumedProcessingReadModel processing) =>
        new(
            processing.ProcessingId,
            processing.ProcessingKey,
            processing.EventId,
            processing.EventType,
            processing.ProcessingStatus,
            processing.ProcessingResult,
            processing.AttemptCount,
            processing.FirstAttemptedAt,
            processing.LastAttemptedAt,
            processing.ProcessedAt,
            processing.FailureCode,
            processing.FailureReason);

    private static GateCommandResponse ToResponse(GateCommandReadModel command) =>
        new(
            command.CommandId,
            command.CommandType,
            command.CommandStatus,
            command.AttemptCount,
            command.MaxAttempts,
            command.RetryPolicyCode,
            command.RequestedAt,
            command.StartedAt,
            command.LastAttemptedAt,
            command.NextAttemptAt,
            command.CompletedAt,
            command.TerminalFailureAt,
            command.FailureCode,
            command.FailureReason,
            command.LastFailureCode,
            command.LastFailureReason);

    private static HikCentralGateActionAttemptResponse ToResponse(HikCentralGateActionAuditReadModel attempt) =>
        new(
            attempt.HikCentralGateActionAuditId,
            attempt.VendorCode,
            attempt.VendorOperation,
            attempt.DoorIndexCode,
            attempt.RequestMethod,
            attempt.RequestPath,
            attempt.RequestHash,
            attempt.SignedHeaderNames,
            attempt.RequestCorrelationId,
            attempt.VendorCorrelationId,
            attempt.HttpStatusCode,
            attempt.VendorResultCode,
            attempt.VendorResultMessage,
            attempt.ActionOutcome,
            attempt.Retryable,
            attempt.FailureRecorded,
            attempt.DurationMs,
            attempt.TimedOut,
            attempt.VendorUnavailable,
            attempt.TransportFailure,
            attempt.RequestedAt,
            attempt.RespondedAt);

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

    public sealed record GateCommandStateResponse(
        GateAuthorizationConsumptionResponse Consumption,
        GateAuthorizationConsumedProcessingResponse? ConsumedProcessing,
        GateCommandResponse? GateCommand,
        IReadOnlyList<HikCentralGateActionAttemptResponse> HikCentralActionAttempts);

    public sealed record GateAuthorizationConsumptionResponse(
        Guid GateAuthorizationConsumptionId,
        Guid? ExitAuthorizationId,
        Guid? GateDeviceId,
        Guid SiteId,
        Guid? LaneId,
        string ConsumeStatus,
        DateTimeOffset? ConsumedAt,
        Guid? CorrelationId);

    public sealed record GateAuthorizationConsumedProcessingResponse(
        Guid ProcessingId,
        Guid ProcessingKey,
        Guid? EventId,
        string EventType,
        string ProcessingStatus,
        string ProcessingResult,
        int AttemptCount,
        DateTimeOffset FirstAttemptedAt,
        DateTimeOffset? LastAttemptedAt,
        DateTimeOffset? ProcessedAt,
        string? FailureCode,
        string? FailureReason);

    public sealed record GateCommandResponse(
        Guid CommandId,
        string CommandType,
        string CommandStatus,
        int AttemptCount,
        int MaxAttempts,
        string RetryPolicyCode,
        DateTimeOffset RequestedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset LastAttemptedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? FailureReason,
        string? LastFailureCode,
        string? LastFailureReason);

    public sealed record HikCentralGateActionAttemptResponse(
        Guid HikCentralGateActionAuditId,
        string VendorCode,
        string VendorOperation,
        string DoorIndexCode,
        string RequestMethod,
        string RequestPath,
        string RequestHash,
        string SignedHeaderNames,
        Guid RequestCorrelationId,
        string? VendorCorrelationId,
        int? HttpStatusCode,
        string? VendorResultCode,
        string? VendorResultMessage,
        string ActionOutcome,
        bool Retryable,
        bool FailureRecorded,
        int DurationMs,
        bool TimedOut,
        bool VendorUnavailable,
        bool TransportFailure,
        DateTimeOffset RequestedAt,
        DateTimeOffset RespondedAt);
}
