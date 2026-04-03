using System.Diagnostics;
using System.Diagnostics.Metrics;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Issues a single-use exit authorization through the canonical DB-backed control path.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - ExitAuthorization may only be issued after confirmed payment finality
/// - Authorization issuance remains DB-backed and deterministic
/// - Issuance requests are fully traceable through correlation metadata
/// </summary>
public sealed class IssueExitAuthorizationHandler : IIssueExitAuthorizationUseCase
{
    /// <summary>
    /// Activity source for exit authorization issuance spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    /// <summary>
    /// Metrics meter for exit authorization issuance.
    /// </summary>
    private static readonly Meter Meter =
        new("ExitPass.CentralPms.Application.Payments", "1.0.0");

    /// <summary>
    /// Counts successful exit authorization issuances.
    /// </summary>
    private static readonly Counter<long> SuccessCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.issue.succeeded",
            unit: "{authorization}",
            description: "Counts successful exit authorization issuances.");

    /// <summary>
    /// Counts rejected exit authorization issuance attempts caused by deterministic validation or business failures.
    /// </summary>
    private static readonly Counter<long> RejectedCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.issue.rejected",
            unit: "{authorization}",
            description: "Counts rejected exit authorization issuance attempts.");

    /// <summary>
    /// Counts unexpected exit authorization issuance failures.
    /// </summary>
    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.exit_authorization.issue.failed",
            unit: "{authorization}",
            description: "Counts unexpected exit authorization issuance failures.");

    private readonly IIssueExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<IssueExitAuthorizationHandler> _logger;

    /// <summary>
    /// Creates a handler for issuing exit authorizations through the canonical DB routine.
    /// </summary>
    /// <param name="gateway">DB-backed issuance gateway.</param>
    /// <param name="systemClock">System clock used for canonical request timestamps.</param>
    /// <param name="logger">Application logger.</param>
    public IssueExitAuthorizationHandler(
        IIssueExitAuthorizationGateway gateway,
        ISystemClock systemClock,
        ILogger<IssueExitAuthorizationHandler> logger)
    {
        _gateway = gateway;
        _systemClock = systemClock;
        _logger = logger;
    }

    /// <summary>
    /// Issues an exit authorization after validating command completeness.
    /// </summary>
    /// <param name="command">Issuance command containing identifiers and trace metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative issuance result mapped into the application model.</returns>
    public async Task<IssueExitAuthorizationResult> ExecuteAsync(
        IssueExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("IssueExitAuthorization", ActivityKind.Internal);

        activity?.SetTag("operation", "issue_exit_authorization");
        activity?.SetTag("parking_session_id", command.ParkingSessionId);
        activity?.SetTag("payment_attempt_id", command.PaymentAttemptId);
        activity?.SetTag("requested_by_user_id", command.RequestedByUserId);
        activity?.SetTag("correlation_id", command.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["parking_session_id"] = command.ParkingSessionId,
            ["payment_attempt_id"] = command.PaymentAttemptId,
            ["requested_by_user_id"] = command.RequestedByUserId,
            ["correlation_id"] = command.CorrelationId
        });

        _logger.LogInformation("IssueExitAuthorization started.");

        try
        {
            ValidateCommand(command);

            var dbStart = _systemClock.UtcNow;

            var dbResult = await _gateway.IssueAsync(
                new IssueExitAuthorizationDbRequest
                {
                    ParkingSessionId = command.ParkingSessionId,
                    PaymentAttemptId = command.PaymentAttemptId,
                    RequestedByUserId = command.RequestedByUserId,
                    CorrelationId = command.CorrelationId,
                    RequestedAt = _systemClock.UtcNow
                },
                cancellationToken);

            var dbDuration = _systemClock.UtcNow - dbStart;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("exit_authorization_id", dbResult.ExitAuthorizationId);
            activity?.SetTag("authorization_status", dbResult.AuthorizationStatus);
            activity?.SetTag("issued_at", dbResult.IssuedAt);
            activity?.SetTag("expiration_timestamp", dbResult.ExpirationTimestamp);
            activity?.SetTag("db.duration_ms", dbDuration.TotalMilliseconds);

            SuccessCounter.Add(
                1,
                new KeyValuePair<string, object?>("authorization_status", dbResult.AuthorizationStatus));

            _logger.LogInformation(
                "Exit authorization issued successfully. exit_authorization_id={ExitAuthorizationId} authorization_status={AuthorizationStatus}",
                dbResult.ExitAuthorizationId,
                dbResult.AuthorizationStatus);

            return new IssueExitAuthorizationResult(
                ExitAuthorizationId: dbResult.ExitAuthorizationId,
                ParkingSessionId: dbResult.ParkingSessionId,
                PaymentAttemptId: dbResult.PaymentAttemptId,
                AuthorizationToken: dbResult.AuthorizationToken,
                AuthorizationStatus: dbResult.AuthorizationStatus,
                IssuedAt: dbResult.IssuedAt,
                ExpirationTimestamp: dbResult.ExpirationTimestamp);
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
                "IssueExitAuthorization rejected because the command is invalid.");

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
                "IssueExitAuthorization was rejected by deterministic business rules.");

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
                "Unexpected failure while issuing exit authorization. payment_attempt_id={PaymentAttemptId}",
                command.PaymentAttemptId);

            throw;
        }
    }

    /// <summary>
    /// Validates command completeness before calling the authoritative DB path.
    /// </summary>
    /// <param name="command">Command to validate.</param>
    /// <exception cref="ArgumentException">Thrown when required fields are missing or invalid.</exception>
    private static void ValidateCommand(IssueExitAuthorizationCommand command)
    {
        if (command.ParkingSessionId == Guid.Empty)
        {
            throw new ArgumentException("ParkingSessionId is required.", nameof(command));
        }

        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
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
