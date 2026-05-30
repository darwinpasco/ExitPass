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
    private const decimal VatRate = 0.12m;
    private const decimal StatutoryDiscountRate = 0.20m;
    private const string ApplicationStatusRequested = "REQUESTED";
    private const string ApplicationStatusApplied = "APPLIED";
    private const string ApplicationChannel = "OPERATOR_CONSOLE";
    private const string RoundingMode = "HALF_AWAY_FROM_ZERO";

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
                return existing with { AlreadyApplied = existing.ApplicationStatus == ApplicationStatusApplied };
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

            var computed = ComputeAmounts(originalSnapshot);
            var applicationId = Guid.NewGuid();

            var application = await InsertApplicationAsync(
                connection,
                transaction,
                command,
                validation,
                originalSnapshot,
                applicationId,
                appliedTariffSnapshotId: null,
                computed,
                policy.Policy!,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return application;
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
                return existing with { AlreadyApplied = existing.ApplicationStatus == ApplicationStatusApplied };
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
                statutory_discount_validation_id,
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type::text,
                validation_status::text,
                validation_channel::text,
                evidence_required,
                evidence_captured,
                currency_code,
                statutory_discount_policy_id,
                resolved_jurisdiction_id,
                policy_resolution_basis::text,
                resolved_policy_snapshot_json
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
            FOR UPDATE;
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
                statutory_discount_payable_basis_application_id,
                statutory_discount_validation_id,
                parking_session_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                application_status::text,
                gross_amount_minor_units,
                vat_amount_minor_units,
                vat_exclusive_amount_minor_units,
                statutory_discount_amount_minor_units,
                final_payable_amount_minor_units,
                currency_code,
                computation_basis_json
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
               OR parking_session_id = @parking_session_id
            ORDER BY created_at DESC, statutory_discount_payable_basis_application_id DESC
            LIMIT 1
            FOR UPDATE;
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
        var accepted = status is ApplicationStatusApplied or ApplicationStatusRequested;
        var policy = ReadPolicySummaryFromComputationBasis(reader.GetString(12));
        return new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult(
            ApplicationAccepted: accepted,
            ApplicationPersisted: true,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
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

    private static async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> InsertApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        ValidationRow validation,
        TariffSnapshotRow originalSnapshot,
        Guid applicationId,
        Guid? appliedTariffSnapshotId,
        ComputedPayableBasis computed,
        PolicySnapshotContext policy,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_payable_basis_applications (
                statutory_discount_payable_basis_application_id,
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
                idempotency_key,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @statutory_discount_payable_basis_application_id,
                @statutory_discount_validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                @applied_tariff_snapshot_id,
                @application_status::discounts.statutory_discount_payable_application_status_enum,
                @application_channel::discounts.statutory_discount_payable_application_channel_enum,
                @gross_amount_minor_units,
                @vat_amount_minor_units,
                @vat_exclusive_amount_minor_units,
                @statutory_discount_amount_minor_units,
                @final_payable_amount_minor_units,
                @currency_code,
                @computation_basis_json::jsonb,
                @rounding_mode,
                NULL,
                NULL,
                @idempotency_key,
                @correlation_id,
                @created_by_user_id,
                @updated_by_user_id
            )
            RETURNING
                statutory_discount_payable_basis_application_id,
                application_status::text;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_payable_basis_application_id", NpgsqlDbType.Uuid).Value = applicationId;
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validation.ValidationId;
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = validation.ParkingSessionId;
        npgsqlCommand.Parameters.Add("original_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = originalSnapshot.TariffSnapshotId;
        npgsqlCommand.Parameters.Add("applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = DbValue(appliedTariffSnapshotId);
        npgsqlCommand.Parameters.Add("application_status", NpgsqlDbType.Text).Value = ApplicationStatusRequested;
        npgsqlCommand.Parameters.Add("application_channel", NpgsqlDbType.Text).Value = ApplicationChannel;
        npgsqlCommand.Parameters.Add("gross_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.GrossAmountMinorUnits;
        npgsqlCommand.Parameters.Add("vat_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.VatAmountMinorUnits;
        npgsqlCommand.Parameters.Add("vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.VatExclusiveAmountMinorUnits;
        npgsqlCommand.Parameters.Add("statutory_discount_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.StatutoryDiscountAmountMinorUnits;
        npgsqlCommand.Parameters.Add("final_payable_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.FinalPayableAmountMinorUnits;
        npgsqlCommand.Parameters.Add("currency_code", NpgsqlDbType.Varchar).Value = originalSnapshot.CurrencyCode;
        npgsqlCommand.Parameters.Add("computation_basis_json", NpgsqlDbType.Jsonb).Value = BuildComputationBasisJson(originalSnapshot, policy);
        npgsqlCommand.Parameters.Add("rounding_mode", NpgsqlDbType.Varchar).Value = RoundingMode;
        npgsqlCommand.Parameters.Add("applied_by_user_id", NpgsqlDbType.Uuid).Value = command.AppliedByUserId;
        npgsqlCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.IdempotencyKey;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.AppliedByUserId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.AppliedByUserId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount payable-basis application insert did not return an application ID.");
        }

        return new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult(
            ApplicationAccepted: true,
            ApplicationPersisted: true,
            reader.GetGuid(0),
            validation.ValidationId,
            validation.ParkingSessionId,
            originalSnapshot.TariffSnapshotId,
            appliedTariffSnapshotId,
            reader.GetString(1),
            AlreadyApplied: false,
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

    private static ComputedPayableBasis ComputeAmounts(TariffSnapshotRow originalSnapshot)
    {
        var grossMinorUnits = ToMinorUnits(originalSnapshot.GrossAmount);
        var vatExclusiveMinorUnits = decimal.ToInt64(decimal.Round(
            grossMinorUnits / (1m + VatRate),
            0,
            MidpointRounding.AwayFromZero));
        var vatMinorUnits = grossMinorUnits - vatExclusiveMinorUnits;
        var statutoryDiscountMinorUnits = decimal.ToInt64(decimal.Round(
            vatExclusiveMinorUnits * StatutoryDiscountRate,
            0,
            MidpointRounding.AwayFromZero));
        var finalPayableMinorUnits = vatExclusiveMinorUnits - statutoryDiscountMinorUnits;

        return new ComputedPayableBasis(
            grossMinorUnits,
            vatMinorUnits,
            vatExclusiveMinorUnits,
            statutoryDiscountMinorUnits,
            finalPayableMinorUnits,
            originalSnapshot.CurrencyCode);
    }

    private static string BuildComputationBasisJson(TariffSnapshotRow originalSnapshot, PolicySnapshotContext policy) =>
        JsonSerializer.Serialize(new
        {
            basis = "GROSS_INCLUSIVE_OF_VAT",
            sourceTariffSnapshotId = originalSnapshot.TariffSnapshotId,
            vatRate = VatRate,
            statutoryDiscountRate = StatutoryDiscountRate,
            formula = "final_payable = round(gross / 1.12) - round(round(gross / 1.12) * 0.20)",
            roundingMode = RoundingMode,
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
