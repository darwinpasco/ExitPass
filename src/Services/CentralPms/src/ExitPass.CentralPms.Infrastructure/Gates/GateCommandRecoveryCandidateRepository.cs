using System.Data;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed read-only candidate finder for one stale gate command recovery cycle.
/// </summary>
public sealed class GateCommandRecoveryCandidateRepository : IGateCommandRecoveryCandidateRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command recovery candidate repository.
    /// </summary>
    public GateCommandRecoveryCandidateRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandRecoveryCandidate?> FindNextStaleAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                command_id,
                last_attempted_at,
                attempt_count,
                max_attempts
            FROM gates.gate_commands
            WHERE command_type = 'OPEN_GATE'
              AND command_status = 'IN_PROGRESS'
              AND last_attempted_at IS NOT NULL
              AND last_attempted_at <= @stale_before
              AND attempt_count >= 0
              AND max_attempts > 0
              AND attempt_count <= max_attempts
              AND source_processing_id IS NOT NULL
              AND gate_authorization_consumption_id IS NOT NULL
              AND exit_authorization_id IS NOT NULL
              AND parking_session_id IS NOT NULL
              AND payment_attempt_id IS NOT NULL
              AND tariff_snapshot_id IS NOT NULL
            ORDER BY last_attempted_at ASC, requested_at ASC, command_id ASC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("stale_before", NpgsqlDbType.TimestampTz).Value = staleBefore;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GateCommandRecoveryCandidate(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.GetInt32(reader.GetOrdinal("max_attempts")));
    }
}
