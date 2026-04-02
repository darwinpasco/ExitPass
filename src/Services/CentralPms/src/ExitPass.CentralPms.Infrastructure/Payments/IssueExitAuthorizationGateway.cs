using ExitPass.CentralPms.Application.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - ExitAuthorization is issued only through the canonical DB routine
/// - Application code does not mint or infer authorization state outside the DB control path
/// </summary>
public sealed class IssueExitAuthorizationGateway : IIssueExitAuthorizationGateway
{
    private readonly string _connectionString;

    public IssueExitAuthorizationGateway(string connectionString)
    {
        _connectionString = connectionString;
    }

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

        return new IssueExitAuthorizationDbResult(
            ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            AuthorizationToken: reader.GetString(reader.GetOrdinal("authorization_token")),
            AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
            IssuedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("issued_at")),
            ExpirationTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expiration_timestamp")));
    }
}
