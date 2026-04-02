using ExitPass.CentralPms.Application.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - ExitAuthorization is consumed only through the canonical DB routine
/// - Application code does not mutate authorization state outside the DB control path
/// </summary>
public sealed class ConsumeExitAuthorizationGateway : IConsumeExitAuthorizationGateway
{
    private readonly string _connectionString;

    public ConsumeExitAuthorizationGateway(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ConsumeExitAuthorizationDbResult> ConsumeAsync(
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                authorization_status,
                consumed_at
            FROM core.consume_exit_authorization(
                @p_exit_authorization_id,
                @p_requested_by,
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

        dbCommand.Parameters.AddWithValue("p_exit_authorization_id", request.ExitAuthorizationId);
        dbCommand.Parameters.AddWithValue("p_requested_by", request.RequestedByUserId);
        dbCommand.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
        dbCommand.Parameters.AddWithValue("p_now", request.RequestedAt);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("consume_exit_authorization() returned no rows.");
        }

        return new ConsumeExitAuthorizationDbResult(
            ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
            ConsumedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("consumed_at")));
    }
}
