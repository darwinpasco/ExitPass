using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Gate-facing endpoints for consuming exit authorizations.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - A valid authorization may be consumed only once.
/// - Gate consume requests must carry an active DEVICE service identity and active gate assignment.
/// - Business conflicts must be distinguished from unexpected server failures.
/// - Trace metadata must be preserved at the HTTP boundary.
/// </summary>
public static class GateExitAuthorizationConsumeEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api");

    /// <summary>
    /// Maps gate-facing exit authorization consume endpoints.
    /// </summary>
    /// <param name="app">Route builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapGateExitAuthorizationConsumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/gate/authorizations")
            .WithTags("GateAuthorizations")
            .RequireInternalServiceMtls();

        group.MapPost("/{exitAuthorizationId:guid}/consume", HandleAsync)
            .WithName("ConsumeExitAuthorization")
            .Produces<ConsumeExitAuthorizationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Consumes a previously issued exit authorization.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid exitAuthorizationId,
        HttpRequest request,
        ConsumeExitAuthorizationRequest body,
        IConsumeExitAuthorizationUseCase useCase,
        IGateDeviceIdentityValidator gateDeviceIdentityValidator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GateConsumeEndpoint");

        using var activity = ActivitySource.StartActivity("HTTP ConsumeExitAuthorization", ActivityKind.Server);

        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "X-Correlation-Id header is required.");
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "X-Correlation-Id header is required.",
                Guid.Empty,
                retryable: false));
        }

        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("exit_authorization_id", exitAuthorizationId);

        if (body.RequestedByUserId == Guid.Empty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "RequestedByUserId is required.");
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "RequestedByUserId is required.",
                correlationId,
                retryable: false));
        }

        if (!request.Headers.TryGetValue("X-Service-Identity-Id", out var serviceIdentityHeader) ||
            !Guid.TryParse(serviceIdentityHeader.ToString(), out var serviceIdentityId) ||
            serviceIdentityId == Guid.Empty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "X-Service-Identity-Id header is required.");
            activity?.SetTag("failure_class", "SECURITY_REJECTION");
            activity?.SetTag("error_code", "SERVICE_IDENTITY_REQUIRED");

            return Results.Json(
                BuildError(
                    "SERVICE_IDENTITY_REQUIRED",
                    "X-Service-Identity-Id header is required.",
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!request.Headers.TryGetValue("X-Gate-Device-Id", out var gateDeviceHeader) ||
            string.IsNullOrWhiteSpace(gateDeviceHeader.ToString()))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "X-Gate-Device-Id header is required.");
            activity?.SetTag("failure_class", "SECURITY_REJECTION");
            activity?.SetTag("error_code", "GATE_DEVICE_IDENTITY_REQUIRED");

            return Results.Json(
                BuildError(
                    "GATE_DEVICE_IDENTITY_REQUIRED",
                    "X-Gate-Device-Id header is required.",
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (body.RequestedByUserId != serviceIdentityId)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "RequestedByUserId does not match caller service identity.");
            activity?.SetTag("failure_class", "SECURITY_REJECTION");
            activity?.SetTag("error_code", "SERVICE_IDENTITY_MISMATCH");

            return Results.Json(
                BuildError(
                    "SERVICE_IDENTITY_MISMATCH",
                    "RequestedByUserId must match X-Service-Identity-Id for gate consume requests.",
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var identityValidation = await gateDeviceIdentityValidator.ValidateConsumeAsync(
            new GateDeviceIdentityValidationRequest(
                exitAuthorizationId,
                gateDeviceHeader.ToString(),
                serviceIdentityId,
                correlationId),
            cancellationToken);

        activity?.SetTag("service_identity_id", serviceIdentityId);
        activity?.SetTag("gate_device_identifier", gateDeviceHeader.ToString());
        activity?.SetTag("gate_device_identity_result", identityValidation.ResultCode);
        if (identityValidation.GateDeviceId.HasValue)
        {
            activity?.SetTag("gate_device_id", identityValidation.GateDeviceId.Value);
        }

        if (!identityValidation.IsAuthorized)
        {
            activity?.SetStatus(ActivityStatusCode.Error, identityValidation.ResultCode);
            activity?.SetTag("failure_class", "SECURITY_REJECTION");
            activity?.SetTag("error_code", identityValidation.ResultCode);

            var error = BuildError(
                identityValidation.ResultCode,
                identityValidation.Message,
                correlationId,
                retryable: false);

            logger.LogWarning(
                "Gate consume rejected before DB consume. error_code={ErrorCode} gate_device_identifier={GateDeviceIdentifier} service_identity_id={ServiceIdentityId}",
                identityValidation.ResultCode,
                gateDeviceHeader.ToString(),
                serviceIdentityId);

            return identityValidation.ResultCode switch
            {
                "EXIT_AUTHORIZATION_NOT_FOUND" => Results.NotFound(error),
                _ => Results.Json(error, statusCode: StatusCodes.Status403Forbidden)
            };
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["exit_authorization_id"] = exitAuthorizationId,
            ["service_identity_id"] = serviceIdentityId,
            ["gate_device_id"] = identityValidation.GateDeviceId,
            ["site_id"] = identityValidation.SiteId,
            ["lane_id"] = identityValidation.LaneId
        });

        logger.LogInformation("HTTP ConsumeExitAuthorization request received.");

        try
        {
            var result = await useCase.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    exitAuthorizationId,
                    body.RequestedByUserId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);
            activity?.SetTag("consumed_at", result.ConsumedAt);

            logger.LogInformation(
                "Exit authorization consumed. exit_authorization_id={ExitAuthorizationId}",
                result.ExitAuthorizationId);

            return Results.Ok(new ConsumeExitAuthorizationResponse(
                result.ExitAuthorizationId,
                result.AuthorizationStatus,
                result.ConsumedAt));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            logger.LogWarning(ex, "Invalid consume request.");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_NOT_FOUND");

            logger.LogWarning(ex, "Exit authorization not found.");

            return Results.NotFound(BuildError(
                "EXIT_AUTHORIZATION_NOT_FOUND",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (KeyNotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_NOT_FOUND");

            logger.LogWarning(ex, "Exit authorization not found.");

            return Results.NotFound(BuildError(
                "EXIT_AUTHORIZATION_NOT_FOUND",
                "Exit authorization was not found.",
                correlationId,
                retryable: false));
        }
        catch (ExitAuthorizationConsumeConflictException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", ex.ErrorCode);

            logger.LogWarning(
                ex,
                "Exit authorization consume rejected by pre-persistence validation. error_code={ErrorCode}",
                ex.ErrorCode);

            return Results.Conflict(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "P0001" &&
            ex.MessageText.Contains("is expired", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_EXPIRED");

            logger.LogWarning(ex, "Exit authorization expired.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_EXPIRED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "P0001" &&
            ex.MessageText.Contains("already been consumed", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_ALREADY_CONSUMED");

            logger.LogWarning(ex, "Exit authorization already consumed.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "P0001" &&
            ex.MessageText.Contains("not issued", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_REJECTED");

            logger.LogWarning(ex, "Exit authorization is not in an issued state.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_REJECTED");

            logger.LogWarning(ex, "Exit authorization consume was rejected by the database control path.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_REJECTED");

            logger.LogWarning(ex, "Consume rejected by deterministic business rule.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Exception ex) when (HasExitAuthorizationNotIssuedMessage(ex))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_REJECTED");

            logger.LogWarning(ex, "Exit authorization is not in an issued state.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                "Exit authorization is not in an issued state.",
                correlationId,
                retryable: false));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "SYSTEM_FAILURE");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_INTERNAL_ERROR");

            logger.LogError(ex, "Unexpected failure.");

            return Results.Json(
                BuildError(
                    "EXIT_AUTHORIZATION_CONSUME_INTERNAL_ERROR",
                    "An unexpected error occurred while consuming the exit authorization.",
                    correlationId,
                    retryable: false),
                 statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static bool HasExitAuthorizationNotIssuedMessage(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("exit authorization", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("not issued", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable,
        Dictionary<string, object?>? details = null)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable,
            Details = details
        };
    }

    /// <summary>
    /// Consume request body.
    /// </summary>
    public sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    /// <summary>
    /// Consume response body.
    /// </summary>
    public sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
