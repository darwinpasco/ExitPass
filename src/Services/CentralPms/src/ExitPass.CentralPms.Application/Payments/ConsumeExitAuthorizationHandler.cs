using System.Diagnostics;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
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
    /// <summary>
    /// Activity source for consume-exit-authorization application spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    private readonly IConsumeExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;
    private readonly CentralPmsMetrics _metrics;
    private readonly ILogger<ConsumeExitAuthorizationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumeExitAuthorizationHandler"/> class.
    /// </summary>
    /// <param name="gateway">Gateway for the canonical DB-backed consume routine.</param>
    /// <param name="systemClock">System clock used for deterministic timestamps and durations.</param>
    /// <param name="metrics">Metrics recorder for consume outcome telemetry.</param>
    /// <param name="logger">Structured application logger.</param>
    public ConsumeExitAuthorizationHandler(
        IConsumeExitAuthorizationGateway gateway,
        ISystemClock systemClock,
        CentralPmsMetrics metrics,
        ILogger<ConsumeExitAuthorizationHandler> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the consume-exit-authorization use case through the canonical database routine.
    /// </summary>
    /// <param name="command">The consume command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deterministic consume result.</returns>
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
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
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
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("rejection_reason", ex.GetType().Name);

            _metrics.ExitAuthorizationConsumeOutcome("REJECTED", ex.GetType().Name);
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogWarning(
                ex,
                "ConsumeExitAuthorization was rejected by deterministic business rules.");

            throw;
        }
        catch (PostgresException ex) when (IsDeterministicBusinessRejection(ex))
        {
            var rejectionCode = ResolveBusinessRejectionCode(ex);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("rejection_reason", rejectionCode);

            _metrics.ExitAuthorizationConsumeOutcome("REJECTED", rejectionCode);
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogWarning(
                ex,
                "ConsumeExitAuthorization was rejected by deterministic database business rules. rejection_reason={RejectionReason}",
                rejectionCode);

            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "SYSTEM_FAILURE");

            _metrics.ExitAuthorizationConsumeOutcome("FAILED", "UNEXPECTED_FAILURE");
            _metrics.ExceptionObserved(ex.GetType().Name, "CONSUME_EXIT_AUTHORIZATION");

            _logger.LogError(
                ex,
                "Unexpected failure while consuming exit authorization. exit_authorization_id={ExitAuthorizationId}",
                command.ExitAuthorizationId);

            throw;
        }
    }

    /// <summary>
    /// Validates the consume command before it reaches the database boundary.
    /// </summary>
    /// <param name="command">The consume command to validate.</param>
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

    /// <summary>
    /// Determines whether the database exception represents a deterministic business rejection
    /// rather than an unexpected system failure.
    /// </summary>
    /// <param name="exception">The PostgreSQL exception.</param>
    /// <returns><see langword="true"/> when the exception is a deterministic business rejection.</returns>
    private static bool IsDeterministicBusinessRejection(PostgresException exception)
    {
        return exception.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.NoDataFound;
    }

    /// <summary>
    /// Resolves the business rejection code used for telemetry and structured logging.
    /// </summary>
    /// <param name="exception">The PostgreSQL exception.</param>
    /// <returns>The deterministic rejection code.</returns>
    private static string ResolveBusinessRejectionCode(PostgresException exception)
    {
        if (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return "EXIT_AUTHORIZATION_NOT_FOUND";
        }

        if (exception.MessageText.Contains("already been consumed", StringComparison.OrdinalIgnoreCase))
        {
            return "EXIT_AUTHORIZATION_ALREADY_CONSUMED";
        }

        if (exception.MessageText.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "EXIT_AUTHORIZATION_EXPIRED";
        }

        return "EXIT_AUTHORIZATION_REJECTED";
    }
}
