using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for statutory discount payable-basis applications.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Writes are limited to a superseding tariff snapshot, immutable application evidence, and statutory validation linkage.
/// - This writer does not create payment attempts, confirmations, provider outcomes, exit authorizations, gate records, coupon applications, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisWriter
    : IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter
{
    private const string ApplicationStatusApplied = "APPLIED";

    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console statutory discount payable-basis writer.
    /// </summary>
    public OperatorConsoleStatutoryDiscountApplyPayableBasisWriter(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ApplyAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await DeferForeignKeysAsync(connection, transaction, cancellationToken);

            var validation = await ReadValidationForUpdateAsync(connection, transaction, command.ValidationId, cancellationToken);
            if (validation is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(command.ValidationId, "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND", "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND");
            }

            var existing = await FindExistingApplicationAsync(connection, transaction, validation.ValidationId, validation.ParkingSessionId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing with { AlreadyApplied = true };
            }

            var ineligible = ValidateEligibility(validation);
            if (ineligible is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, ineligible.Value.IneligibilityReason, ineligible.Value.ErrorCode);
            }

            var session = await ReadParkingSessionForUpdateAsync(connection, transaction, validation.ParkingSessionId, cancellationToken);
            if (session is null || session.SessionStatus != "ACTIVE")
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, "SESSION_NOT_ELIGIBLE", "SESSION_NOT_ELIGIBLE");
            }

            var originalSnapshot = await ReadOriginalTariffSnapshotForUpdateAsync(
                connection,
                transaction,
                validation.ParkingSessionId,
                command.OriginalTariffSnapshotId,
                cancellationToken);

            if (originalSnapshot is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, "TARIFF_SNAPSHOT_NOT_FOUND", "TARIFF_SNAPSHOT_NOT_FOUND");
            }

            var snapshotEligibility = ValidateSnapshotEligibility(validation, originalSnapshot);
            if (snapshotEligibility is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, snapshotEligibility.Value.IneligibilityReason, snapshotEligibility.Value.ErrorCode);
            }

            if (await PaymentAttemptExistsAsync(connection, transaction, validation.ParkingSessionId, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, "PAYMENT_ATTEMPT_ALREADY_EXISTS", "PAYMENT_ATTEMPT_ALREADY_EXISTS");
            }

            var policy = ValidateAndReadPolicySnapshot(validation);
            if (!policy.Valid)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(validation, policy.IneligibilityReason!, policy.ErrorCode!);
            }

            var computation = ComputeAmounts(validation, originalSnapshot, policy.Policy!);
            if (computation.Computed is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return NotAccepted(
                    validation,
                    computation.ErrorCode ?? "STATUTORY_DISCOUNT_COMPUTATION_NOT_SUPPORTED",
                    computation.ErrorCode ?? "STATUTORY_DISCOUNT_COMPUTATION_NOT_SUPPORTED");
            }

            var finalizedApplication = await ApplyLockedSchemaAsync(
                connection,
                transaction,
                command,
                validation,
                originalSnapshot,
                computation.Computed,
                policy.Policy!,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return finalizedApplication;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ResolveUniqueViolationAsync(command, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ResolveUniqueViolationAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var validation = await ReadValidationForUpdateAsync(connection, transaction, command.ValidationId, cancellationToken);
        if (validation is not null)
        {
            var existing = await FindExistingApplicationAsync(connection, transaction, validation.ValidationId, validation.ParkingSessionId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing with { AlreadyApplied = true };
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return NotAccepted(command.ValidationId, "PAYABLE_BASIS_APPLICATION_FAILED", "PAYABLE_BASIS_APPLICATION_FAILED");
    }

    private static async Task DeferForeignKeysAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SET CONSTRAINTS ALL DEFERRED;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ValidationRow?> ReadValidationForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid validationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                sdv.tariff_snapshot_id,
                sdv.entitlement_type::text,
                sdv.validation_status::text,
                sdv.validation_channel::text,
                sdv.evidence_required,
                sdv.evidence_captured,
                sdv.currency_code,
                COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id) AS policy_reference_id,
                NULL::uuid AS resolved_jurisdiction_id,
                sdv.policy_resolution_basis::text,
                jsonb_build_object(
                    'statutoryDiscountPolicyId', p.discount_policy_reference_id,
                    'policyCode', p.policy_code,
                    'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
                    'policyResolutionBasis', sdv.policy_resolution_basis::text,
                    'succeedingHoursDiscountRule', 'STANDARD_20_PERCENT',
                    'discountBaseScope', 'VAT_EXCLUSIVE',
                    'stackingPolicy', 'STATUTORY_FIRST',
                    'legalBasisPriority', COALESCE(p.local_ordinance_reference, p.national_law_reference, 'LOCKED_SCHEMA'),
                    'requiresEvidence', p.requires_evidence_capture,
                    'policyName', p.policy_name,
                    'nationalLawReference', p.national_law_reference,
                    'ordinanceReference', p.local_ordinance_reference
                )::text AS resolved_policy_snapshot_json
            FROM discounts.statutory_discount_validations AS sdv
            LEFT JOIN discounts.discount_policy_references AS p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            WHERE sdv.statutory_discount_validation_id = @statutory_discount_validation_id
            FOR UPDATE OF sdv;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ValidationRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static async Task<ParkingSessionRow?> ReadParkingSessionForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT parking_session_id, session_status::text
            FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ParkingSessionRow(reader.GetGuid(0), reader.GetString(1));
    }

    private static async Task<TariffSnapshotRow?> ReadOriginalTariffSnapshotForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        Guid? originalTariffSnapshotId,
        CancellationToken cancellationToken)
    {
        var sql = originalTariffSnapshotId.HasValue
            ? """
                SELECT
                    tariff_snapshot_id,
                    parking_session_id,
                    vendor_system_id,
                    vendor_tariff_ref,
                    tariff_version_reference,
                    currency_code,
                    gross_amount,
                    statutory_discount_amount,
                    coupon_discount_amount,
                    net_amount,
                    snapshot_status::text,
                    expires_at,
                    created_by_service_identity_id
                FROM core.tariff_snapshots
                WHERE tariff_snapshot_id = @tariff_snapshot_id
                FOR UPDATE;
                """
            : """
                SELECT
                    tariff_snapshot_id,
                    parking_session_id,
                    vendor_system_id,
                    vendor_tariff_ref,
                    tariff_version_reference,
                    currency_code,
                    gross_amount,
                    statutory_discount_amount,
                    coupon_discount_amount,
                    net_amount,
                    snapshot_status::text,
                    expires_at,
                    created_by_service_identity_id
                FROM core.tariff_snapshots
                WHERE parking_session_id = @parking_session_id
                  AND snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
                FOR UPDATE;
                """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = originalTariffSnapshotId ?? Guid.Empty;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TariffSnapshotRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetString(10),
            reader.GetDateTime(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }

    private static async Task<bool> PaymentAttemptExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.payment_attempts
                WHERE parking_session_id = @parking_session_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult?> FindExistingApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid validationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                app.statutory_discount_payable_basis_application_id AS payable_basis_application_id,
                app.statutory_discount_validation_id,
                app.parking_session_id,
                app.original_tariff_snapshot_id,
                app.applied_tariff_snapshot_id,
                app.application_status::text,
                app.gross_amount_minor_units,
                app.vat_amount_minor_units,
                app.vat_exclusive_amount_minor_units,
                app.statutory_discount_amount_minor_units,
                app.final_payable_amount_minor_units,
                app.currency_code,
                app.computation_basis_json::text AS computation_basis_json
            FROM discounts.statutory_discount_payable_basis_applications AS app
            WHERE app.application_status = 'APPLIED'::discounts.statutory_discount_payable_application_status_enum
              AND (
                    app.statutory_discount_validation_id = @statutory_discount_validation_id
                 OR app.parking_session_id = @parking_session_id
              )
            ORDER BY app.applied_at DESC NULLS LAST, app.updated_at DESC, app.statutory_discount_payable_basis_application_id DESC
            LIMIT 1
            FOR UPDATE OF app;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = reader.GetString(5);
        var accepted = status is ApplicationStatusApplied;
        var policy = ReadPolicySummaryFromComputationBasis(reader.GetString(12));
        return new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult(
            ApplicationAccepted: accepted,
            ApplicationPersisted: true,
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            status,
            AlreadyApplied: status == ApplicationStatusApplied,
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetString(11),
            policy.StatutoryDiscountPolicyId,
            policy.ResolvedJurisdictionId,
            policy.PolicyResolutionBasis,
            policy.PolicyCode,
            policy.BenefitType,
            policy.NationalLawReference,
            policy.OrdinanceReference,
            policy.PolicySnapshotUsed,
            IneligibilityReason: accepted ? null : "PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
            ErrorCode: null);
    }

    private static async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ApplyLockedSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        ValidationRow validation,
        TariffSnapshotRow originalSnapshot,
        ComputedPayableBasis computed,
        PolicySnapshotContext policy,
        CancellationToken cancellationToken)
    {
        var applicationId = await InsertRequestedApplicationAsync(
            connection,
            transaction,
            command,
            validation,
            originalSnapshot,
            computed,
            policy,
            cancellationToken);

        var finalized = await FinalizeRequestedApplicationAsync(
            connection,
            transaction,
            applicationId,
            command.AppliedByUserId,
            command.CorrelationId,
            cancellationToken);

        if (finalized is null)
        {
            await DeleteRequestedApplicationAsync(connection, transaction, applicationId, cancellationToken);
            return NotAccepted(validation, "PAYABLE_BASIS_APPLICATION_FAILED", "PAYABLE_BASIS_APPLICATION_FAILED");
        }

        if (finalized.OutcomeCode is not "APPLIED" and not "ALREADY_APPLIED")
        {
            await DeleteRequestedApplicationAsync(connection, transaction, applicationId, cancellationToken);
            return NotAccepted(
                validation,
                finalized.FailureCode ?? finalized.OutcomeCode,
                finalized.FailureCode ?? finalized.OutcomeCode);
        }

        if (command.AppliedByServiceIdentityId.HasValue && finalized.AppliedTariffSnapshotId.HasValue)
        {
            await AttributeServiceApplicationAsync(
                connection,
                transaction,
                validation.ValidationId,
                applicationId,
                finalized.AppliedTariffSnapshotId.Value,
                command.AppliedByServiceIdentityId.Value,
                cancellationToken);
        }

        return new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult(
            ApplicationAccepted: true,
            ApplicationPersisted: true,
            applicationId,
            validation.ValidationId,
            validation.ParkingSessionId,
            originalSnapshot.TariffSnapshotId,
            finalized.AppliedTariffSnapshotId,
            finalized.ApplicationStatus ?? ApplicationStatusApplied,
            finalized.AlreadyApplied,
            computed.GrossAmountMinorUnits,
            computed.VatAmountMinorUnits,
            computed.VatExclusiveAmountMinorUnits,
            computed.StatutoryDiscountAmountMinorUnits,
            computed.FinalPayableAmountMinorUnits,
            originalSnapshot.CurrencyCode,
            policy.StatutoryDiscountPolicyId,
            policy.ResolvedJurisdictionId,
            policy.PolicyResolutionBasis,
            policy.PolicyCode,
            policy.BenefitType,
            policy.NationalLawReference,
            policy.OrdinanceReference,
            PolicySnapshotUsed: true,
            IneligibilityReason: null,
            ErrorCode: null);
    }

    private static async Task<Guid> InsertRequestedApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        ValidationRow validation,
        TariffSnapshotRow originalSnapshot,
        ComputedPayableBasis computed,
        PolicySnapshotContext policy,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_payable_basis_applications (
                statutory_discount_validation_id,
                parking_session_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                application_status,
                application_channel,
                gross_amount_minor_units,
                vat_amount_minor_units,
                vat_exclusive_amount_minor_units,
                statutory_discount_amount_minor_units,
                final_payable_amount_minor_units,
                currency_code,
                computation_basis_json,
                rounding_mode,
                applied_at,
                applied_by_user_id,
                applied_by_service_identity_id,
                idempotency_key,
                correlation_id,
                created_at,
                created_by_user_id,
                created_by_service_identity_id,
                updated_at,
                updated_by_user_id,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @statutory_discount_validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                NULL,
                'REQUESTED'::discounts.statutory_discount_payable_application_status_enum,
                @application_channel::discounts.statutory_discount_payable_application_channel_enum,
                @gross_amount_minor_units,
                @vat_amount_minor_units,
                @vat_exclusive_amount_minor_units,
                @statutory_discount_amount_minor_units,
                @final_payable_amount_minor_units,
                @currency_code,
                CAST(@computation_basis_json AS jsonb),
                @rounding_mode,
                NULL,
                NULL,
                @applied_by_service_identity_id,
                @idempotency_key,
                @correlation_id,
                now(),
                @created_by_user_id,
                @created_by_service_identity_id,
                now(),
                @updated_by_user_id,
                @updated_by_service_identity_id,
                1
            )
            RETURNING statutory_discount_payable_basis_application_id;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validation.ValidationId;
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = validation.ParkingSessionId;
        npgsqlCommand.Parameters.Add("original_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = originalSnapshot.TariffSnapshotId;
        npgsqlCommand.Parameters.Add("gross_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.GrossAmountMinorUnits;
        npgsqlCommand.Parameters.Add("vat_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.VatAmountMinorUnits;
        npgsqlCommand.Parameters.Add("vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.VatExclusiveAmountMinorUnits;
        npgsqlCommand.Parameters.Add("statutory_discount_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.StatutoryDiscountAmountMinorUnits;
        npgsqlCommand.Parameters.Add("final_payable_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.FinalPayableAmountMinorUnits;
        npgsqlCommand.Parameters.Add("currency_code", NpgsqlDbType.Varchar).Value = originalSnapshot.CurrencyCode;
        npgsqlCommand.Parameters.Add("computation_basis_json", NpgsqlDbType.Jsonb).Value = BuildComputationBasisJson(originalSnapshot, policy);
        npgsqlCommand.Parameters.Add("rounding_mode", NpgsqlDbType.Varchar).Value = OperatorConsoleStatutoryDiscountComputationContract.RoundingMode;
        npgsqlCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.IdempotencyKey;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.AddWithValue("application_channel", command.ApplicationChannel);
        AddNullableGuid(npgsqlCommand, "applied_by_service_identity_id", command.AppliedByServiceIdentityId);
        AddNullableGuid(npgsqlCommand, "created_by_user_id", command.AppliedByUserId);
        AddNullableGuid(npgsqlCommand, "created_by_service_identity_id", command.AppliedByServiceIdentityId);
        AddNullableGuid(npgsqlCommand, "updated_by_user_id", command.AppliedByUserId);
        AddNullableGuid(npgsqlCommand, "updated_by_service_identity_id", command.AppliedByServiceIdentityId);

        return (Guid)(await npgsqlCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected statutory discount payable-basis application id."));
    }

    private static async Task<AppliedPayableBasisRoutineResult?> FinalizeRequestedApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid? actorUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_payable_basis_application_id,
                statutory_discount_validation_id,
                parking_session_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                application_status,
                final_payable_amount_minor_units,
                currency_code,
                already_applied,
                outcome_code,
                failure_code
            FROM discounts.apply_statutory_discount_payable_basis(
                @statutory_discount_payable_basis_application_id,
                @actor_user_id,
                @correlation_id);
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_payable_basis_application_id", NpgsqlDbType.Uuid).Value = applicationId;
        AddNullableGuid(npgsqlCommand, "actor_user_id", actorUserId);
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AppliedPayableBasisRoutineResult(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetBoolean(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    private static async Task AttributeServiceApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid validationId,
        Guid applicationId,
        Guid appliedTariffSnapshotId,
        Guid serviceIdentityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
            SET updated_by_user_id = NULL,
                updated_by_service_identity_id = @service_identity_id
            WHERE statutory_discount_validation_id = @validation_id;

            UPDATE discounts.statutory_discount_payable_basis_applications
            SET application_channel = 'SYSTEM'::discounts.statutory_discount_payable_application_channel_enum,
                applied_by_user_id = NULL,
                applied_by_service_identity_id = @service_identity_id,
                created_by_user_id = NULL,
                created_by_service_identity_id = @service_identity_id,
                updated_by_user_id = NULL,
                updated_by_service_identity_id = @service_identity_id
            WHERE statutory_discount_payable_basis_application_id = @application_id;

            UPDATE core.tariff_snapshots
            SET created_by_service_identity_id = @service_identity_id,
                updated_by_service_identity_id = @service_identity_id
            WHERE tariff_snapshot_id = @applied_tariff_snapshot_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = serviceIdentityId;
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("application_id", NpgsqlDbType.Uuid).Value = applicationId;
        command.Parameters.Add("applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = appliedTariffSnapshotId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateActor(OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command)
    {
        if (command.AppliedByUserId.HasValue == command.AppliedByServiceIdentityId.HasValue)
        {
            throw new ArgumentException("Exactly one payable-basis application actor is required.", nameof(command));
        }

        var expectedChannel = command.AppliedByServiceIdentityId.HasValue ? "SYSTEM" : "OPERATOR_CONSOLE";
        if (!string.Equals(command.ApplicationChannel, expectedChannel, StringComparison.Ordinal))
        {
            throw new ArgumentException("Payable-basis application channel does not match the authenticated actor.", nameof(command));
        }
    }

    private static void AddNullableGuid(NpgsqlCommand command, string parameterName, Guid? value) =>
        command.Parameters.Add(parameterName, NpgsqlDbType.Uuid).Value = (object?)value ?? DBNull.Value;

    private static async Task DeleteRequestedApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_payable_basis_application_id = @statutory_discount_payable_basis_application_id
              AND application_status = 'REQUESTED'::discounts.statutory_discount_payable_application_status_enum;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_payable_basis_application_id", NpgsqlDbType.Uuid).Value = applicationId;
        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string IneligibilityReason, string ErrorCode)? ValidateEligibility(ValidationRow validation)
    {
        if (validation.ValidationStatus != "APPROVED")
        {
            return ("STATUTORY_DISCOUNT_NOT_APPROVED", "STATUTORY_DISCOUNT_NOT_APPROVED");
        }

        if (validation.EvidenceRequired && !validation.EvidenceCaptured)
        {
            return ("EVIDENCE_REQUIRED_NOT_CAPTURED", "EVIDENCE_REQUIRED_NOT_CAPTURED");
        }

        return null;
    }

    private static (string IneligibilityReason, string ErrorCode)? ValidateSnapshotEligibility(
        ValidationRow validation,
        TariffSnapshotRow snapshot)
    {
        if (snapshot.ParkingSessionId != validation.ParkingSessionId)
        {
            return ("TARIFF_SNAPSHOT_SESSION_MISMATCH", "TARIFF_SNAPSHOT_SESSION_MISMATCH");
        }

        if (snapshot.SnapshotStatus != "ACTIVE")
        {
            return ("TARIFF_SNAPSHOT_NOT_FOUND", "TARIFF_SNAPSHOT_NOT_FOUND");
        }

        if (snapshot.GrossAmount <= 0 || snapshot.NetAmount <= 0)
        {
            return ("PAYABLE_BASIS_COMPONENTS_MISSING", "PAYABLE_BASIS_COMPONENTS_MISSING");
        }

        if (snapshot.StatutoryDiscountAmount > 0)
        {
            return ("STATUTORY_DISCOUNT_ALREADY_APPLIED", "STATUTORY_DISCOUNT_ALREADY_APPLIED");
        }

        if (snapshot.CouponDiscountAmount > 0)
        {
            return ("COUPON_COMPOSITION_NOT_SUPPORTED", "PAYABLE_BASIS_COMPONENTS_MISSING");
        }

        if (snapshot.ExpiresAt <= DateTime.UtcNow)
        {
            return ("SESSION_NOT_ELIGIBLE", "SESSION_NOT_ELIGIBLE");
        }

        return null;
    }

    private static (ComputedPayableBasis? Computed, string? ErrorCode) ComputeAmounts(
        ValidationRow validation,
        TariffSnapshotRow originalSnapshot,
        PolicySnapshotContext policy)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            new OperatorConsoleStatutoryDiscountComputationRequest(
                ToMinorUnits(originalSnapshot.GrossAmount),
                validation.EntitlementType,
                policy.BenefitType,
                policy.DiscountBaseScope));

        if (!result.Accepted)
        {
            return (null, result.ErrorCode);
        }

        return (new ComputedPayableBasis(
            result.GrossAmountMinorUnits,
            result.VatAmountMinorUnits!.Value,
            result.VatExclusiveAmountMinorUnits!.Value,
            result.StatutoryDiscountAmountMinorUnits!.Value,
            result.FinalPayableAmountMinorUnits!.Value,
            originalSnapshot.CurrencyCode), null);
    }

    private static string BuildComputationBasisJson(TariffSnapshotRow originalSnapshot, PolicySnapshotContext policy) =>
        JsonSerializer.Serialize(new
        {
            basis = "GROSS_INCLUSIVE_OF_VAT",
            sourceTariffSnapshotId = originalSnapshot.TariffSnapshotId,
            vatRate = OperatorConsoleStatutoryDiscountComputationContract.VatRate,
            statutoryDiscountRate = OperatorConsoleStatutoryDiscountComputationContract.StatutoryDiscountRate,
            formula = "final_payable = round(gross / 1.12) - round(round(gross / 1.12) * 0.20)",
            roundingMode = OperatorConsoleStatutoryDiscountComputationContract.RoundingMode,
            policyContext = new
            {
                statutoryDiscountPolicyId = policy.StatutoryDiscountPolicyId,
                resolvedJurisdictionId = policy.ResolvedJurisdictionId,
                policyResolutionBasis = policy.PolicyResolutionBasis,
                policyCode = policy.PolicyCode,
                policyName = policy.PolicyName,
                legalBasisReference = policy.LegalBasisReference,
                ordinanceReference = policy.OrdinanceReference,
                nationalLawReference = policy.NationalLawReference,
                benefitType = policy.BenefitType,
                freeDurationMinutes = policy.FreeDurationMinutes,
                initialRateExempt = policy.InitialRateExempt,
                fullFeeExempt = policy.FullFeeExempt,
                freePeriodApplication = policy.FreePeriodApplication,
                succeedingHoursDiscountRule = policy.SucceedingHoursDiscountRule,
                discountBaseScope = policy.DiscountBaseScope,
                stackingPolicy = policy.StackingPolicy,
                legalBasisPriority = policy.LegalBasisPriority,
                requiresEvidence = policy.RequiresEvidence,
                snapshotHash = policy.SnapshotHash
            }
        });

    private static (bool Valid, PolicySnapshotContext? Policy, string? IneligibilityReason, string? ErrorCode)
        ValidateAndReadPolicySnapshot(ValidationRow validation)
    {
        if (!validation.StatutoryDiscountPolicyId.HasValue ||
            string.IsNullOrWhiteSpace(validation.PolicyResolutionBasis) ||
            string.IsNullOrWhiteSpace(validation.ResolvedPolicySnapshotJson))
        {
            return (false, null, "STATUTORY_DISCOUNT_POLICY_CONTEXT_MISSING", "STATUTORY_DISCOUNT_POLICY_CONTEXT_MISSING");
        }

        try
        {
            using var document = JsonDocument.Parse(validation.ResolvedPolicySnapshotJson);
            var root = document.RootElement;
            var snapshotPolicyId = RequiredGuid(root, "statutoryDiscountPolicyId");
            var policyCode = RequiredString(root, "policyCode");
            var benefitType = RequiredString(root, "benefitType");
            var policyResolutionBasis = RequiredString(root, "policyResolutionBasis");
            var succeedingHoursDiscountRule = RequiredString(root, "succeedingHoursDiscountRule");
            var discountBaseScope = RequiredString(root, "discountBaseScope");
            var stackingPolicy = RequiredString(root, "stackingPolicy");
            var legalBasisPriority = RequiredString(root, "legalBasisPriority");
            var requiresEvidence = RequiredBoolean(root, "requiresEvidence");

            if (snapshotPolicyId != validation.StatutoryDiscountPolicyId.Value)
            {
                return (false, null, "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID", "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
            }

            if (!string.Equals(benefitType, "STATUTORY_DISCOUNT_VAT_EXEMPT", StringComparison.Ordinal))
            {
                return (
                    false,
                    null,
                    "POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION",
                    "POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
            }

            return (
                true,
                new PolicySnapshotContext(
                    snapshotPolicyId,
                    validation.ResolvedJurisdictionId,
                    policyResolutionBasis,
                    policyCode,
                    OptionalString(root, "policyName"),
                    OptionalString(root, "legalBasisReference"),
                    OptionalString(root, "ordinanceReference"),
                    OptionalString(root, "nationalLawReference"),
                    benefitType,
                    OptionalInt(root, "freeDurationMinutes"),
                    OptionalBool(root, "initialRateExempt") ?? false,
                    OptionalBool(root, "fullFeeExempt") ?? false,
                    OptionalString(root, "freePeriodApplication"),
                    succeedingHoursDiscountRule,
                    discountBaseScope,
                    stackingPolicy,
                    legalBasisPriority,
                    requiresEvidence,
                    SnapshotHash: null),
                null,
                null);
        }
        catch (JsonException)
        {
            return (false, null, "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID", "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
        }
        catch (InvalidOperationException)
        {
            return (false, null, "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID", "STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
        }
    }

    private static PolicySummary ReadPolicySummaryFromComputationBasis(string computationBasisJson)
    {
        try
        {
            using var document = JsonDocument.Parse(computationBasisJson);
            if (!document.RootElement.TryGetProperty("policyContext", out var policyContext) ||
                policyContext.ValueKind != JsonValueKind.Object)
            {
                return PolicySummary.Empty;
            }

            return new PolicySummary(
                OptionalGuid(policyContext, "statutoryDiscountPolicyId"),
                OptionalGuid(policyContext, "resolvedJurisdictionId"),
                OptionalString(policyContext, "policyResolutionBasis"),
                OptionalString(policyContext, "policyCode"),
                OptionalString(policyContext, "benefitType"),
                OptionalString(policyContext, "nationalLawReference"),
                OptionalString(policyContext, "ordinanceReference"),
                PolicySnapshotUsed: true);
        }
        catch (JsonException)
        {
            return PolicySummary.Empty;
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = OptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{propertyName} is required.");
        }

        return value;
    }

    private static bool RequiredBoolean(JsonElement element, string propertyName) =>
        OptionalBool(element, propertyName) ?? throw new InvalidOperationException($"{propertyName} is required.");

    private static Guid RequiredGuid(JsonElement element, string propertyName) =>
        OptionalGuid(element, propertyName) ?? throw new InvalidOperationException($"{propertyName} is required.");

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? OptionalBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? OptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetInt32(out var result) ? result : null;
    }

    private static Guid? OptionalGuid(JsonElement element, string propertyName)
    {
        var value = OptionalString(element, propertyName);
        return Guid.TryParse(value, out var result) ? result : null;
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult NotAccepted(
        ValidationRow validation,
        string ineligibilityReason,
        string errorCode) =>
        new(
            ApplicationAccepted: false,
            ApplicationPersisted: false,
            PayableBasisApplicationId: null,
            validation.ValidationId,
            validation.ParkingSessionId,
            validation.TariffSnapshotId,
            AppliedTariffSnapshotId: null,
            ApplicationStatus: null,
            AlreadyApplied: false,
            GrossAmountMinorUnits: null,
            VatAmountMinorUnits: null,
            VatExclusiveAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: null,
            FinalPayableAmountMinorUnits: null,
            validation.CurrencyCode,
            validation.StatutoryDiscountPolicyId,
            validation.ResolvedJurisdictionId,
            validation.PolicyResolutionBasis,
            PolicyCode: null,
            BenefitType: null,
            NationalLawReference: null,
            OrdinanceReference: null,
            PolicySnapshotUsed: false,
            ineligibilityReason,
            errorCode);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult NotAccepted(
        Guid validationId,
        string ineligibilityReason,
        string errorCode) =>
        new(
            ApplicationAccepted: false,
            ApplicationPersisted: false,
            PayableBasisApplicationId: null,
            validationId,
            ParkingSessionId: null,
            OriginalTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            ApplicationStatus: null,
            AlreadyApplied: false,
            GrossAmountMinorUnits: null,
            VatAmountMinorUnits: null,
            VatExclusiveAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: null,
            FinalPayableAmountMinorUnits: null,
            CurrencyCode: null,
            StatutoryDiscountPolicyId: null,
            ResolvedJurisdictionId: null,
            PolicyResolutionBasis: null,
            PolicyCode: null,
            BenefitType: null,
            NationalLawReference: null,
            OrdinanceReference: null,
            PolicySnapshotUsed: false,
            ineligibilityReason,
            errorCode);

    private static long ToMinorUnits(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static decimal ToMajorUnits(long amountMinorUnits) => amountMinorUnits / 100m;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private sealed record ValidationRow(
        Guid ValidationId,
        Guid ParkingSessionId,
        Guid? TariffSnapshotId,
        string EntitlementType,
        string ValidationStatus,
        string ValidationChannel,
        bool EvidenceRequired,
        bool EvidenceCaptured,
        string? CurrencyCode,
        Guid? StatutoryDiscountPolicyId,
        Guid? ResolvedJurisdictionId,
        string? PolicyResolutionBasis,
        string? ResolvedPolicySnapshotJson);

    private sealed record ParkingSessionRow(Guid ParkingSessionId, string SessionStatus);

    private sealed record TariffSnapshotRow(
        Guid TariffSnapshotId,
        Guid ParkingSessionId,
        Guid VendorSystemId,
        string? VendorTariffRef,
        string? TariffVersionReference,
        string CurrencyCode,
        decimal GrossAmount,
        decimal StatutoryDiscountAmount,
        decimal CouponDiscountAmount,
        decimal NetAmount,
        string SnapshotStatus,
        DateTime ExpiresAt,
        Guid? ServiceIdentityId);

    private sealed record ComputedPayableBasis(
        long GrossAmountMinorUnits,
        long VatAmountMinorUnits,
        long VatExclusiveAmountMinorUnits,
        long StatutoryDiscountAmountMinorUnits,
        long FinalPayableAmountMinorUnits,
        string CurrencyCode);

    private sealed record PolicySnapshotContext(
        Guid StatutoryDiscountPolicyId,
        Guid? ResolvedJurisdictionId,
        string PolicyResolutionBasis,
        string PolicyCode,
        string? PolicyName,
        string? LegalBasisReference,
        string? OrdinanceReference,
        string? NationalLawReference,
        string BenefitType,
        int? FreeDurationMinutes,
        bool InitialRateExempt,
        bool FullFeeExempt,
        string? FreePeriodApplication,
        string SucceedingHoursDiscountRule,
        string DiscountBaseScope,
        string StackingPolicy,
        string LegalBasisPriority,
        bool RequiresEvidence,
        string? SnapshotHash);

    private sealed record AppliedPayableBasisRoutineResult(
        Guid ApplicationId,
        Guid? ValidationId,
        Guid? ParkingSessionId,
        Guid? OriginalTariffSnapshotId,
        Guid? AppliedTariffSnapshotId,
        string? ApplicationStatus,
        long? FinalPayableAmountMinorUnits,
        string? CurrencyCode,
        bool AlreadyApplied,
        string OutcomeCode,
        string? FailureCode);

    private sealed record PolicySummary(
        Guid? StatutoryDiscountPolicyId,
        Guid? ResolvedJurisdictionId,
        string? PolicyResolutionBasis,
        string? PolicyCode,
        string? BenefitType,
        string? NationalLawReference,
        string? OrdinanceReference,
        bool PolicySnapshotUsed)
    {
        public static PolicySummary Empty { get; } = new(
            StatutoryDiscountPolicyId: null,
            ResolvedJurisdictionId: null,
            PolicyResolutionBasis: null,
            PolicyCode: null,
            BenefitType: null,
            NationalLawReference: null,
            OrdinanceReference: null,
            PolicySnapshotUsed: false);
    }
}
