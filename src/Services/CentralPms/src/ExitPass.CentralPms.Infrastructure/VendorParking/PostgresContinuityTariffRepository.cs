using ExitPass.CentralPms.Application.VendorParking;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

public sealed class PostgresContinuityTariffRepository(string connectionString) : IContinuityTariffRepository
{
    public async Task<ContinuityTariffDefinition?> FindActiveAsync(
        Guid siteId, string vehicleTypeCode, DateTimeOffset at, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.site_tariff_definition_id, d.site_id, s.timezone_name, d.vehicle_type_code,
                   d.tariff_code, d.version, d.currency_code, d.parking_grace_period_minutes,
                   d.post_payment_exit_grace_minutes, d.daily_max_fee_minor_units,
                   d.quote_validity_minutes, d.effective_from, d.effective_to,
                   r.site_tariff_rule_id, r.sequence, r.rule_scope, r.charge_type,
                   r.duration_start_minutes, r.duration_end_minutes,
                   r.clock_start_time, r.clock_end_time, r.amount_minor_units,
                   r.billing_unit_minutes, r.rounding_rule
            FROM sites.site_tariff_definitions d
            JOIN sites.sites s ON s.site_id = d.site_id
            JOIN sites.site_tariff_rules r ON r.site_tariff_definition_id = d.site_tariff_definition_id
            WHERE d.site_id = @site_id AND d.vehicle_type_code = @vehicle_type_code
              AND d.status = 'ACTIVE' AND d.effective_from <= @at
              AND (d.effective_to IS NULL OR d.effective_to > @at)
              AND s.site_status = 'ACTIVE'
            ORDER BY d.effective_from DESC, r.sequence;
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vehicle_type_code", NpgsqlDbType.Varchar).Value = vehicleTypeCode;
        command.Parameters.Add("at", NpgsqlDbType.TimestampTz).Value = at.ToUniversalTime();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        ContinuityTariffDefinition? definition = null;
        var rules = new List<ContinuityTariffRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            definition ??= new ContinuityTariffDefinition(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9), reader.GetInt32(10),
                reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12), rules);
            if (definition.SiteTariffDefinitionId != reader.GetGuid(0))
                throw new InvalidOperationException("CONTINUITY_TARIFF_RULE_AMBIGUOUS");
            rules.Add(new ContinuityTariffRule(
                reader.GetGuid(13), reader.GetInt32(14), reader.GetString(15), reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetInt32(17), reader.IsDBNull(18) ? null : reader.GetInt32(18),
                reader.IsDBNull(19) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(19)),
                reader.IsDBNull(20) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(20)),
                reader.GetInt64(21), reader.IsDBNull(22) ? null : reader.GetInt32(22),
                reader.IsDBNull(23) ? null : reader.GetString(23)));
        }
        return definition;
    }
}
