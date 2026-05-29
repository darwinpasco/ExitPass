using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for Operator Console parking session lookup.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reads only current Central PMS persistence state.
/// - Does not calculate tariffs or write payment, gate, coupon, statutory discount, provider, settlement, or reconciliation state.
/// </summary>
public sealed class OperatorConsoleSessionLookupReadRepository : IOperatorConsoleSessionLookupReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console session lookup read repository.
    /// </summary>
    public OperatorConsoleSessionLookupReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleSessionReadModel?> FindAsync(
        OperatorConsoleSessionLookupReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string sql = """
            SELECT
                ps.parking_session_id,
                ps.site_id,
                ps.site_group_id,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                ps.plate_number_masked,
                COALESCE(ps.entry_at, ps.created_at) AS entry_time,
                ps.session_status::text AS session_status,
                active_tariff.currency_code AS tariff_currency_code,
                active_tariff.net_amount AS current_payable_amount,
                CASE
                    WHEN active_tariff.tariff_snapshot_id IS NULL THEN NULL
                    WHEN active_tariff.statutory_discount_amount > 0 OR active_tariff.coupon_discount_amount > 0 THEN 'APPLIED'
                    ELSE 'NOT_APPLIED'
                END AS discount_status,
                latest_attempt.attempt_status::text AS payment_status,
                latest_exit.authorization_status::text AS exit_authorization_status
            FROM core.parking_sessions AS ps
            LEFT JOIN LATERAL (
                SELECT
                    tariff_snapshot_id,
                    currency_code,
                    net_amount,
                    statutory_discount_amount,
                    coupon_discount_amount
                FROM core.tariff_snapshots
                WHERE parking_session_id = ps.parking_session_id
                  AND snapshot_status = 'ACTIVE'
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS active_tariff ON TRUE
            LEFT JOIN LATERAL (
                SELECT attempt_status
                FROM core.payment_attempts
                WHERE parking_session_id = ps.parking_session_id
                ORDER BY requested_at DESC, payment_attempt_id DESC
                LIMIT 1
            ) AS latest_attempt ON TRUE
            LEFT JOIN LATERAL (
                SELECT authorization_status
                FROM core.exit_authorizations
                WHERE parking_session_id = ps.parking_session_id
                ORDER BY issued_at DESC, exit_authorization_id DESC
                LIMIT 1
            ) AS latest_exit ON TRUE
            WHERE (@site_id IS NULL OR ps.site_id = @site_id)
              AND (@site_group_id IS NULL OR ps.site_group_id = @site_group_id)
              AND (
                    (@lookup_mode = 'PARKING_SESSION_ID' AND ps.parking_session_id = @parking_session_id)
                 OR (@lookup_mode = 'TICKET_REFERENCE' AND (
                        ps.vendor_session_ref = @ticket_reference
                     OR ps.ticket_number_masked = @ticket_reference
                     OR ps.ticket_number_hash = @ticket_reference_hash
                    ))
              )
            ORDER BY ps.created_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = DbValue(request.ParkingSessionId);
        command.Parameters.Add("ticket_reference", NpgsqlDbType.Text).Value = DbValue(request.TicketReference);
        command.Parameters.Add("ticket_reference_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(request.TicketReference));
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(request.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(request.SiteGroupId);
        command.Parameters.Add("lookup_mode", NpgsqlDbType.Text).Value = request.LookupMode;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorConsoleSessionReadModel(
            reader.GetGuid("parking_session_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number_masked"),
            reader.GetGuid("site_id"),
            reader.GetGuid("site_group_id"),
            reader.GetString("session_status"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("entry_time")),
            ToMinorUnits(reader, "current_payable_amount"),
            GetNullableString(reader, "tariff_currency_code"),
            GetNullableString(reader, "payment_status"),
            GetNullableString(reader, "discount_status"),
            GetNullableString(reader, "exit_authorization_status"));
    }

    private static long? ToMinorUnits(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetDecimal(ordinal);
        return (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
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

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}

internal static class OperatorConsoleSessionLookupDataReaderExtensions
{
    public static Guid GetGuid(this NpgsqlDataReader reader, string columnName) =>
        reader.GetGuid(reader.GetOrdinal(columnName));

    public static string GetString(this NpgsqlDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));
}
