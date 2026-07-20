using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Primitives;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal disabled-by-default endpoint for controlled execution of one REQUESTED OPEN_GATE command.
/// </summary>
public static class InternalControlledGateExecutionEndpoints
{
    private const string RequiredConfirmation = "OPEN_GATE";
    private const int MaximumCorrelationHeaderLength = 128;
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.ControlledGateExecution");

    /// <summary>
    /// Maps the controlled internal execution endpoint only when explicitly enabled.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalControlledGateExecutionEndpoints(
        this IEndpointRouteBuilder app,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = ReadControlledOptions(configuration);
        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid {HikCentralControlledGateExecutionOptions.SectionName} configuration: {string.Join(", ", validationErrors)}.");
        }

        if (!options.Enabled)
        {
            return app;
        }

        var integrationOptions = ReadGateIntegrationOptions(configuration);
        if (!integrationOptions.Enabled)
        {
            throw new InvalidOperationException(
                $"Invalid {HikCentralControlledGateExecutionOptions.SectionName} configuration: HIKCENTRAL_GATE_INTEGRATION_REQUIRED.");
        }

        var integrationErrors = integrationOptions.Validate();
        if (integrationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid {HikCentralControlledGateExecutionOptions.SectionName} configuration: HIKCENTRAL_GATE_INTEGRATION_INVALID.");
        }

        var group = app.MapGroup("/v1/internal/gates")
            .WithTags("InternalGates")
            .RequireInternalServiceMtls();

        group.MapPost("/commands/{gateCommandId}/execute", ExecuteAsync)
            .WithName("ControlledExecuteGateCommand")
            .Produces<ControlledGateCommandExecutionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ControlledGateCommandExecutionResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Execute one controlled gate command")
            .WithDescription("Executes exactly one explicitly selected REQUESTED OPEN_GATE command through the existing gate command execution service. This endpoint is disabled by default, requires internal mTLS metadata, performs no command discovery or retry execution, and does not claim that a physical gate opened.");

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        string gateCommandId,
        HttpRequest httpRequest,
        ControlledGateExecutionRequest? body,
        IGateCommandExecutionService executionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP ControlledExecuteGateCommand", ActivityKind.Server);
        activity?.SetTag("http.route", "/v1/internal/gates/commands/{gateCommandId}/execute");

        if (!TryReadCorrelationId(httpRequest.Headers, out var correlationId, out var correlationError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, correlationError);
            return Results.BadRequest(BuildError("INVALID_REQUEST", correlationError, correlationId));
        }

        activity?.SetTag("correlation_id", correlationId);

        if (!Guid.TryParse(gateCommandId, out var parsedGateCommandId) ||
            parsedGateCommandId == Guid.Empty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Gate command id is invalid.");
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "gateCommandId must be a valid non-empty GUID.",
                correlationId));
        }

        activity?.SetTag("gate_command_id", parsedGateCommandId);

        if (body is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request body is required.");
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "Request body is required.",
                correlationId));
        }

        if (body.UnexpectedFields is { Count: > 0 })
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request contains unsupported fields.");
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "Request contains unsupported fields.",
                correlationId));
        }

        if (!string.Equals(body.Confirmation, RequiredConfirmation, StringComparison.Ordinal))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Explicit OPEN_GATE confirmation is required.");
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "Confirmation must exactly match OPEN_GATE.",
                correlationId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await executionService.ExecuteAsync(parsedGateCommandId, cancellationToken)
                .ConfigureAwait(false);

            var response = ToResponse(result, correlationId);
            activity?.SetTag("execution_outcome", response.ExecutionClassification);
            activity?.SetTag("command_status", response.CommandStatus);

            return Results.Json(response, statusCode: ToHttpStatus(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request was cancelled.");
            return Results.Json(
                BuildError(
                    "REQUEST_CANCELLED",
                    "The controlled gate command execution request was cancelled.",
                    correlationId,
                    retryable: false),
                statusCode: 499);
        }
        catch (InvalidOperationException ex)
        {
            var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalControlledGateExecutionEndpoints");
            logger.LogError(ex, "Controlled gate command execution failed closed.");
            activity?.SetStatus(ActivityStatusCode.Error, "Controlled execution integration is unavailable.");
            activity?.AddException(ex);

            return Results.Json(
                BuildError(
                    "CONTROLLED_GATE_EXECUTION_UNAVAILABLE",
                    "Controlled gate command execution is unavailable.",
                    correlationId,
                    retryable: true),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static int ToHttpStatus(GateCommandExecutionResult result)
    {
        if (result.Outcome == GateCommandExecutionOutcome.Executed)
        {
            return StatusCodes.Status200OK;
        }

        if (string.Equals(result.ErrorCode, "GATE_COMMAND_NOT_FOUND", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        return StatusCodes.Status409Conflict;
    }

    private static ControlledGateCommandExecutionResponse ToResponse(
        GateCommandExecutionResult result,
        Guid requestCorrelationId) =>
        new(
            result.GateCommandId,
            result.Outcome.ToString(),
            result.CommandStatus,
            result.AdapterInvoked,
            IsRetryableCommandStatus(result.CommandStatus),
            result.HikCentralGateActionAuditId,
            result.ErrorCode,
            result.Message,
            requestCorrelationId,
            DateTimeOffset.UtcNow);

    private static bool IsRetryableCommandStatus(string? commandStatus) =>
        string.Equals(commandStatus, "RETRYABLE", StringComparison.Ordinal);

    private static bool TryReadCorrelationId(
        IHeaderDictionary headers,
        out Guid correlationId,
        out string errorMessage)
    {
        correlationId = Guid.Empty;

        if (!headers.TryGetValue("X-Correlation-Id", out StringValues headerValue) ||
            StringValues.IsNullOrEmpty(headerValue))
        {
            errorMessage = "X-Correlation-Id header is required.";
            return false;
        }

        var raw = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Length > MaximumCorrelationHeaderLength ||
            raw.Contains('\r', StringComparison.Ordinal) ||
            raw.Contains('\n', StringComparison.Ordinal) ||
            !Guid.TryParse(raw, out correlationId) ||
            correlationId == Guid.Empty)
        {
            correlationId = Guid.Empty;
            errorMessage = "X-Correlation-Id header must be a valid non-empty GUID.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable = false) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private static HikCentralControlledGateExecutionOptions ReadControlledOptions(IConfiguration configuration)
    {
        var options = new HikCentralControlledGateExecutionOptions();
        configuration.GetSection(HikCentralControlledGateExecutionOptions.SectionName).Bind(options);
        return options;
    }

    private static HikCentralGateIntegrationOptions ReadGateIntegrationOptions(IConfiguration configuration)
    {
        var options = new HikCentralGateIntegrationOptions();
        configuration.GetSection(HikCentralGateIntegrationOptions.SectionName).Bind(options);
        return options;
    }

    public sealed class ControlledGateExecutionRequest
    {
        public string? Confirmation { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnexpectedFields { get; set; }
    }

    public sealed record ControlledGateCommandExecutionResponse(
        Guid GateCommandId,
        string ExecutionClassification,
        string CommandStatus,
        bool AdapterInvoked,
        bool Retryable,
        Guid? HikCentralGateActionAuditId,
        string? ErrorCode,
        string? Message,
        Guid RequestCorrelationId,
        DateTimeOffset RespondedAt);
}
