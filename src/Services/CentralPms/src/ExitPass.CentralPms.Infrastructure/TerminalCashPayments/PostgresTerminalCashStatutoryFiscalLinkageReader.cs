using System.Data;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.TerminalCashPayments;

/// <summary>
/// Reads canonical applied statutory discount context for terminal-cash fiscal issuance.
/// </summary>
public sealed class PostgresTerminalCashStatutoryFiscalLinkageReader : ITerminalCashStatutoryFiscalLinkageReader
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates the reader using the Central PMS main database connection string.
    /// </summary>
    public PostgresTerminalCashStatutoryFiscalLinkageReader(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<TerminalCashStatutoryFiscalLinkageResult> ReadByAppliedTariffSnapshotAsync(
        TerminalCashPaymentReadback cashPayment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cashPayment);

        try
        {
            var rows = await ReadRowsAsync(cashPayment.TariffSnapshotId, cancellationToken).ConfigureAwait(false);
            return Evaluate(cashPayment, rows);
        }
        catch (NpgsqlException)
        {
            return TerminalCashStatutoryFiscalLinkageResult.RetryableUnavailable(
                "STATUTORY_FISCAL_CONTEXT_TEMPORARILY_UNAVAILABLE");
        }
        catch (TimeoutException)
        {
            return TerminalCashStatutoryFiscalLinkageResult.RetryableUnavailable(
                "STATUTORY_FISCAL_CONTEXT_TEMPORARILY_UNAVAILABLE");
        }
    }

    private async Task<IReadOnlyList<StatutoryFiscalLinkageRow>> ReadRowsAsync(
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ts.tariff_snapshot_id,
                ts.parking_session_id AS snapshot_parking_session_id,
                ts.statutory_discount_validation_id AS snapshot_validation_id,
                ts.statutory_discount_amount,
                ts.net_amount AS snapshot_net_amount,
                ts.currency_code AS snapshot_currency_code,
                appcmd.statutory_discount_payable_basis_application_command_id,
                appcmd.statutory_discount_decision_command_id,
                appcmd.parking_session_id AS application_parking_session_id,
                appcmd.site_id AS application_site_id,
                appcmd.entitlement_type,
                appcmd.command_status AS application_command_status,
                appcmd.result_classification AS application_result_classification,
                appcmd.retryable AS application_retryable,
                appcmd.recovery_classification AS application_recovery_classification,
                appcmd.safe_error_code AS application_safe_error_code,
                appcmd.statutory_discount_validation_id AS application_validation_id,
                appcmd.statutory_discount_payable_basis_application_id,
                appcmd.original_tariff_snapshot_id,
                appcmd.applied_tariff_snapshot_id,
                appcmd.applied_policy_reference_id,
                appcmd.policy_resolution_basis,
                COALESCE(app.gross_amount_minor_units, decision.gross_amount_minor_units) AS gross_amount_minor_units,
                appcmd.approved_discount_amount_minor_units,
                appcmd.approved_vat_exclusive_amount_minor_units,
                appcmd.approved_vat_amount_minor_units,
                appcmd.approved_final_payable_amount_minor_units,
                appcmd.currency_code,
                appcmd.source_channel,
                appcmd.applied_at AS application_applied_at,
                decision.command_status AS decision_command_status,
                decision.decision_result_status,
                decision.statutory_discount_validation_id AS decision_validation_id,
                decision.parking_session_id AS decision_parking_session_id,
                decision.decided_at,
                review.site_id AS review_site_id,
                review.site_group_id AS review_site_group_id,
                review.masked_id_reference,
                app.application_status::text AS immutable_application_status,
                app.applied_tariff_snapshot_id AS immutable_applied_tariff_snapshot_id,
                validation.statutory_discount_validation_id AS approved_validation_id
            FROM core.tariff_snapshots AS ts
            LEFT JOIN discounts.statutory_discount_payable_basis_application_commands AS appcmd
              ON appcmd.applied_tariff_snapshot_id = ts.tariff_snapshot_id
            LEFT JOIN discounts.statutory_discount_decision_commands AS decision
              ON decision.statutory_discount_decision_command_id = appcmd.statutory_discount_decision_command_id
            LEFT JOIN operator_console.statutory_discount_service_channel_reviews AS review
              ON review.statutory_discount_decision_command_id = decision.statutory_discount_decision_command_id
            LEFT JOIN discounts.statutory_discount_payable_basis_applications AS app
              ON app.statutory_discount_payable_basis_application_id =
                 appcmd.statutory_discount_payable_basis_application_id
            LEFT JOIN discounts.statutory_discount_validations AS validation
              ON validation.statutory_discount_validation_id =
                 COALESCE(appcmd.statutory_discount_validation_id, decision.statutory_discount_validation_id, ts.statutory_discount_validation_id)
            WHERE ts.tariff_snapshot_id = @tariff_snapshot_id
            ORDER BY appcmd.updated_at DESC NULLS LAST,
                     appcmd.statutory_discount_payable_basis_application_command_id DESC NULLS LAST;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;

        var rows = new List<StatutoryFiscalLinkageRow>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    private static TerminalCashStatutoryFiscalLinkageResult Evaluate(
        TerminalCashPaymentReadback cashPayment,
        IReadOnlyList<StatutoryFiscalLinkageRow> rows)
    {
        if (rows.Count == 0)
        {
            return TerminalCashStatutoryFiscalLinkageResult.NotApplicable();
        }

        var first = rows[0];
        var applicationRows = rows
            .Where(row => row.StatutoryDiscountPayableBasisApplicationCommandId.HasValue)
            .ToArray();

        if (applicationRows.Length == 0)
        {
            return first.SnapshotValidationId.HasValue || first.SnapshotStatutoryDiscountAmount > 0
                ? TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_LINKAGE_MISSING")
                : TerminalCashStatutoryFiscalLinkageResult.NotApplicable();
        }

        if (applicationRows.Length > 1)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_LINKAGE_AMBIGUOUS");
        }

        var row = applicationRows[0];
        var validationId = row.ApplicationValidationId ?? row.DecisionValidationId ?? row.SnapshotValidationId;
        if (!validationId.HasValue || row.ApprovedValidationId != validationId)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_VALIDATION_MISSING");
        }

        if (!string.Equals(row.DecisionCommandStatus, StatutoryDiscountDecisionCommandStatuses.Completed, StringComparison.Ordinal) ||
            !string.Equals(row.DecisionResultStatus, StatutoryDiscountDecisionV2ResultStates.Approved, StringComparison.Ordinal))
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_DECISION_NOT_APPROVED");
        }

        if (!string.Equals(row.ApplicationCommandStatus, StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied, StringComparison.Ordinal) ||
            !IsSuccessfulApplicationResult(row.ApplicationResultClassification) ||
            !string.Equals(row.ImmutableApplicationStatus, StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied, StringComparison.Ordinal))
        {
            return row.ApplicationRetryable
                ? TerminalCashStatutoryFiscalLinkageResult.RetryableUnavailable(row.ApplicationSafeErrorCode ?? "STATUTORY_FISCAL_APPLICATION_RETRYABLE")
                : TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_APPLICATION_NOT_APPLIED");
        }

        if (row.AppliedTariffSnapshotId != cashPayment.TariffSnapshotId ||
            row.ImmutableAppliedTariffSnapshotId != cashPayment.TariffSnapshotId)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_APPLIED_SNAPSHOT_MISMATCH");
        }

        if (row.ApplicationParkingSessionId != cashPayment.ParkingSessionId ||
            row.DecisionParkingSessionId != cashPayment.ParkingSessionId ||
            row.SnapshotParkingSessionId != cashPayment.ParkingSessionId)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_PARKING_SESSION_MISMATCH");
        }

        if (row.ApplicationSiteId.HasValue && row.ApplicationSiteId != cashPayment.SiteId ||
            row.ReviewSiteId.HasValue && row.ReviewSiteId != cashPayment.SiteId)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_SITE_MISMATCH");
        }

        if (row.ReviewSiteGroupId.HasValue && row.ReviewSiteGroupId != cashPayment.SiteGroupId)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_SITE_GROUP_MISMATCH");
        }

        if (!row.OriginalTariffSnapshotId.HasValue ||
            string.IsNullOrWhiteSpace(row.EntitlementType) ||
            string.IsNullOrWhiteSpace(row.Currency) ||
            row.GrossAmountMinorUnits is null ||
            row.ApprovedVatExclusiveAmountMinorUnits is null ||
            row.ApprovedVatAmountMinorUnits is null)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent(
                "STATUTORY_FISCAL_REQUIRED_FACTS_UNAVAILABLE");
        }

        var currency = row.Currency.Trim().ToUpperInvariant();
        if (!string.Equals(currency, cashPayment.Currency.Trim().ToUpperInvariant(), StringComparison.Ordinal) ||
            row.ApprovedFinalPayableAmountMinorUnits != cashPayment.AmountDueMinorUnits)
        {
            return TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent(
                "STATUTORY_FISCAL_AMOUNT_OR_CURRENCY_MISMATCH");
        }

        return TerminalCashStatutoryFiscalLinkageResult.Complete(
            new TerminalCashStatutoryFiscalLinkageContext(
                row.StatutoryDiscountDecisionCommandId!.Value,
                row.StatutoryDiscountPayableBasisApplicationCommandId!.Value,
                validationId.Value,
                row.StatutoryDiscountPayableBasisApplicationId,
                cashPayment.ParkingSessionId,
                row.ApplicationSiteId ?? row.ReviewSiteId,
                row.ReviewSiteGroupId,
                row.OriginalTariffSnapshotId.Value,
                cashPayment.TariffSnapshotId,
                row.AppliedPolicyReferenceId,
                row.PolicyResolutionBasis,
                row.EntitlementType.Trim().ToUpperInvariant(),
                row.SourceChannel?.Trim().ToUpperInvariant() ?? string.Empty,
                row.GrossAmountMinorUnits.Value,
                row.ApprovedVatExclusiveAmountMinorUnits.Value,
                row.ApprovedVatAmountMinorUnits.Value,
                "VAT_EXCLUSIVE",
                row.ApprovedDiscountAmountMinorUnits,
                row.ApprovedFinalPayableAmountMinorUnits,
                currency,
                row.DecidedAt,
                row.ApplicationAppliedAt,
                row.MaskedIdReference));
    }

    private static StatutoryFiscalLinkageRow ReadRow(NpgsqlDataReader reader) =>
        new(
            GetNullableGuid(reader, "snapshot_validation_id"),
            reader.GetGuid(reader.GetOrdinal("snapshot_parking_session_id")),
            reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount")),
            GetNullableGuid(reader, "statutory_discount_payable_basis_application_command_id"),
            GetNullableGuid(reader, "statutory_discount_decision_command_id"),
            GetNullableGuid(reader, "application_parking_session_id"),
            GetNullableGuid(reader, "application_site_id"),
            GetNullableString(reader, "entitlement_type"),
            GetNullableString(reader, "application_command_status"),
            GetNullableString(reader, "application_result_classification"),
            GetNullableBoolean(reader, "application_retryable") ?? false,
            GetNullableString(reader, "application_recovery_classification"),
            GetNullableString(reader, "application_safe_error_code"),
            GetNullableGuid(reader, "application_validation_id"),
            GetNullableGuid(reader, "statutory_discount_payable_basis_application_id"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_policy_reference_id"),
            GetNullableString(reader, "policy_resolution_basis"),
            GetNullableInt64(reader, "gross_amount_minor_units"),
            GetNullableInt64(reader, "approved_discount_amount_minor_units") ?? 0,
            GetNullableInt64(reader, "approved_vat_exclusive_amount_minor_units"),
            GetNullableInt64(reader, "approved_vat_amount_minor_units"),
            GetNullableInt64(reader, "approved_final_payable_amount_minor_units") ?? 0,
            GetNullableString(reader, "currency_code"),
            GetNullableString(reader, "source_channel"),
            GetNullableDateTimeOffset(reader, "application_applied_at"),
            GetNullableString(reader, "decision_command_status"),
            GetNullableString(reader, "decision_result_status"),
            GetNullableGuid(reader, "decision_validation_id"),
            GetNullableGuid(reader, "decision_parking_session_id"),
            GetNullableDateTimeOffset(reader, "decided_at"),
            GetNullableGuid(reader, "review_site_id"),
            GetNullableGuid(reader, "review_site_group_id"),
            GetNullableString(reader, "masked_id_reference"),
            GetNullableString(reader, "immutable_application_status"),
            GetNullableGuid(reader, "immutable_applied_tariff_snapshot_id"),
            GetNullableGuid(reader, "approved_validation_id"));

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? GetNullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static bool? GetNullableBoolean(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static bool IsSuccessfulApplicationResult(string? resultClassification) =>
        string.Equals(
            resultClassification,
            StatutoryDiscountPayableBasisApplicationV1ResultClassifications.Applied,
            StringComparison.Ordinal) ||
        string.Equals(
            resultClassification,
            StatutoryDiscountPayableBasisApplicationV1ResultClassifications.IdempotentReplay,
            StringComparison.Ordinal);

    private sealed record StatutoryFiscalLinkageRow(
        Guid? SnapshotValidationId,
        Guid SnapshotParkingSessionId,
        decimal SnapshotStatutoryDiscountAmount,
        Guid? StatutoryDiscountPayableBasisApplicationCommandId,
        Guid? StatutoryDiscountDecisionCommandId,
        Guid? ApplicationParkingSessionId,
        Guid? ApplicationSiteId,
        string? EntitlementType,
        string? ApplicationCommandStatus,
        string? ApplicationResultClassification,
        bool ApplicationRetryable,
        string? ApplicationRecoveryClassification,
        string? ApplicationSafeErrorCode,
        Guid? ApplicationValidationId,
        Guid? StatutoryDiscountPayableBasisApplicationId,
        Guid? OriginalTariffSnapshotId,
        Guid? AppliedTariffSnapshotId,
        Guid? AppliedPolicyReferenceId,
        string? PolicyResolutionBasis,
        long? GrossAmountMinorUnits,
        long ApprovedDiscountAmountMinorUnits,
        long? ApprovedVatExclusiveAmountMinorUnits,
        long? ApprovedVatAmountMinorUnits,
        long ApprovedFinalPayableAmountMinorUnits,
        string? Currency,
        string? SourceChannel,
        DateTimeOffset? ApplicationAppliedAt,
        string? DecisionCommandStatus,
        string? DecisionResultStatus,
        Guid? DecisionValidationId,
        Guid? DecisionParkingSessionId,
        DateTimeOffset? DecidedAt,
        Guid? ReviewSiteId,
        Guid? ReviewSiteGroupId,
        string? MaskedIdReference,
        string? ImmutableApplicationStatus,
        Guid? ImmutableAppliedTariffSnapshotId,
        Guid? ApprovedValidationId);
}
