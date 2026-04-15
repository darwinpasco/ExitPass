using System.Diagnostics;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Consumes a single-use exit authorization through the canonical DB-backed control path.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - ExitAuthorization may only be consumed once
/// - Consumption remains DB-backed and deterministic
/// - Consumption requests are fully traceable through correlation metadata
/// </summary>
public sealed class ConsumeExitAuthorizationHandler : IConsumeExitAuthorizationUseCase
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    private readonly IConsumeExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;
    private readonly CentralPmsMetrics _metrics;
    private readonly ILogger<ConsumeExitAuthorizationHandler> _logger;

    public ConsumeExitAuthorizationHandler(
        IConsumeExitAuthorizationGateway gateway,
        ISystemClock systemClock,
        CentralPmsMetrics metrics,
        ILogger<ConsumeExitAuthorizationHandler> logger)
    {
        _gateway = gateway;
        _systemClock = systemClock;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ConsumeExitAuthorizationResult> ExecuteAsync(
        ConsumeExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("ConsumeExitAuthorization", ActivityKind.Internal);

        activity?.SetTag("operation", "consume_exit_authorization");
        activity?.SetTag("exit_authorization_id", command.ExitAuthorizationId);
        activity?.SetTag("requested_by_user_id", command.RequestedByUserId);
        activity?.SetTag("correlation_id", command.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["exit_authorization_id"] = command.ExitAuthorizationId,
            ["requested_by_user_id"] = command.RequestedByUserId,
            ["correlation_id"] = command.CorrelationId
        });

        _logger.LogInformation("ConsumeExitAuthorization started.");

        try
        {
            ValidateCommand(command);

            var dbStart = _systemClock.UtcNow;

            var dbResult = await _gateway.ConsumeAsync(
                new ConsumeExitAuthorizationDbRequest
                {
                    ExitAuthorizationId = command.ExitAuthorizationId,
                    RequestedByUserId = command.RequestedByUserId,
                    CorrelationId = command.CorrelationId,
                    RequestedAt = _systemClock.UtcNow
                },
                cancellationToken);

            var dbDuration = _systemClock.UtcNow - dbStart;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("authorization_status", dbResult.AuthorizationStatus);
            activity?.SetTag("consumed_at", dbResult.ConsumedAt);
            activity?.SetTag("db.duration_ms", dbDuration.TotalMilliseconds);

            _metrics.ExitAuthorizationConsumeOutcome("CONSUMED", "SUCCESS");

            _logger.LogInformation(
                "Exit authorization consumed successfully. exit_authorization_id={ExitAuthorizationId} authorization_status={AuthorizationStatus}",
                dbResult.ExitAuthorizationId,
                dbResult.AuthorizationStatus);

            return new ConsumeExitAuthorizationResult(
                ExitAuthorizationId: dbResult.ExitAuthorizationId,
                AuthorizationStatus: dbResult.AuthorizationStatus,
                ConsumedAt: dbResult.ConsumedAt);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("rejection_reason", "INVALID_REQUEST");

            _metrics.ExitAuthorizationConsumeOutcome("REJECTED", "INVALID_REQUEST");
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogWarning(
                ex,
                "ConsumeExitAuthorization rejected because the command is invalid.");

            throw;
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("rejection_reason", ex.GetType().Name);

            _metrics.ExitAuthorizationConsumeOutcome("REJECTED", ex.GetType().Name);
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogWarning(
                ex,
                "ConsumeExitAuthorization was rejected by deterministic business rules.");

            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _metrics.ExitAuthorizationConsumeOutcome("FAILED", "UNEXPECTED_FAILURE");
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogError(
                ex,
                "Unexpected failure while consuming exit authorization. exit_authorization_id={ExitAuthorizationId}",
                command.ExitAuthorizationId);

            throw;
        }
    }

    private static void ValidateCommand(ConsumeExitAuthorizationCommand command)
    {
        if (command.ExitAuthorizationId == Guid.Empty)
        {
            throw new ArgumentException("ExitAuthorizationId is required.", nameof(command));
        }

        if (command.RequestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(command));
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId is required.", nameof(command));
        }
    }
}
