using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    /// <summary>
    /// Activity source for exit authorization consumption spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    /// <summary>
    /// Metrics meter for exit authorization consumption.
    /// </summary>
    private static readonly Meter Meter =
        new("ExitPass.CentralPms.Application.Payments", "1.0.0");

    /// <summary>
    /// Counts successful exit authorization consumptions.
    /// </summary>
    private static readonly Counter<long> SuccessCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.consume.succeeded",
            unit: "{authorization}",
            description: "Counts successful exit authorization consumptions.");

    /// <summary>
    /// Counts rejected exit authorization consumptions caused by deterministic validation or business failures.
    /// </summary>
    private static readonly Counter<long> RejectedCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.consume.rejected",
            unit: "{authorization}",
            description: "Counts rejected exit authorization consumptions.");

    /// <summary>
    /// Counts unexpected exit authorization consumption failures.
    /// </summary>
    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.consume.failed",
            unit: "{authorization}",
            description: "Counts unexpected exit authorization consumption failures.");

    private readonly IConsumeExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<ConsumeExitAuthorizationHandler> _logger;

    /// <summary>
    /// Creates a handler for consuming exit authorizations through the canonical DB routine.
    /// </summary>
    /// <param name="gateway">DB-backed consume gateway.</param>
    /// <param name="systemClock">System clock used for canonical request timestamps.</param>
    /// <param name="logger">Application logger.</param>
    public ConsumeExitAuthorizationHandler(
        IConsumeExitAuthorizationGateway gateway,
        ISystemClock systemClock,
        ILogger<ConsumeExitAuthorizationHandler> logger)
    {
        _gateway = gateway;
        _systemClock = systemClock;
        _logger = logger;
    }

    /// <summary>
    /// Consumes an exit authorization after validating command completeness.
    /// </summary>
    /// <param name="command">Consumption command containing identifiers and trace metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative consume result mapped into the application model.</returns>
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

            SuccessCounter.Add(
                1,
                new KeyValuePair<string, object?>("authorization_status", dbResult.AuthorizationStatus));

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

            RejectedCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", "INVALID_REQUEST"));

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

            RejectedCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", ex.GetType().Name));

            _logger.LogWarning(
                ex,
                "ConsumeExitAuthorization was rejected by deterministic business rules.");

            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            FailureCounter.Add(
                1,
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name));

            _logger.LogError(
                ex,
                "Unexpected failure while consuming exit authorization. exit_authorization_id={ExitAuthorizationId}",
                command.ExitAuthorizationId);

            throw;
        }
    }

    /// <summary>
    /// Validates command completeness before calling the authoritative DB path.
    /// </summary>
    /// <param name="command">Command to validate.</param>
    /// <exception cref="ArgumentException">Thrown when required fields are missing or invalid.</exception>
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
