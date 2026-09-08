using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

public sealed class PostgresStatutoryDiscountZeroPayableFinalityReader
    : IStatutoryDiscountZeroPayableFinalityReader
{
    private readonly string _connectionString;

    public PostgresStatutoryDiscountZeroPayableFinalityReader(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<StatutoryDiscountZeroPayableFinalityCandidate?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                decision.statutory_discount_decision_command_id AS decision_command_id,
                decision.parking_session_id AS decision_parking_session_id,
                decision.statutory_discount_validation_id AS decision_validation_id,
                decision.applied_policy_reference_id AS decision_policy_id,
                decision.decision_result_status AS decision_status,
                decision.entitlement_type AS decision_entitlement_type,
                decision.source_channel AS decision_source_channel,
                decision.gross_amount_minor_units AS decision_gross_minor,
                decision.vat_amount_minor_units AS decision_vat_minor,
                decision.statutory_discount_amount_minor_units AS decision_discount_minor,
                decision.net_payable_amount_minor_units AS decision_final_minor,
                decision.currency_code::text AS decision_currency,
                decision.decided_at AS decision_decided_at,

                command.statutory_discount_payable_basis_application_command_id AS application_command_id,
                command.statutory_discount_decision_command_id AS command_decision_id,
                command.parking_session_id AS command_parking_session_id,
                command.site_id AS command_site_id,
                command.statutory_discount_validation_id AS command_validation_id,
                command.statutory_discount_payable_basis_application_id AS command_application_id,
                command.original_tariff_snapshot_id AS command_original_tariff_id,
                command.applied_tariff_snapshot_id AS command_applied_tariff_id,
                command.applied_policy_reference_id AS command_policy_id,
                command.statutory_discount_policy_version_id AS command_policy_version_id,
                command.command_status AS command_status,
                command.entitlement_type AS command_entitlement_type,
                command.approved_discount_amount_minor_units AS command_discount_minor,
                command.approved_vat_amount_minor_units AS command_vat_minor,
                command.approved_final_payable_amount_minor_units AS command_final_minor,
                command.currency_code::text AS command_currency,
                command.source_channel AS command_source_channel,
                command.original_correlation_id AS command_correlation_id,
                command.applied_at AS command_applied_at,

                application.statutory_discount_payable_basis_application_id AS application_id,
                application.statutory_discount_validation_id AS application_validation_id,
                application.parking_session_id AS application_parking_session_id,
                application.original_tariff_snapshot_id AS application_original_tariff_id,
                application.applied_tariff_snapshot_id AS application_applied_tariff_id,
                application.application_status::text AS application_status,
                application.gross_amount_minor_units AS application_gross_minor,
                application.vat_amount_minor_units AS application_vat_minor,
                application.statutory_discount_amount_minor_units AS application_discount_minor,
                application.final_payable_amount_minor_units AS application_final_minor,
                application.currency_code::text AS application_currency,
                application.applied_at AS application_applied_at,
                application.correlation_id AS application_correlation_id,

                validation.statutory_discount_validation_id AS validation_id,
                validation.parking_session_id AS validation_parking_session_id,
                validation.tariff_snapshot_id AS validation_tariff_id,
                validation.applied_policy_reference_id AS validation_policy_id,
                validation.statutory_discount_policy_version_id AS validation_policy_version_id,
                validation.validation_status::text AS validation_status,
                validation.entitlement_type::text AS validation_entitlement_type,
                ROUND(validation.gross_amount_at_validation * 100)::bigint AS validation_gross_minor,
                ROUND(validation.statutory_discount_amount * 100)::bigint AS validation_discount_minor,
                ROUND(validation.net_amount_after_discount * 100)::bigint AS validation_final_minor,
                validation.currency_code::text AS validation_currency,

                authority.statutory_discount_decision_command_id AS authority_decision_id,
                authority.statutory_discount_policy_version_id AS authority_policy_version_id,
                authority.entitlement_type AS authority_entitlement_type,
                authority.benefit_type::text AS authority_benefit_type,
                policy.entitlement_type::text AS policy_entitlement_type,
                policy.benefit_type::text AS policy_benefit_type,
                policy.policy_effect_support_status::text AS policy_support_status,
                policy.full_fee_exempt AS policy_full_fee_exempt,
                policy.site_id AS policy_site_id,
                policy.site_group_id AS policy_site_group_id,

                parking.parking_session_id AS parking_session_id,
                parking.site_id AS parking_site_id,
                parking.site_group_id AS parking_site_group_id,

                original_tariff.tariff_snapshot_id AS original_tariff_id,
                original_tariff.parking_session_id AS original_tariff_parking_session_id,
                original_tariff.snapshot_status::text AS original_tariff_status,
                ROUND(original_tariff.gross_amount * 100)::bigint AS original_tariff_gross_minor,
                ROUND(original_tariff.statutory_discount_amount * 100)::bigint AS original_tariff_discount_minor,
                ROUND(original_tariff.net_amount * 100)::bigint AS original_tariff_net_minor,
                original_tariff.currency_code::text AS original_tariff_currency,
                original_tariff.statutory_discount_validation_id AS original_tariff_validation_id,

                applied_tariff.tariff_snapshot_id AS applied_tariff_id,
                applied_tariff.parking_session_id AS applied_tariff_parking_session_id,
                applied_tariff.snapshot_status::text AS applied_tariff_status,
                ROUND(applied_tariff.gross_amount * 100)::bigint AS applied_tariff_gross_minor,
                ROUND(applied_tariff.statutory_discount_amount * 100)::bigint AS applied_tariff_discount_minor,
                ROUND(applied_tariff.net_amount * 100)::bigint AS applied_tariff_net_minor,
                applied_tariff.currency_code::text AS applied_tariff_currency,
                applied_tariff.statutory_discount_validation_id AS applied_tariff_validation_id,

                EXISTS (
                    SELECT 1
                    FROM core.payment_attempts AS payment
                    WHERE payment.tariff_snapshot_id = command.applied_tariff_snapshot_id
                ) AS has_conflicting_payment_attempt
            FROM discounts.statutory_discount_decision_commands AS decision
            LEFT JOIN discounts.statutory_discount_payable_basis_application_commands AS command
              ON command.statutory_discount_decision_command_id = decision.statutory_discount_decision_command_id
            LEFT JOIN discounts.statutory_discount_payable_basis_applications AS application
              ON application.statutory_discount_payable_basis_application_id =
                 command.statutory_discount_payable_basis_application_id
            LEFT JOIN discounts.statutory_discount_validations AS validation
              ON validation.statutory_discount_validation_id = command.statutory_discount_validation_id
            LEFT JOIN discounts.statutory_discount_decision_policy_authorities AS authority
              ON authority.statutory_discount_decision_command_id = decision.statutory_discount_decision_command_id
            LEFT JOIN discounts.statutory_discount_policy_versions AS policy
              ON policy.statutory_discount_policy_version_id = authority.statutory_discount_policy_version_id
            LEFT JOIN core.parking_sessions AS parking
              ON parking.parking_session_id = decision.parking_session_id
            LEFT JOIN core.tariff_snapshots AS original_tariff
              ON original_tariff.tariff_snapshot_id = command.original_tariff_snapshot_id
            LEFT JOIN core.tariff_snapshots AS applied_tariff
              ON applied_tariff.tariff_snapshot_id = command.applied_tariff_snapshot_id
            WHERE decision.statutory_discount_decision_command_id = @decision_command_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var candidate = new StatutoryDiscountZeroPayableFinalityCandidate(
            new StatutoryDiscountZeroPayableDecisionAnchor(
                RequiredGuid(reader, "decision_command_id"),
                RequiredGuid(reader, "decision_parking_session_id"),
                NullableGuid(reader, "decision_validation_id"),
                NullableGuid(reader, "decision_policy_id"),
                RequiredString(reader, "decision_status"),
                RequiredString(reader, "decision_entitlement_type"),
                RequiredString(reader, "decision_source_channel"),
                NullableInt64(reader, "decision_gross_minor"),
                NullableInt64(reader, "decision_vat_minor"),
                NullableInt64(reader, "decision_discount_minor"),
                NullableInt64(reader, "decision_final_minor"),
                NullableString(reader, "decision_currency"),
                NullableTimestamp(reader, "decision_decided_at")),
            new StatutoryDiscountZeroPayableApplicationCommandAnchor(
                RequiredGuid(reader, "application_command_id"),
                RequiredGuid(reader, "command_decision_id"),
                RequiredGuid(reader, "command_parking_session_id"),
                NullableGuid(reader, "command_site_id"),
                NullableGuid(reader, "command_validation_id"),
                NullableGuid(reader, "command_application_id"),
                NullableGuid(reader, "command_original_tariff_id"),
                NullableGuid(reader, "command_applied_tariff_id"),
                NullableGuid(reader, "command_policy_id"),
                NullableGuid(reader, "command_policy_version_id"),
                RequiredString(reader, "command_status"),
                RequiredString(reader, "command_entitlement_type"),
                RequiredInt64(reader, "command_discount_minor"),
                NullableInt64(reader, "command_vat_minor"),
                RequiredInt64(reader, "command_final_minor"),
                RequiredString(reader, "command_currency"),
                RequiredString(reader, "command_source_channel"),
                RequiredGuid(reader, "command_correlation_id"),
                NullableTimestamp(reader, "command_applied_at")),
            new StatutoryDiscountZeroPayableApplicationAnchor(
                NullableGuid(reader, "application_id"),
                NullableGuid(reader, "application_validation_id"),
                NullableGuid(reader, "application_parking_session_id"),
                NullableGuid(reader, "application_original_tariff_id"),
                NullableGuid(reader, "application_applied_tariff_id"),
                NullableString(reader, "application_status"),
                NullableInt64(reader, "application_gross_minor"),
                NullableInt64(reader, "application_vat_minor"),
                NullableInt64(reader, "application_discount_minor"),
                NullableInt64(reader, "application_final_minor"),
                NullableString(reader, "application_currency"),
                NullableTimestamp(reader, "application_applied_at"),
                NullableGuid(reader, "application_correlation_id")),
            new StatutoryDiscountZeroPayableValidationAnchor(
                NullableGuid(reader, "validation_id"),
                NullableGuid(reader, "validation_parking_session_id"),
                NullableGuid(reader, "validation_tariff_id"),
                NullableGuid(reader, "validation_policy_id"),
                NullableGuid(reader, "validation_policy_version_id"),
                NullableString(reader, "validation_status"),
                NullableString(reader, "validation_entitlement_type"),
                NullableInt64(reader, "validation_gross_minor"),
                NullableInt64(reader, "validation_discount_minor"),
                NullableInt64(reader, "validation_final_minor"),
                NullableString(reader, "validation_currency")),
            new StatutoryDiscountZeroPayablePolicyAnchor(
                NullableGuid(reader, "authority_decision_id"),
                NullableGuid(reader, "authority_policy_version_id"),
                NullableString(reader, "authority_entitlement_type"),
                NullableString(reader, "authority_benefit_type"),
                NullableString(reader, "policy_entitlement_type"),
                NullableString(reader, "policy_benefit_type"),
                NullableString(reader, "policy_support_status"),
                NullableBoolean(reader, "policy_full_fee_exempt"),
                NullableGuid(reader, "policy_site_id"),
                NullableGuid(reader, "policy_site_group_id")),
            new StatutoryDiscountZeroPayableParkingAnchor(
                NullableGuid(reader, "parking_session_id"),
                NullableGuid(reader, "parking_site_id"),
                NullableGuid(reader, "parking_site_group_id")),
            ReadTariff(reader, "original_tariff"),
            ReadTariff(reader, "applied_tariff"),
            reader.GetBoolean(reader.GetOrdinal("has_conflicting_payment_attempt")));

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Zero-payable statutory finality resolved more than one canonical durable row for the decision command.");
        }

        return candidate;
    }

    private static StatutoryDiscountZeroPayableTariffAnchor ReadTariff(NpgsqlDataReader reader, string prefix) =>
        new(
            NullableGuid(reader, $"{prefix}_id"),
            NullableGuid(reader, $"{prefix}_parking_session_id"),
            NullableString(reader, $"{prefix}_status"),
            NullableInt64(reader, $"{prefix}_gross_minor"),
            NullableInt64(reader, $"{prefix}_discount_minor"),
            NullableInt64(reader, $"{prefix}_net_minor"),
            NullableString(reader, $"{prefix}_currency"),
            NullableGuid(reader, $"{prefix}_validation_id"));

    private static Guid RequiredGuid(NpgsqlDataReader reader, string name) => reader.GetGuid(reader.GetOrdinal(name));

    private static Guid? NullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string RequiredString(NpgsqlDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));

    private static string? NullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long RequiredInt64(NpgsqlDataReader reader, string name) => reader.GetInt64(reader.GetOrdinal(name));

    private static long? NullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static bool? NullableBoolean(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateTimeOffset? NullableTimestamp(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
