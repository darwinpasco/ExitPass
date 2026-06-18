using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Operations;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Operations;

/// <summary>
/// PostgreSQL-backed read-only repository for ticket session payment status summaries.
/// </summary>
public sealed class TicketSessionSummaryReadRepository : ITicketSessionSummaryReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a ticket session summary read repository.
    /// </summary>
    public TicketSessionSummaryReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<TicketSessionLocalStatusResult> FindLocalStatusAsync(
        string ticketNumber,
        Guid? siteId,
        Guid? siteGroupId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketNumber);

        const string sql = """
            SELECT
                ps.parking_session_id,
                latest_attempt.payment_attempt_id,
                latest_attempt.attempt_status::text AS payment_attempt_status,
                latest_confirmation.confirmation_status::text AS payment_confirmation_status
            FROM core.parking_sessions AS ps
            LEFT JOIN LATERAL (
                SELECT
                    pa.payment_attempt_id,
                    pa.attempt_status
                FROM core.payment_attempts AS pa
                WHERE pa.parking_session_id = ps.parking_session_id
                ORDER BY pa.requested_at DESC, pa.payment_attempt_id DESC
                LIMIT 1
            ) AS latest_attempt ON TRUE
            LEFT JOIN LATERAL (
                SELECT pc.confirmation_status
                FROM core.payment_confirmations AS pc
                WHERE pc.payment_attempt_id = latest_attempt.payment_attempt_id
                ORDER BY pc.confirmed_at DESC, pc.payment_confirmation_id DESC
                LIMIT 1
            ) AS latest_confirmation ON TRUE
            WHERE (@site_id IS NULL OR ps.site_id = @site_id)
              AND (@site_group_id IS NULL OR ps.site_group_id = @site_group_id)
              AND (
                    ps.vendor_session_ref = @ticket_number
                 OR ps.ticket_number_masked = @ticket_number
                 OR ps.ticket_number_hash = @ticket_number_hash
              )
            ORDER BY ps.created_at DESC, ps.parking_session_id DESC
            LIMIT 2;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("ticket_number", NpgsqlDbType.Text).Value = ticketNumber.Trim();
        command.Parameters.Add("ticket_number_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(ticketNumber));
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(siteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(siteGroupId);

        var matches = new List<TicketSessionLocalStatusReadModel>(capacity: 2);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var attemptStatus = GetNullableString(reader, "payment_attempt_status");
            var confirmationStatus = GetNullableString(reader, "payment_confirmation_status");
            matches.Add(new TicketSessionLocalStatusReadModel(
                reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                GetNullableGuid(reader, "payment_attempt_id"),
                attemptStatus,
                MapPaymentStatus(attemptStatus, confirmationStatus),
                confirmationStatus,
                VendorConfirmationStatus: null,
                VendorConfirmationTimestamp: null));
        }

        return matches.Count switch
        {
            0 => new TicketSessionLocalStatusResult(TicketSessionLocalStatusOutcome.NotFound, Status: null),
            1 => new TicketSessionLocalStatusResult(TicketSessionLocalStatusOutcome.Found, matches[0]),
            _ => new TicketSessionLocalStatusResult(TicketSessionLocalStatusOutcome.Ambiguous, Status: null)
        };
    }

    private static string MapPaymentStatus(string? attemptStatus, string? confirmationStatus)
    {
        if (string.Equals(confirmationStatus, "RECORDED", StringComparison.OrdinalIgnoreCase))
        {
            return "Paid";
        }

        return attemptStatus?.Trim().ToUpperInvariant() switch
        {
            null or "" => "Not Started",
            "REQUESTED" or "PENDING_PROVIDER" => "Pending Payment",
            "CONFIRMED" or "PAID" or "FINALIZED" => "Paid",
            "FAILED" or "CANCELLED" => "Failed",
            "EXPIRED" => "Expired",
            _ => attemptStatus!.Trim()
        };
    }

    private static string? HashIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
