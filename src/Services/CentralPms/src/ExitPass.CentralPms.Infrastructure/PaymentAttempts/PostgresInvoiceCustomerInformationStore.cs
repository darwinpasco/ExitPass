using ExitPass.CentralPms.Application.PaymentAttempts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

/// <summary>
/// Persists an immutable customer-information snapshot for fiscal issuance and replay.
/// </summary>
/// <remarks>
/// TODO: Confirm exact BRD v1.2 and SDD v1.2 section references before merging.
/// The authoritative tariff and statutory approval remain server-owned. A statutory ID is retained only
/// when the payment attempt's tariff snapshot carries an approved statutory validation.
/// </remarks>
public sealed class PostgresInvoiceCustomerInformationStore : IInvoiceCustomerInformationStore
{
    private readonly string _connectionString;

    public PostgresInvoiceCustomerInformationStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveImmutableAsync(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        InvoiceCustomerInformation information,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(information);
        Validate(information);

        const string sql = """
            WITH authoritative_attempt AS (
                SELECT
                    pa.payment_attempt_id,
                    pa.parking_session_id,
                    pa.tariff_snapshot_id,
                    ts.statutory_discount_validation_id IS NOT NULL AS has_approved_statutory_benefit
                FROM core.payment_attempts pa
                INNER JOIN core.tariff_snapshots ts
                    ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
                   AND ts.parking_session_id = pa.parking_session_id
                WHERE pa.payment_attempt_id = @payment_attempt_id
                  AND pa.parking_session_id = @parking_session_id
                  AND pa.tariff_snapshot_id = @tariff_snapshot_id
            ), inserted AS (
                INSERT INTO core.payment_attempt_invoice_customer_information (
                    payment_attempt_id,
                    parking_session_id,
                    tariff_snapshot_id,
                    customer_name,
                    customer_address,
                    customer_tin,
                    business_style,
                    statutory_id_number,
                    created_at
                )
                SELECT
                    payment_attempt_id,
                    parking_session_id,
                    tariff_snapshot_id,
                    @customer_name,
                    @customer_address,
                    @customer_tin,
                    @business_style,
                    CASE WHEN has_approved_statutory_benefit THEN @statutory_id_number ELSE NULL END,
                    current_timestamp
                FROM authoritative_attempt
                ON CONFLICT (payment_attempt_id) DO NOTHING
                RETURNING
                    payment_attempt_id,
                    parking_session_id,
                    tariff_snapshot_id,
                    customer_name,
                    customer_address,
                    customer_tin,
                    business_style,
                    statutory_id_number
            ), selected AS (
                SELECT *
                FROM inserted
                UNION ALL
                SELECT
                    info.payment_attempt_id,
                    info.parking_session_id,
                    info.tariff_snapshot_id,
                    info.customer_name,
                    info.customer_address,
                    info.customer_tin,
                    info.business_style,
                    info.statutory_id_number
                FROM core.payment_attempt_invoice_customer_information info
                WHERE info.payment_attempt_id = @payment_attempt_id
                  AND NOT EXISTS (SELECT 1 FROM inserted)
            )
            SELECT
                selected.customer_name,
                selected.customer_address,
                selected.customer_tin,
                selected.business_style,
                selected.statutory_id_number,
                ts.statutory_discount_validation_id IS NOT NULL AS has_approved_statutory_benefit
            FROM selected
            INNER JOIN core.tariff_snapshots ts
                ON ts.tariff_snapshot_id = selected.tariff_snapshot_id
               AND ts.parking_session_id = selected.parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
        AddNullableText(command, "customer_name", Normalize(information.CustomerName));
        AddNullableText(command, "customer_address", Normalize(information.Address));
        AddNullableText(command, "customer_tin", Normalize(information.Tin));
        AddNullableText(command, "business_style", Normalize(information.BusinessStyle));
        AddNullableText(command, "statutory_id_number", Normalize(information.StatutoryIdNumber));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("invoice_customer_information_payment_attempt_not_found");
        }

        var approved = reader.GetBoolean(reader.GetOrdinal("has_approved_statutory_benefit"));
        var expectedStatutoryId = approved ? Normalize(information.StatutoryIdNumber) : null;
        if (!EqualsNullable(reader, "customer_name", Normalize(information.CustomerName)) ||
            !EqualsNullable(reader, "customer_address", Normalize(information.Address)) ||
            !EqualsNullable(reader, "customer_tin", Normalize(information.Tin)) ||
            !EqualsNullable(reader, "business_style", Normalize(information.BusinessStyle)) ||
            !EqualsNullable(reader, "statutory_id_number", expectedStatutoryId))
        {
            throw new InvalidOperationException("invoice_customer_information_immutable_conflict");
        }
    }

    private static void Validate(InvoiceCustomerInformation value)
    {
        RequireMaximum(value.CustomerName, 160, "customer_name_too_long");
        RequireMaximum(value.Address, 300, "customer_address_too_long");
        RequireMaximum(value.Tin, 40, "customer_tin_too_long");
        RequireMaximum(value.BusinessStyle, 160, "business_style_too_long");
        RequireMaximum(value.StatutoryIdNumber, 80, "statutory_id_number_too_long");
    }

    private static void RequireMaximum(string? value, int maximum, string code)
    {
        if (Normalize(value)?.Length > maximum)
        {
            throw new ArgumentException(code);
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;

    private static bool EqualsNullable(NpgsqlDataReader reader, string name, string? expected)
    {
        var ordinal = reader.GetOrdinal(name);
        var actual = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }
}
