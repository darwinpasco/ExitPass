using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository :
    IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository,
    IFiscalExceptionSemanticHashGuardedBackfillMutationRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @semantic_hash_recalculation_preview_audit_id,
                @mutation_preparation_audit_id,
                @controlled_backfill_approval_status,
                @old_semantic_hash_source_version,
                @required_semantic_hash_source_version,
                @old_semantic_hash_value,
                @new_semantic_hash_value,
                @new_semantic_hash_algorithm,
                @new_semantic_hash_source_version,
                @new_semantic_hash_source_fact_count,
                @safe_source_summary,
                @mutation_preparation_status,
                @mutation_block_reason_code,
                @mutation_mode,
                @mutation_enabled,
                @fiscal_issuance_reference_mutated,
                @attempted_at,
                @safe_summary,
                @correlation_id,
                @actor_service_identity_id,
                @approval_reference,
                @dual_control_reference
            )
            RETURNING
                semantic_hash_backfill_mutation_audit_id,
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference,
                created_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        AddParameters(command, attempt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Semantic hash controlled backfill mutation audit insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var reference = await ReadReferenceForUpdateAsync(connection, transaction, command, cancellationToken);
        if (reference is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Failed,
                "fiscal_issuance_reference_not_found",
                "semantic_hash_guarded_backfill_failed_reference_not_found",
                command,
                mutationAuditId: null,
                oldSourceVersion: null,
                oldHashValue: null,
                mutated: false,
                mutationTimestamp: null);
        }

        if (!string.Equals(
                reference.SourceVersion,
                command.ExpectedOldSourceVersion,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                reference.SourceVersion,
                FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            var audit = await RecordTransactionalAuditAsync(
                connection,
                transaction,
                command,
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
                "semantic_hash_source_version_changed_before_backfill",
                "semantic_hash_guarded_backfill_stale_source_version_changed_not_mutated",
                reference.SourceVersion,
                reference.HashValue,
                mutated: false,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToMutationResult(audit, mutated: false);
        }

        var preview = await ReadPreviewAsync(connection, transaction, command, cancellationToken);
        if (preview is null || !PreviewMatches(preview, command))
        {
            var audit = await RecordTransactionalAuditAsync(
                connection,
                transaction,
                command,
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
                "semantic_hash_recalculation_preview_audit_basis_mismatch",
                "semantic_hash_guarded_backfill_stale_preview_audit_basis_mismatch",
                reference.SourceVersion,
                reference.HashValue,
                mutated: false,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToMutationResult(audit, mutated: false);
        }

        var preparation = await ReadMutationPreparationAsync(connection, transaction, command, cancellationToken);
        if (preparation is null || !PreparationMatches(preparation, command))
        {
            var audit = await RecordTransactionalAuditAsync(
                connection,
                transaction,
                command,
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
                "semantic_hash_backfill_mutation_preparation_audit_basis_mismatch",
                "semantic_hash_guarded_backfill_stale_mutation_preparation_audit_basis_mismatch",
                reference.SourceVersion,
                reference.HashValue,
                mutated: false,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToMutationResult(audit, mutated: false);
        }

        var mutationTimestamp = command.AttemptedAt;
        await UpdateSemanticHashMetadataAsync(connection, transaction, command, mutationTimestamp, cancellationToken);
        var mutationAudit = await RecordTransactionalAuditAsync(
            connection,
            transaction,
            command,
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated,
            blockReasonCode: null,
            "semantic_hash_guarded_backfill_mutated_single_record_semantic_metadata_only",
            reference.SourceVersion,
            reference.HashValue,
            mutated: true,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ToMutationResult(mutationAudit, mutated: true);
    }

    private static async Task<ReferenceSnapshot?> ReadReferenceForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT semantic_request_hash_source_version, semantic_request_hash_value
            FROM core.fiscal_issuance_references
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
              AND is_active = true
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("fiscal_issuance_reference_id", command.FiscalIssuanceReferenceId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReferenceSnapshot(
                GetNullableString(reader, "semantic_request_hash_source_version"),
                GetNullableString(reader, "semantic_request_hash_value"))
            : null;
    }

    private static async Task<PreviewSnapshot?> ReadPreviewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                recalculated_hash_value,
                recalculated_hash_algorithm,
                recalculated_hash_source_version,
                recalculated_source_fact_count,
                safe_source_summary,
                recalculation_preview_status,
                complete_original_request_facts_available,
                mutation_status
            FROM core.fiscal_issuance_semantic_hash_recalculation_previews
            WHERE semantic_hash_recalculation_preview_audit_id = @semantic_hash_recalculation_preview_audit_id
              AND fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            FOR SHARE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue(
            "semantic_hash_recalculation_preview_audit_id",
            command.RecalculationPreviewAuditId);
        dbCommand.Parameters.AddWithValue("fiscal_issuance_reference_id", command.FiscalIssuanceReferenceId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PreviewSnapshot(
                GetNullableString(reader, "recalculated_hash_value"),
                GetNullableString(reader, "recalculated_hash_algorithm"),
                GetNullableString(reader, "recalculated_hash_source_version"),
                GetNullableInt32(reader, "recalculated_source_fact_count"),
                GetNullableString(reader, "safe_source_summary"),
                reader.GetString(reader.GetOrdinal("recalculation_preview_status")),
                reader.GetBoolean(reader.GetOrdinal("complete_original_request_facts_available")),
                reader.GetString(reader.GetOrdinal("mutation_status")))
            : null;
    }

    private static async Task<PreparationSnapshot?> ReadMutationPreparationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                semantic_hash_recalculation_preview_audit_id,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference
            FROM core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            WHERE semantic_hash_backfill_mutation_audit_id = @mutation_preparation_audit_id
              AND fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            FOR SHARE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("mutation_preparation_audit_id", command.MutationPreparationAuditId);
        dbCommand.Parameters.AddWithValue("fiscal_issuance_reference_id", command.FiscalIssuanceReferenceId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PreparationSnapshot(
                GetNullableGuid(reader, "semantic_hash_recalculation_preview_audit_id"),
                GetNullableString(reader, "new_semantic_hash_value"),
                GetNullableString(reader, "new_semantic_hash_algorithm"),
                GetNullableString(reader, "new_semantic_hash_source_version"),
                GetNullableInt32(reader, "new_semantic_hash_source_fact_count"),
                GetNullableString(reader, "safe_source_summary"),
                reader.GetString(reader.GetOrdinal("mutation_preparation_status")),
                reader.GetString(reader.GetOrdinal("mutation_mode")),
                reader.GetBoolean(reader.GetOrdinal("mutation_enabled")),
                reader.GetBoolean(reader.GetOrdinal("fiscal_issuance_reference_mutated")),
                GetNullableGuid(reader, "actor_service_identity_id"),
                GetNullableString(reader, "approval_reference"),
                GetNullableString(reader, "dual_control_reference"))
            : null;
    }

    private static bool PreviewMatches(
        PreviewSnapshot preview,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command) =>
        preview.PreviewStatus == "PREVIEW_CALCULATED" &&
        preview.CompleteOriginalRequestFactsAvailable &&
        preview.MutationStatus == "NOT_MUTATED" &&
        preview.RecalculatedHashValue == command.NewHashValue &&
        string.Equals(preview.RecalculatedHashAlgorithm, command.NewHashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(preview.RecalculatedHashSourceVersion, command.NewHashSourceVersion, StringComparison.OrdinalIgnoreCase) &&
        preview.RecalculatedSourceFactCount == command.NewHashSourceFactCount &&
        preview.RecalculatedSafeSourceSummary == command.SafeSourceSummary;

    private static bool PreparationMatches(
        PreparationSnapshot preparation,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command) =>
        preparation.RecalculationPreviewAuditId == command.RecalculationPreviewAuditId &&
        preparation.MutationStatus == "PREPARED_FOR_CONTROLLED_MUTATION" &&
        preparation.MutationMode == "SINGLE_RECORD_ONLY" &&
        preparation.MutationEnabled &&
        !preparation.FiscalIssuanceReferenceMutated &&
        preparation.NewHashValue == command.NewHashValue &&
        string.Equals(preparation.NewHashAlgorithm, command.NewHashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(preparation.NewHashSourceVersion, command.NewHashSourceVersion, StringComparison.OrdinalIgnoreCase) &&
        preparation.NewHashSourceFactCount == command.NewHashSourceFactCount &&
        preparation.SafeSourceSummary == command.SafeSourceSummary &&
        preparation.ActorServiceIdentityId == command.ActorServiceIdentityId &&
        preparation.ApprovalReference == command.ApprovalReference &&
        preparation.DualControlReference == command.DualControlReference;

    private static async Task UpdateSemanticHashMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        DateTimeOffset mutationTimestamp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.fiscal_issuance_references
            SET
                semantic_request_hash_status = 'AVAILABLE',
                semantic_request_hash_value = @semantic_request_hash_value,
                semantic_request_hash_algorithm = @semantic_request_hash_algorithm,
                semantic_request_hash_source_version = @semantic_request_hash_source_version,
                semantic_request_hash_source_fact_count = @semantic_request_hash_source_fact_count,
                semantic_request_hash_safe_summary = @semantic_request_hash_safe_summary,
                semantic_request_hash_recorded_at = @semantic_request_hash_recorded_at
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
              AND is_active = true
              AND semantic_request_hash_source_version = @expected_old_source_version;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("fiscal_issuance_reference_id", command.FiscalIssuanceReferenceId);
        dbCommand.Parameters.AddWithValue("expected_old_source_version", command.ExpectedOldSourceVersion);
        dbCommand.Parameters.AddWithValue("semantic_request_hash_value", command.NewHashValue);
        dbCommand.Parameters.AddWithValue("semantic_request_hash_algorithm", command.NewHashAlgorithm);
        dbCommand.Parameters.AddWithValue("semantic_request_hash_source_version", command.NewHashSourceVersion);
        dbCommand.Parameters.AddWithValue("semantic_request_hash_source_fact_count", command.NewHashSourceFactCount);
        dbCommand.Parameters.AddWithValue("semantic_request_hash_safe_summary", Truncate(command.SafeSourceSummary, 240));
        dbCommand.Parameters.AddWithValue("semantic_request_hash_recorded_at", mutationTimestamp);

        var affected = await dbCommand.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("semantic_hash_guarded_backfill_update_failed_closed");
        }
    }

    private static async Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord>
        RecordTransactionalAuditAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status,
            string? blockReasonCode,
            string safeSummary,
            string? oldSourceVersion,
            string? oldHashValue,
            bool mutated,
            CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @semantic_hash_recalculation_preview_audit_id,
                @mutation_preparation_audit_id,
                @controlled_backfill_approval_status,
                @old_semantic_hash_source_version,
                @required_semantic_hash_source_version,
                @old_semantic_hash_value,
                @new_semantic_hash_value,
                @new_semantic_hash_algorithm,
                @new_semantic_hash_source_version,
                @new_semantic_hash_source_fact_count,
                @safe_source_summary,
                @mutation_preparation_status,
                @mutation_block_reason_code,
                @mutation_mode,
                @mutation_enabled,
                @fiscal_issuance_reference_mutated,
                @attempted_at,
                @safe_summary,
                @correlation_id,
                @actor_service_identity_id,
                @approval_reference,
                @dual_control_reference
            )
            RETURNING
                semantic_hash_backfill_mutation_audit_id,
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference,
                created_at;
            """;

        var write = new FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite(
            FiscalIssuanceReferenceId: command.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: command.RecalculationPreviewAuditId,
            MutationPreparationAuditId: command.MutationPreparationAuditId,
            ApprovalBasisStatus: command.ApprovalBasisStatus,
            OldSourceVersion: oldSourceVersion,
            RequiredSourceVersion: command.RequiredSourceVersion,
            OldHashValue: oldHashValue,
            NewHashValue: command.NewHashValue,
            NewHashAlgorithm: command.NewHashAlgorithm,
            NewHashSourceVersion: command.NewHashSourceVersion,
            NewHashSourceFactCount: command.NewHashSourceFactCount,
            SafeSourceSummary: command.SafeSourceSummary,
            MutationStatus: status,
            BlockReasonCode: blockReasonCode,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            MutationEnabled: true,
            FiscalIssuanceReferenceMutated: mutated,
            AttemptedAt: command.AttemptedAt,
            SafeSummary: safeSummary,
            CorrelationId: command.CorrelationId,
            ActorServiceIdentityId: command.ActorServiceIdentityId,
            ApprovalReference: command.ApprovalReference,
            DualControlReference: command.DualControlReference);

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(dbCommand, write);
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Semantic hash guarded backfill mutation audit insert returned no rows.");
        }

        return MapRecord(reader);
    }

    private static FiscalExceptionSemanticHashGuardedBackfillMutationResult ToMutationResult(
        FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord audit,
        bool mutated) =>
        FiscalExceptionSemanticHashGuardedBackfillMutationService.Result(
            audit.MutationStatus,
            audit.BlockReasonCode,
            audit.SafeSummary,
            audit.MutationAuditId,
            audit.OldSourceVersion,
            audit.NewHashSourceVersion,
            audit.OldHashValue,
            audit.NewHashValue,
            audit.AttemptedAt,
            mutated);

    private static FiscalExceptionSemanticHashGuardedBackfillMutationResult Result(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status,
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        Guid? mutationAuditId,
        string? oldSourceVersion,
        string? oldHashValue,
        bool mutated,
        DateTimeOffset? mutationTimestamp) =>
        FiscalExceptionSemanticHashGuardedBackfillMutationService.Result(
            status,
            blockReasonCode,
            safeSummary,
            mutationAuditId,
            oldSourceVersion,
            command.NewHashSourceVersion,
            oldHashValue,
            command.NewHashValue,
            mutationTimestamp,
            mutated);

    public async Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken)
    {
        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fiscal issuance reference id is required.",
                nameof(fiscalIssuanceReferenceId));
        }

        const string sql = """
            SELECT
                semantic_hash_backfill_mutation_audit_id,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                old_semantic_hash_source_version,
                new_semantic_hash_source_version,
                new_semantic_hash_value,
                attempted_at,
                safe_summary,
                COUNT(*) OVER ()::integer AS attempt_count
            FROM core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            ORDER BY attempted_at DESC, created_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", fiscalIssuanceReferenceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary(
            LastMutationAuditId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_mutation_audit_id")),
            LastMutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_preparation_status"))),
            LastAttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastBlockReasonCode: GetNullableString(reader, "mutation_block_reason_code"),
            MutationMode: ParseMutationMode(reader.GetString(reader.GetOrdinal("mutation_mode"))),
            MutationEnabled: reader.GetBoolean(reader.GetOrdinal("mutation_enabled")),
            FiscalIssuanceReferenceMutated: reader.GetBoolean(reader.GetOrdinal("fiscal_issuance_reference_mutated")),
            OldSourceVersion: GetNullableString(reader, "old_semantic_hash_source_version"),
            NewSourceVersion: GetNullableString(reader, "new_semantic_hash_source_version"),
            NewHashValue: GetNullableString(reader, "new_semantic_hash_value"),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")));
    }

    private static void AddParameters(
        NpgsqlCommand command,
        FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt)
    {
        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "semantic_hash_recalculation_preview_audit_id", attempt.RecalculationPreviewAuditId);
        AddNullable(command, "mutation_preparation_audit_id", attempt.MutationPreparationAuditId);
        command.Parameters.AddWithValue(
            "controlled_backfill_approval_status",
            ToStorageValue(attempt.ApprovalBasisStatus));
        AddNullable(command, "old_semantic_hash_source_version", TruncateOrNull(attempt.OldSourceVersion, 80));
        command.Parameters.AddWithValue(
            "required_semantic_hash_source_version",
            Truncate(attempt.RequiredSourceVersion, 80));
        AddNullable(command, "old_semantic_hash_value", TruncateOrNull(attempt.OldHashValue, 64));
        AddNullable(command, "new_semantic_hash_value", TruncateOrNull(attempt.NewHashValue, 64));
        AddNullable(command, "new_semantic_hash_algorithm", TruncateOrNull(attempt.NewHashAlgorithm, 32));
        AddNullable(command, "new_semantic_hash_source_version", TruncateOrNull(attempt.NewHashSourceVersion, 80));
        AddNullable(command, "new_semantic_hash_source_fact_count", attempt.NewHashSourceFactCount);
        AddNullable(command, "safe_source_summary", TruncateOrNull(attempt.SafeSourceSummary, 240));
        command.Parameters.AddWithValue("mutation_preparation_status", ToStorageValue(attempt.MutationStatus));
        AddNullable(command, "mutation_block_reason_code", TruncateOrNull(attempt.BlockReasonCode, 160));
        command.Parameters.AddWithValue("mutation_mode", ToStorageValue(attempt.MutationMode));
        command.Parameters.AddWithValue("mutation_enabled", attempt.MutationEnabled);
        command.Parameters.AddWithValue(
            "fiscal_issuance_reference_mutated",
            attempt.FiscalIssuanceReferenceMutated);
        command.Parameters.AddWithValue("attempted_at", attempt.AttemptedAt);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        AddNullable(command, "actor_service_identity_id", attempt.ActorServiceIdentityId);
        AddNullable(command, "approval_reference", TruncateOrNull(attempt.ApprovalReference, 160));
        AddNullable(command, "dual_control_reference", TruncateOrNull(attempt.DualControlReference, 160));
    }

    private static void Validate(FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.RequiredSourceVersion))
        {
            throw new ArgumentException("Required semantic hash source version is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Semantic hash backfill mutation safe summary is required.", nameof(attempt));
        }

        if (attempt.FiscalIssuanceReferenceMutated &&
            attempt.MutationStatus != FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated)
        {
            throw new ArgumentException(
                "A mutated audit record must use mutated status.",
                nameof(attempt));
        }
    }

    private static void Validate(FiscalExceptionSemanticHashGuardedBackfillMutationCommand command)
    {
        if (command.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(command));
        }

        if (command.RecalculationPreviewAuditId == Guid.Empty)
        {
            throw new ArgumentException("Recalculation preview audit id is required.", nameof(command));
        }

        if (command.MutationPreparationAuditId == Guid.Empty)
        {
            throw new ArgumentException("Mutation preparation audit id is required.", nameof(command));
        }

        if (command.ActorServiceIdentityId == Guid.Empty)
        {
            throw new ArgumentException("Actor service identity id is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ApprovalReference))
        {
            throw new ArgumentException("Approval reference is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ExpectedOldSourceVersion) ||
            string.IsNullOrWhiteSpace(command.RequiredSourceVersion) ||
            string.IsNullOrWhiteSpace(command.NewHashValue) ||
            string.IsNullOrWhiteSpace(command.NewHashAlgorithm) ||
            string.IsNullOrWhiteSpace(command.NewHashSourceVersion) ||
            string.IsNullOrWhiteSpace(command.SafeSourceSummary) ||
            command.NewHashSourceFactCount < 1)
        {
            throw new ArgumentException("Semantic hash guarded backfill command metadata is incomplete.", nameof(command));
        }
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            MutationAuditId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_mutation_audit_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            RecalculationPreviewAuditId: GetNullableGuid(reader, "semantic_hash_recalculation_preview_audit_id"),
            MutationPreparationAuditId: GetNullableGuid(reader, "mutation_preparation_audit_id"),
            ApprovalBasisStatus: ParseApprovalStatus(
                reader.GetString(reader.GetOrdinal("controlled_backfill_approval_status"))),
            OldSourceVersion: GetNullableString(reader, "old_semantic_hash_source_version"),
            RequiredSourceVersion: reader.GetString(reader.GetOrdinal("required_semantic_hash_source_version")),
            OldHashValue: GetNullableString(reader, "old_semantic_hash_value"),
            NewHashValue: GetNullableString(reader, "new_semantic_hash_value"),
            NewHashAlgorithm: GetNullableString(reader, "new_semantic_hash_algorithm"),
            NewHashSourceVersion: GetNullableString(reader, "new_semantic_hash_source_version"),
            NewHashSourceFactCount: GetNullableInt32(reader, "new_semantic_hash_source_fact_count"),
            SafeSourceSummary: GetNullableString(reader, "safe_source_summary"),
            MutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_preparation_status"))),
            BlockReasonCode: GetNullableString(reader, "mutation_block_reason_code"),
            MutationMode: ParseMutationMode(reader.GetString(reader.GetOrdinal("mutation_mode"))),
            MutationEnabled: reader.GetBoolean(reader.GetOrdinal("mutation_enabled")),
            FiscalIssuanceReferenceMutated: reader.GetBoolean(
                reader.GetOrdinal("fiscal_issuance_reference_mutated")),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            ActorServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
            ApprovalReference: GetNullableString(reader, "approval_reference"),
            DualControlReference: GetNullableString(reader, "dual_control_reference"),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));

    private static string ToStorageValue(FiscalExceptionSemanticHashControlledBackfillApprovalStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent => "NOT_REQUIRED_CURRENT",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill => "READY_FOR_CONTROLLED_BACKFILL",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl => "PENDING_DUAL_CONTROL",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown approval status.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillApprovalStatus ParseApprovalStatus(string value) =>
        value switch
        {
            "NOT_REQUIRED_CURRENT" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent,
            "READY_FOR_CONTROLLED_BACKFILL" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            "PENDING_DUAL_CONTROL" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl,
            "UNAVAILABLE" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Unavailable,
            _ => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked
        };

    private static string ToStorageValue(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared => "NOT_PREPARED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled => "PREPARED_BUT_MUTATION_DISABLED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable => "UNAVAILABLE",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation => "PREPARED_FOR_CONTROLLED_MUTATION",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated => "MUTATED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Failed => "FAILED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale => "STALE",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled => "DISABLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown mutation status.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus ParseMutationStatus(
        string value) =>
        value switch
        {
            "PREPARED_BUT_MUTATION_DISABLED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled,
            "BLOCKED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable,
            "PREPARED_FOR_CONTROLLED_MUTATION" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation,
            "MUTATED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated,
            "FAILED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Failed,
            "STALE" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
            "DISABLED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled,
            _ => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared
        };

    private static string ToStorageValue(FiscalExceptionSemanticHashControlledBackfillMutationMode mode) =>
        mode switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly => "SINGLE_RECORD_ONLY",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mutation mode.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillMutationMode ParseMutationMode(string value) =>
        value switch
        {
            "SINGLE_RECORD_ONLY" => FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            _ => FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly
        };

    private static void AddNullable<T>(NpgsqlCommand command, string name, T? value)
    {
        if (value is not null)
        {
            command.Parameters.AddWithValue(name, value);
            return;
        }

        var parameter = TryGetNpgsqlDbType(typeof(T), out var dbType)
            ? command.Parameters.Add(name, dbType)
            : command.Parameters.AddWithValue(name, DBNull.Value);

        parameter.Value = DBNull.Value;
    }

    private static bool TryGetNpgsqlDbType(Type type, out NpgsqlDbType dbType)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(Guid))
        {
            dbType = NpgsqlDbType.Uuid;
            return true;
        }

        if (underlyingType == typeof(string))
        {
            dbType = NpgsqlDbType.Text;
            return true;
        }

        if (underlyingType == typeof(int))
        {
            dbType = NpgsqlDbType.Integer;
            return true;
        }

        dbType = default;
        return false;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Truncate(value.Trim(), maxLength);

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static int? GetNullableInt32(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private sealed record ReferenceSnapshot(string? SourceVersion, string? HashValue);

    private sealed record PreviewSnapshot(
        string? RecalculatedHashValue,
        string? RecalculatedHashAlgorithm,
        string? RecalculatedHashSourceVersion,
        int? RecalculatedSourceFactCount,
        string? RecalculatedSafeSourceSummary,
        string PreviewStatus,
        bool CompleteOriginalRequestFactsAvailable,
        string MutationStatus);

    private sealed record PreparationSnapshot(
        Guid? RecalculationPreviewAuditId,
        string? NewHashValue,
        string? NewHashAlgorithm,
        string? NewHashSourceVersion,
        int? NewHashSourceFactCount,
        string? SafeSourceSummary,
        string MutationStatus,
        string MutationMode,
        bool MutationEnabled,
        bool FiscalIssuanceReferenceMutated,
        Guid? ActorServiceIdentityId,
        string? ApprovalReference,
        string? DualControlReference);
}
