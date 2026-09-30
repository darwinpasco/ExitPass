using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for privacy-minimized Operator Console statutory discount validation drafts.
///
/// ExitPass v1.3 Invariants Enforced:
/// - Writes are limited to discounts.statutory_discount_validations and restricted discounts.discount_evidence_references rows.
/// - Raw evidence bytes are stored only by the protected object-storage boundary and never enter PostgreSQL.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftWriter : IOperatorConsoleStatutoryDiscountDraftWriter
{
    private const string PlaceholderEvidenceStorageType = "EXTERNAL_REFERENCE";
    private const string ProtectedEvidenceStorageType = "OBJECT_STORAGE";
    private const string PlaceholderEvidenceCaptureStatus = "REFERENCED";
    private const string ProtectedEvidenceCaptureStatus = "CAPTURED";
    private const string EvidenceAccessClassification = "RESTRICTED";
    private const string EvidenceRedactionStatus = "NOT_REDACTED";
    private const string PlaceholderEvidenceRetentionPolicyCode = "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_METADATA_V1";
    private const string ProtectedEvidenceRetentionPolicyCode = "OPERATOR_CONSOLE_STATUTORY_ID_PHOTO_V1";

    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console statutory discount validation draft writer.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDraftWriter(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult?> FindReusableAsync(
        Guid parkingSessionId,
        string entitlementType,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await FindReusableDraftAsync(
            connection,
            transaction: null,
            parkingSessionId,
            entitlementType,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await FindReusableDraftAsync(
                connection,
                transaction,
                command.ParkingSessionId,
                command.EntitlementType,
                cancellationToken);
            if (existing is not null)
            {
                existing = await EnsureEvidenceMetadataAsync(connection, transaction, command, existing, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existing with { ReusedExistingDraft = true };
            }

            var result = await InsertDraftAsync(connection, transaction, command, cancellationToken);
            result = await EnsureEvidenceMetadataAsync(connection, transaction, command, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
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

    private async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> ResolveUniqueViolationAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await FindReusableDraftAsync(
            connection,
            transaction,
            command.ParkingSessionId,
            command.EntitlementType,
            cancellationToken);
        if (existing is not null)
        {
            existing = await EnsureEvidenceMetadataAsync(connection, transaction, command, existing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing with { ReusedExistingDraft = true };
        }

        await transaction.RollbackAsync(cancellationToken);
        throw new OperatorConsoleStatutoryDiscountDraftAlreadyExistsException(
            command.ParkingSessionId,
            command.EntitlementType);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult?> FindReusableDraftAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid parkingSessionId,
        string entitlementType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.validation_status::text AS validation_status,
                sdv.evidence_required,
                COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id) AS statutory_discount_policy_id,
                NULL::uuid AS resolved_jurisdiction_id,
                sdv.parking_session_id,
                ps.site_id,
                ps.site_group_id,
                p.policy_code,
                p.policy_name,
                sdv.entitlement_type::text,
                sdv.policy_resolution_basis::text,
                p.policy_level::text,
                p.policy_type::text,
                COALESCE(p.local_ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.local_ordinance_reference AS ordinance_reference,
                p.national_law_reference,
                p.policy_status::text AS verification_status,
                'LOCKED_SCHEMA_POLICY_REFERENCE' AS beneficiary_residency_scope,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type,
                NULL::integer AS free_duration_minutes,
                false AS initial_rate_exempt_flag,
                false AS full_fee_exempt_flag,
                false AS overnight_excluded_flag,
                false AS valet_excluded_flag,
                false AS standalone_parking_excluded_flag,
                false AS driver_or_passenger_required_flag,
                'NOT_APPLICABLE' AS free_period_application,
                'APPLY_NATIONAL_STATUTORY_DISCOUNT' AS succeeding_hours_discount_rule,
                'VAT_EXCLUSIVE' AS discount_base_scope,
                'STATUTORY_FIRST' AS stacking_policy,
                COALESCE(p.local_ordinance_reference, p.national_law_reference, p.policy_code) AS legal_basis_priority,
                p.requires_operator_validation,
                p.requires_evidence_capture,
                p.effective_from,
                p.effective_to,
                p.policy_version AS source_reference,
                jsonb_build_object(
                    'statutoryDiscountPolicyId', COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id),
                    'policyCode', p.policy_code,
                    'policyName', p.policy_name,
                    'entitlementType', sdv.entitlement_type::text,
                    'policyResolutionBasis', sdv.policy_resolution_basis::text,
                    'policyLevel', p.policy_level::text,
                    'policyType', p.policy_type::text,
                    'legalBasisReference', COALESCE(p.local_ordinance_reference, p.national_law_reference),
                    'ordinanceReference', p.local_ordinance_reference,
                    'nationalLawReference', p.national_law_reference,
                    'verificationStatus', p.policy_status::text,
                    'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
                    'freeDurationMinutes', NULL,
                    'succeedingHoursDiscountRule', 'APPLY_NATIONAL_STATUTORY_DISCOUNT',
                    'discountBaseScope', 'VAT_EXCLUSIVE',
                    'stackingPolicy', 'STATUTORY_FIRST',
                    'requiresEvidence', p.requires_evidence_capture,
                    'resolvedAt', sdv.requested_at
                )::text AS resolved_policy_snapshot_json,
                (
                    SELECT der.discount_evidence_reference_id
                    FROM discounts.discount_evidence_references AS der
                    WHERE der.statutory_discount_validation_id = sdv.statutory_discount_validation_id
                      AND der.purged_at IS NULL
                    ORDER BY der.created_at DESC, der.discount_evidence_reference_id DESC
                    LIMIT 1
                ) AS evidence_reference_id
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            LEFT JOIN discounts.discount_policy_references AS p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            WHERE sdv.parking_session_id = @parking_session_id
              AND sdv.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND sdv.validation_status IN (
                    'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                    'PENDING_OPERATOR_REVIEW'::discounts.statutory_discount_validations_status_enum
              )
              AND sdv.validated_at IS NULL
            ORDER BY sdv.requested_at DESC, sdv.statutory_discount_validation_id DESC
            LIMIT 1;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        npgsqlCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
            reader.GetGuid(0),
            reader.GetString(1),
            Persisted: true,
            ReusedExistingDraft: true,
            EvidenceRequired: reader.GetBoolean(2),
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: reader.IsDBNull(38) ? null : reader.GetGuid(38),
            Policy: ReadPolicy(reader, startOrdinal: 3));
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> InsertDraftAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        var policyAuthorityIds = await ResolvePersistencePolicyAuthorityIdsAsync(
            connection,
            transaction,
            command,
            cancellationToken);

        const string sql = """
            INSERT INTO discounts.statutory_discount_validations (
                parking_session_id,
                entitlement_type,
                id_document_type,
                issuing_authority,
                id_expiry_date,
                masked_id_reference,
                policy_resolution_basis,
                validation_channel,
                validation_status,
                evidence_required,
                evidence_captured,
                decision_reason_code,
                requester_attestation,
                attestation_notes,
                evaluated_policy_reference_id,
                applied_policy_reference_id,
                statutory_discount_policy_version_id,
                requested_at,
                requested_by_user_id,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                @parking_session_id,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @id_document_type,
                @issuing_authority,
                @id_expiry_date,
                @masked_id_reference,
                @policy_resolution_basis::discounts.policy_resolution_basis_enum,
                'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum,
                'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                @evidence_required,
                @evidence_captured,
                @decision_reason_code,
                @requester_attestation,
                @attestation_notes,
                @policy_reference_id,
                NULL,
                @policy_version_id,
                now(),
                @requested_by_user_id,
                @correlation_id,
                @created_by_user_id
            )
            RETURNING
                statutory_discount_validation_id,
                validation_status::text,
                jsonb_set(
                    @resolved_policy_snapshot_json,
                    '{resolvedAt}',
                    to_jsonb(requested_at),
                    true)::text AS resolved_policy_snapshot_json;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.ParkingSessionId;
        npgsqlCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = command.EntitlementType;
        npgsqlCommand.Parameters.Add("id_document_type", NpgsqlDbType.Varchar).Value = command.IdDocumentType;
        npgsqlCommand.Parameters.Add("issuing_authority", NpgsqlDbType.Varchar).Value = command.IssuingAuthority;
        npgsqlCommand.Parameters.Add("id_expiry_date", NpgsqlDbType.Date).Value = DbValue(command.ExpiryDate);
        npgsqlCommand.Parameters.Add("masked_id_reference", NpgsqlDbType.Varchar).Value = command.MaskedIdReference;
        npgsqlCommand.Parameters.Add("policy_resolution_basis", NpgsqlDbType.Text).Value = command.Policy.PolicyResolutionBasis;
        npgsqlCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = command.EvidenceRequired;
        npgsqlCommand.Parameters.Add("evidence_captured", NpgsqlDbType.Boolean).Value = HasProtectedPhoto(command);
        npgsqlCommand.Parameters.Add("decision_reason_code", NpgsqlDbType.Varchar).Value = DbValue(command.ReasonCode);
        npgsqlCommand.Parameters.Add("requester_attestation", NpgsqlDbType.Boolean).Value = command.OperatorAttestation;
        npgsqlCommand.Parameters.Add("attestation_notes", NpgsqlDbType.Varchar).Value = DbValue(command.AttestationNotes);
        npgsqlCommand.Parameters.Add("policy_reference_id", NpgsqlDbType.Uuid).Value =
            DbValue(policyAuthorityIds.LegacyPolicyReferenceId);
        npgsqlCommand.Parameters.Add("policy_version_id", NpgsqlDbType.Uuid).Value =
            DbValue(policyAuthorityIds.PolicyVersionId);
        npgsqlCommand.Parameters.Add("resolved_policy_snapshot_json", NpgsqlDbType.Jsonb).Value =
            command.Policy.PolicySnapshot.GetRawText();
        npgsqlCommand.Parameters.Add("requested_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount draft insert did not return a draft ID.");
        }

        return new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
            reader.GetGuid(0),
            reader.GetString(1),
            Persisted: true,
            ReusedExistingDraft: false,
            EvidenceRequired: command.EvidenceRequired,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            Policy: command.Policy with
            {
                PolicySnapshot = JsonDocument.Parse(reader.GetString(2)).RootElement.Clone()
            });
    }

    private static async Task<(Guid? LegacyPolicyReferenceId, Guid? PolicyVersionId)> ResolvePersistencePolicyAuthorityIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (
                    SELECT p.discount_policy_reference_id
                    FROM discounts.discount_policy_references AS p
                    WHERE p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                      AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
                      AND (
                            p.discount_policy_reference_id = @resolved_policy_id
                         OR p.policy_code = @policy_code
                      )
                      AND (
                            p.site_id = @site_id
                         OR p.site_group_id = @site_group_id
                         OR (p.site_id IS NULL AND p.site_group_id IS NULL)
                      )
                    ORDER BY
                        CASE
                            WHEN p.discount_policy_reference_id = @resolved_policy_id THEN 0
                            WHEN p.site_id = @site_id THEN 1
                            WHEN p.site_group_id = @site_group_id THEN 2
                            ELSE 3
                        END,
                        p.effective_from DESC,
                        p.policy_code
                    LIMIT 1
                ) AS legacy_policy_reference_id,
                (
                    SELECT policy_version.statutory_discount_policy_version_id
                    FROM discounts.statutory_discount_policy_versions AS policy_version
                    JOIN sites.site_jurisdiction_assignments AS assignment
                      ON assignment.jurisdiction_id = policy_version.jurisdiction_id
                     AND assignment.site_id = @site_id
                     AND assignment.assignment_status = 'ACTIVE'
                     AND assignment.effective_from <= now()
                     AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
                    WHERE policy_version.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                      AND policy_version.source_verification_status IN (
                            'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum,
                            'VERIFIED_ACTIVE_OPERATIONAL'::discounts.policy_verification_status_enum,
                            'ACTIVE_APPROVED'::discounts.policy_verification_status_enum
                      )
                      AND policy_version.transaction_publication_status = 'ACTIVE_FOR_TRANSACTION_USE'
                      AND policy_version.parking_service_applicability = 'COVERED'
                      AND policy_version.policy_effect_support_status = 'SUPPORTED_BY_CURRENT_CALCULATION'
                      AND (
                            (policy_version.policy_scope_type = 'JURISDICTION' AND policy_version.site_group_id IS NULL AND policy_version.site_id IS NULL)
                         OR (policy_version.policy_scope_type = 'SITE_GROUP' AND policy_version.site_group_id = @site_group_id AND policy_version.site_id IS NULL)
                         OR (policy_version.policy_scope_type = 'SITE' AND policy_version.site_id = @site_id)
                      )
                      AND (policy_version.transaction_use_effective_from IS NULL OR policy_version.transaction_use_effective_from <= now())
                      AND (policy_version.transaction_use_effective_to IS NULL OR policy_version.transaction_use_effective_to > now())
                      AND policy_version.withdrawn_at IS NULL
                      AND policy_version.retired_at IS NULL
                      AND policy_version.superseded_by_policy_version_id IS NULL
                    ORDER BY
                        CASE policy_version.policy_scope_type
                            WHEN 'SITE' THEN 3
                            WHEN 'SITE_GROUP' THEN 2
                            ELSE 1
                        END DESC,
                        policy_version.precedence_rank,
                        policy_version.transaction_use_effective_from DESC NULLS LAST,
                        policy_version.policy_code
                    LIMIT 1
                ) AS policy_version_id;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = command.EntitlementType;
        npgsqlCommand.Parameters.Add("resolved_policy_id", NpgsqlDbType.Uuid).Value = command.Policy.StatutoryDiscountPolicyId;
        npgsqlCommand.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = command.Policy.PolicyCode;
        npgsqlCommand.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = command.Policy.SiteId;
        npgsqlCommand.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = command.Policy.SiteGroupId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new OperatorConsoleStatutoryDiscountDraftPolicyReferenceMissingException(
                command.Policy.PolicyCode,
                command.EntitlementType);
        }

        Guid? legacyPolicyReferenceId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
        Guid? policyVersionId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        if (!legacyPolicyReferenceId.HasValue && !policyVersionId.HasValue)
        {
            throw new OperatorConsoleStatutoryDiscountDraftPolicyReferenceMissingException(
                command.Policy.PolicyCode,
                command.EntitlementType);
        }

        return (legacyPolicyReferenceId, policyVersionId);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> EnsureEvidenceMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        OperatorConsoleStatutoryDiscountDraftPersistenceResult result,
        CancellationToken cancellationToken)
    {
        if (!command.EvidenceRequired)
        {
            return result;
        }

        await MarkDraftEvidenceRequiredAsync(connection, transaction, command, result.DraftId, cancellationToken);
        await LockEvidenceReferenceTableAsync(connection, transaction, cancellationToken);

        var hasProtectedPhoto = HasProtectedPhoto(command);
        var evidenceType = EvidenceTypeForEntitlement(command.EntitlementType);
        var existingEvidenceReferenceId = await FindEvidenceReferenceAsync(
            connection,
            transaction,
            result.DraftId,
            evidenceType,
            cancellationToken);

        if (existingEvidenceReferenceId.HasValue)
        {
            if (hasProtectedPhoto)
            {
                await UpdateEvidenceReferenceAsync(
                    connection,
                    transaction,
                    command,
                    result.DraftId,
                    existingEvidenceReferenceId.Value,
                    cancellationToken);
                await MarkDraftEvidenceCapturedAsync(connection, transaction, command, result.DraftId, cancellationToken);
            }

            return result with
            {
                EvidenceRequired = true,
                EvidenceReferenceCreated = false,
                EvidenceReferenceId = existingEvidenceReferenceId
            };
        }

        var evidenceReferenceId = await InsertEvidenceReferenceAsync(
            connection,
            transaction,
            command,
            result.DraftId,
            evidenceType,
            cancellationToken);

        if (hasProtectedPhoto)
        {
            await MarkDraftEvidenceCapturedAsync(connection, transaction, command, result.DraftId, cancellationToken);
        }

        return result with
        {
            EvidenceRequired = true,
            EvidenceReferenceCreated = true,
            EvidenceReferenceId = evidenceReferenceId
        };
    }

    private static async Task MarkDraftEvidenceRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
            SET evidence_required = true,
                updated_at = now(),
                updated_by_user_id = @updated_by_user_id
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_required = false;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockEvidenceReferenceTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "LOCK TABLE discounts.discount_evidence_references IN SHARE ROW EXCLUSIVE MODE;";
        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> FindEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid draftId,
        string evidenceType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT discount_evidence_reference_id
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND purged_at IS NULL
            ORDER BY created_at DESC, discount_evidence_reference_id DESC
            LIMIT 1;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;

        var value = await npgsqlCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid evidenceReferenceId ? evidenceReferenceId : null;
    }

    private static async Task<Guid> InsertEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        string evidenceType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.discount_evidence_references (
                statutory_discount_validation_id,
                evidence_type,
                evidence_storage_type,
                evidence_storage_ref,
                evidence_hash,
                evidence_capture_status,
                access_classification,
                redaction_status,
                retention_policy_code,
                retention_expires_at,
                captured_at,
                captured_by_user_id,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                @statutory_discount_validation_id,
                @evidence_type::discounts.discount_evidence_type_enum,
                @evidence_storage_type::discounts.evidence_storage_type_enum,
                @evidence_storage_ref,
                @evidence_hash,
                @evidence_capture_status::discounts.evidence_capture_status_enum,
                @access_classification::discounts.evidence_access_classification_enum,
                @redaction_status::discounts.evidence_redaction_status_enum,
                @retention_policy_code,
                NULL,
                now(),
                @captured_by_user_id,
                @correlation_id,
                @created_by_user_id
            )
            RETURNING discount_evidence_reference_id;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;
        npgsqlCommand.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = EvidenceStorageType(command);
        npgsqlCommand.Parameters.Add("evidence_storage_ref", NpgsqlDbType.Varchar).Value = DbValue(command.EvidenceStorageReference);
        npgsqlCommand.Parameters.Add("evidence_hash", NpgsqlDbType.Char).Value = DbValue(command.EvidenceHash);
        npgsqlCommand.Parameters.Add("evidence_capture_status", NpgsqlDbType.Text).Value = EvidenceCaptureStatus(command);
        npgsqlCommand.Parameters.Add("access_classification", NpgsqlDbType.Text).Value = EvidenceAccessClassification;
        npgsqlCommand.Parameters.Add("redaction_status", NpgsqlDbType.Text).Value = EvidenceRedactionStatus;
        npgsqlCommand.Parameters.Add("retention_policy_code", NpgsqlDbType.Varchar).Value = EvidenceRetentionPolicyCode(command);
        npgsqlCommand.Parameters.Add("captured_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;

        var value = await npgsqlCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid evidenceReferenceId
            ? evidenceReferenceId
            : throw new InvalidOperationException("Operator Console statutory discount evidence metadata insert did not return an evidence reference ID.");
    }

    private static async Task UpdateEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        Guid evidenceReferenceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.discount_evidence_references
               SET evidence_storage_type = @evidence_storage_type::discounts.evidence_storage_type_enum,
                   evidence_storage_ref = @evidence_storage_ref,
                   evidence_hash = @evidence_hash,
                   evidence_capture_status = @evidence_capture_status::discounts.evidence_capture_status_enum,
                   access_classification = @access_classification::discounts.evidence_access_classification_enum,
                   redaction_status = @redaction_status::discounts.evidence_redaction_status_enum,
                   retention_policy_code = @retention_policy_code,
                   captured_at = now(),
                   captured_by_user_id = @captured_by_user_id,
                   correlation_id = @correlation_id,
                   updated_at = now(),
                   updated_by_user_id = @updated_by_user_id,
                   row_version = row_version + 1
             WHERE discount_evidence_reference_id = @evidence_reference_id
               AND statutory_discount_validation_id = @statutory_discount_validation_id
               AND purged_at IS NULL;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("evidence_reference_id", NpgsqlDbType.Uuid).Value = evidenceReferenceId;
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = ProtectedEvidenceStorageType;
        npgsqlCommand.Parameters.Add("evidence_storage_ref", NpgsqlDbType.Varchar).Value = command.EvidenceStorageReference!;
        npgsqlCommand.Parameters.Add("evidence_hash", NpgsqlDbType.Char).Value = command.EvidenceHash!;
        npgsqlCommand.Parameters.Add("evidence_capture_status", NpgsqlDbType.Text).Value = ProtectedEvidenceCaptureStatus;
        npgsqlCommand.Parameters.Add("access_classification", NpgsqlDbType.Text).Value = EvidenceAccessClassification;
        npgsqlCommand.Parameters.Add("redaction_status", NpgsqlDbType.Text).Value = EvidenceRedactionStatus;
        npgsqlCommand.Parameters.Add("retention_policy_code", NpgsqlDbType.Varchar).Value = ProtectedEvidenceRetentionPolicyCode;
        npgsqlCommand.Parameters.Add("captured_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;

        if (await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Operator Console statutory ID photo evidence reference could not be updated.");
        }
    }

    private static async Task MarkDraftEvidenceCapturedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
               SET evidence_captured = true,
                   correlation_id = @correlation_id,
                   updated_at = now(),
                   updated_by_user_id = @updated_by_user_id,
                   row_version = row_version + 1
             WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        if (await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Operator Console statutory ID photo capture state could not be persisted.");
        }
    }

    private static string EvidenceTypeForEntitlement(string entitlementType) =>
        string.Equals(entitlementType, "PWD", StringComparison.Ordinal)
            ? "PWD_ID"
            : "SENIOR_CITIZEN_ID";

    private static bool HasProtectedPhoto(OperatorConsoleStatutoryDiscountDraftPersistenceCommand command) =>
        !string.IsNullOrWhiteSpace(command.EvidenceStorageReference) &&
        !string.IsNullOrWhiteSpace(command.EvidenceHash) &&
        command.EvidenceHash.Length == 64 &&
        !string.IsNullOrWhiteSpace(command.EvidenceContentType) &&
        command.EvidenceSizeBytes is > 0;

    private static string EvidenceStorageType(OperatorConsoleStatutoryDiscountDraftPersistenceCommand command) =>
        HasProtectedPhoto(command) ? ProtectedEvidenceStorageType : PlaceholderEvidenceStorageType;

    private static string EvidenceCaptureStatus(OperatorConsoleStatutoryDiscountDraftPersistenceCommand command) =>
        HasProtectedPhoto(command) ? ProtectedEvidenceCaptureStatus : PlaceholderEvidenceCaptureStatus;

    private static string EvidenceRetentionPolicyCode(OperatorConsoleStatutoryDiscountDraftPersistenceCommand command) =>
        HasProtectedPhoto(command) ? ProtectedEvidenceRetentionPolicyCode : PlaceholderEvidenceRetentionPolicyCode;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateOnly? value) => value.HasValue ? value.Value : DBNull.Value;

    private static OperatorConsoleResolvedStatutoryDiscountPolicy? ReadPolicy(
        NpgsqlDataReader reader,
        int startOrdinal)
    {
        if (reader.IsDBNull(startOrdinal))
        {
            return null;
        }

        var rawSnapshot = reader.IsDBNull(startOrdinal + 34)
            ? "{}"
            : reader.GetString(startOrdinal + 34);

        return new OperatorConsoleResolvedStatutoryDiscountPolicy(
            reader.GetGuid(startOrdinal),
            reader.IsDBNull(startOrdinal + 1) ? null : reader.GetGuid(startOrdinal + 1),
            reader.GetGuid(startOrdinal + 3),
            reader.GetGuid(startOrdinal + 4),
            reader.GetString(startOrdinal + 7),
            reader.GetString(startOrdinal + 5),
            reader.GetString(startOrdinal + 6),
            reader.GetString(startOrdinal + 8),
            reader.GetString(startOrdinal + 9),
            reader.GetString(startOrdinal + 10),
            GetNullableString(reader, startOrdinal + 11),
            GetNullableString(reader, startOrdinal + 12),
            GetNullableString(reader, startOrdinal + 13),
            reader.GetString(startOrdinal + 14),
            reader.GetString(startOrdinal + 15),
            reader.GetString(startOrdinal + 16),
            reader.IsDBNull(startOrdinal + 17) ? null : reader.GetInt32(startOrdinal + 17),
            reader.GetBoolean(startOrdinal + 18),
            reader.GetBoolean(startOrdinal + 19),
            reader.GetBoolean(startOrdinal + 20),
            reader.GetBoolean(startOrdinal + 21),
            reader.GetBoolean(startOrdinal + 22),
            reader.GetBoolean(startOrdinal + 23),
            reader.GetString(startOrdinal + 24),
            reader.GetString(startOrdinal + 25),
            reader.GetString(startOrdinal + 26),
            reader.GetString(startOrdinal + 27),
            reader.GetString(startOrdinal + 28),
            reader.GetBoolean(startOrdinal + 29),
            reader.GetBoolean(startOrdinal + 30),
            DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(startOrdinal + 31).Date),
            reader.IsDBNull(startOrdinal + 32)
                ? null
                : DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(startOrdinal + 32).Date),
            GetNullableString(reader, startOrdinal + 33),
            JsonDocument.Parse(rawSnapshot).RootElement.Clone());
    }

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
