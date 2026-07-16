using System.Data;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed read-only candidate finder for one gate command dispatch cycle.
/// </summary>
public sealed class GateCommandDispatchCandidateRepository : IGateCommandDispatchCandidateRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command dispatch candidate repository.
    /// </summary>
    public GateCommandDispatchCandidateRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandDispatchCandidate?> FindNextEligibleAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT command_id, command_status, eligibility_timestamp
            FROM (
                SELECT
                    command_id,
                    command_status,
                    requested_at AS eligibility_timestamp,
                    requested_at AS requested_order_timestamp
                FROM gates.gate_commands
                WHERE command_type = 'OPEN_GATE'
                  AND command_status = 'REQUESTED'
                  AND attempt_count < max_attempts

                UNION ALL

                SELECT
                    command_id,
                    command_status,
                    next_attempt_at AS eligibility_timestamp,
                    requested_at AS requested_order_timestamp
                FROM gates.gate_commands
                WHERE command_type = 'OPEN_GATE'
                  AND command_status = 'RETRYABLE'
                  AND next_attempt_at IS NOT NULL
                  AND next_attempt_at <= @as_of
                  AND attempt_count < max_attempts
            ) AS candidates
            ORDER BY eligibility_timestamp ASC, requested_order_timestamp ASC, command_id ASC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("as_of", NpgsqlDbType.TimestampTz).Value = asOf;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GateCommandDispatchCandidate(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("eligibility_timestamp")));
    }
}
