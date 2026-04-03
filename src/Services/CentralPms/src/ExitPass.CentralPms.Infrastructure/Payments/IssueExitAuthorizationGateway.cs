using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// Infrastructure gateway that issues exit authorizations through the canonical database routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 9.7 Recommended Database Functions
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - ExitAuthorization is issued only through the canonical DB routine
/// - Application code does not mint or infer authorization state outside the DB control path
/// </summary>
public sealed class IssueExitAuthorizationGateway : IIssueExitAuthorizationGateway
{
    /// <summary>
    /// Activity source for payment infrastructure spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Infrastructure.Payments");

    private readonly string _connectionString;
    private readonly ILogger<IssueExitAuthorizationGateway> _logger;

    /// <summary>
    /// Creates a gateway for issuing exit authorizations against the primary database.
    /// </summary>
    /// <param name="connectionString">Database connection string for Central PMS persistence.</param>
    /// <param name="logger">Application logger.</param>
    public IssueExitAuthorizationGateway(
        string connectionString,
        ILogger<IssueExitAuthorizationGateway> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    /// <summary>
    /// Issues an exit authorization by calling the canonical database routine.
    /// </summary>
    /// <param name="request">Issuance request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative issuance result.</returns>
    public async Task<IssueExitAuthorizationDbResult> IssueAsync(
        IssueExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                authorization_token,
                authorization_status,
                issued_at,
                expiration_timestamp
            FROM core.issue_exit_authorization(
                @p_parking_session_id,
                @p_payment_attempt_id,
                @p_requested_by_user_id,
                @p_correlation_id,
                @p_now
            );
            """;

        using var activity = ActivitySource.StartActivity("DB IssueExitAuthorization", ActivityKind.Client);

        activity?.SetTag("db.system", "postgresql");
        activity?.SetTag("db.operation", "issue_exit_authorization");
        activity?.SetTag("db.statement.name", "core.issue_exit_authorization");
        activity?.SetTag("parking_session_id", request.ParkingSessionId);
        activity?.SetTag("payment_attempt_id", request.PaymentAttemptId);
        activity?.SetTag("requested_by_user_id", request.RequestedByUserId);
        activity?.SetTag("correlation_id", request.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["parking_session_id"] = request.ParkingSessionId,
            ["payment_attempt_id"] = request.PaymentAttemptId,
            ["requested_by_user_id"] = request.RequestedByUserId,
            ["correlation_id"] = request.CorrelationId
        });

        _logger.LogInformation("DB IssueExitAuthorization started.");

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var dbCommand = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = 30
            };

            dbCommand.Parameters.AddWithValue("p_parking_session_id", request.ParkingSessionId);
            dbCommand.Parameters.AddWithValue("p_payment_attempt_id", request.PaymentAttemptId);
            dbCommand.Parameters.AddWithValue("p_requested_by_user_id", request.RequestedByUserId);
            dbCommand.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
            dbCommand.Parameters.AddWithValue("p_now", request.RequestedAt);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("issue_exit_authorization() returned no rows.");
            }

            var result = new IssueExitAuthorizationDbResult(
                ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
                ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
                AuthorizationToken: reader.GetString(reader.GetOrdinal("authorization_token")),
                AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
                IssuedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("issued_at")),
                ExpirationTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expiration_timestamp")));

            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);
            activity?.SetTag("exit_authorization_id", result.ExitAuthorizationId);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);

            _logger.LogInformation(
                "DB IssueExitAuthorization succeeded. exit_authorization_id={ExitAuthorizationId} authorization_status={AuthorizationStatus}",
                result.ExitAuthorizationId,
                result.AuthorizationStatus);

            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogError(
                ex,
                "DB IssueExitAuthorization failed. payment_attempt_id={PaymentAttemptId}",
                request.PaymentAttemptId);

            throw;
        }
    }
}
