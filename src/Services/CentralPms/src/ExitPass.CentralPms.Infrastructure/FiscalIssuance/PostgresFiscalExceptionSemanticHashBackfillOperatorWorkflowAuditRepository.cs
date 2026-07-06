using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository :
    IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_semantic_hash_backfill_workflow_requests (
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                approval_reference,
                dual_control_reference,
                actor_service_identity_id,
                reason_code,
                safe_justification,
                request_mode,
                workflow_status,
                workflow_block_reason_code,
                mutation_invocation_posture,
                guarded_mutation_audit_id,
                guarded_mutation_status,
                execute_controlled_mutation_requested,
                mutation_invocation_enabled,
                dry_run_only,
                requested_at,
                correlation_id,
                safe_summary
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @semantic_hash_recalculation_preview_audit_id,
                @mutation_preparation_audit_id,
                @approval_reference,
                @dual_control_reference,
                @actor_service_identity_id,
                @reason_code,
                @safe_justification,
                @request_mode,
                @workflow_status,
                @workflow_block_reason_code,
                @mutation_invocation_posture,
                @guarded_mutation_audit_id,
                @guarded_mutation_status,
                @execute_controlled_mutation_requested,
                @mutation_invocation_enabled,
                @dry_run_only,
                @requested_at,
                @correlation_id,
                @safe_summary
            )
            RETURNING
                semantic_hash_backfill_workflow_request_id,
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                mutation_preparation_audit_id,
                approval_reference,
                dual_control_reference,
                actor_service_identity_id,
                reason_code,
                safe_justification,
                request_mode,
                workflow_status,
                workflow_block_reason_code,
                mutation_invocation_posture,
                guarded_mutation_audit_id,
                guarded_mutation_status,
                execute_controlled_mutation_requested,
                mutation_invocation_enabled,
                dry_run_only,
                requested_at,
                correlation_id,
                safe_summary,
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
                "Semantic hash backfill operator workflow audit insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary?> GetSummaryAsync(
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
                semantic_hash_backfill_workflow_request_id,
                workflow_status,
                workflow_block_reason_code,
                approval_reference,
                dual_control_reference,
                mutation_invocation_posture,
                guarded_mutation_audit_id,
                guarded_mutation_status,
                requested_at,
                safe_summary,
                COUNT(*) OVER ()::integer AS request_count
            FROM core.fiscal_issuance_semantic_hash_backfill_workflow_requests
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            ORDER BY requested_at DESC, created_at DESC
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

        return new FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary(
            LastWorkflowRequestId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_workflow_request_id")),
            LastWorkflowStatus: ParseWorkflowStatus(reader.GetString(reader.GetOrdinal("workflow_status"))),
            LastRequestedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            RequestCount: reader.GetInt32(reader.GetOrdinal("request_count")),
            LastBlockReasonCode: GetNullableString(reader, "workflow_block_reason_code"),
            ApprovalReference: GetNullableString(reader, "approval_reference"),
            DualControlPosture: string.IsNullOrWhiteSpace(GetNullableString(reader, "dual_control_reference"))
                ? FiscalExceptionSemanticHashControlledBackfillDualControlPosture.RequiredPending
                : FiscalExceptionSemanticHashControlledBackfillDualControlPosture.Satisfied,
            MutationInvocationPosture: ParseInvocationPosture(
                reader.GetString(reader.GetOrdinal("mutation_invocation_posture"))),
            GuardedMutationAuditId: GetNullableGuid(reader, "guarded_mutation_audit_id"),
            GuardedMutationStatus: ParseNullableMutationStatus(GetNullableString(reader, "guarded_mutation_status")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")));
    }

    private static void AddParameters(
        NpgsqlCommand command,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite attempt)
    {
        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "semantic_hash_recalculation_preview_audit_id", attempt.RecalculationPreviewAuditId);
        AddNullable(command, "mutation_preparation_audit_id", attempt.MutationPreparationAuditId);
        AddNullable(command, "approval_reference", TruncateOrNull(attempt.ApprovalReference, 160));
        AddNullable(command, "dual_control_reference", TruncateOrNull(attempt.DualControlReference, 160));
        AddNullable(command, "actor_service_identity_id", attempt.ActorServiceIdentityId);
        AddNullable(command, "reason_code", TruncateOrNull(attempt.ReasonCode, 80));
        AddNullable(command, "safe_justification", TruncateOrNull(attempt.SafeJustification, 240));
        command.Parameters.AddWithValue("request_mode", ToStorageValue(attempt.RequestMode));
        command.Parameters.AddWithValue("workflow_status", ToStorageValue(attempt.WorkflowStatus));
        AddNullable(command, "workflow_block_reason_code", TruncateOrNull(attempt.BlockReasonCode, 160));
        command.Parameters.AddWithValue(
            "mutation_invocation_posture",
            ToStorageValue(attempt.MutationInvocationPosture));
        AddNullable(command, "guarded_mutation_audit_id", attempt.GuardedMutationAuditId);
        AddNullable(command, "guarded_mutation_status", ToStorageValueOrNull(attempt.GuardedMutationStatus));
        command.Parameters.AddWithValue(
            "execute_controlled_mutation_requested",
            attempt.ExecuteControlledMutationRequested);
        command.Parameters.AddWithValue("mutation_invocation_enabled", attempt.MutationInvocationEnabled);
        command.Parameters.AddWithValue("dry_run_only", attempt.DryRunOnly);
        command.Parameters.AddWithValue("requested_at", attempt.RequestedAt);
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));
    }

    private static void Validate(FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Workflow safe summary is required.", nameof(attempt));
        }
    }

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord MapRecord(
        NpgsqlDataReader reader) =>
        new(
            WorkflowRequestId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_workflow_request_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            RecalculationPreviewAuditId: GetNullableGuid(reader, "semantic_hash_recalculation_preview_audit_id"),
            MutationPreparationAuditId: GetNullableGuid(reader, "mutation_preparation_audit_id"),
            ApprovalReference: GetNullableString(reader, "approval_reference"),
            DualControlReference: GetNullableString(reader, "dual_control_reference"),
            ActorServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
            ReasonCode: GetNullableString(reader, "reason_code"),
            SafeJustification: GetNullableString(reader, "safe_justification"),
            RequestMode: ParseRequestMode(reader.GetString(reader.GetOrdinal("request_mode"))),
            WorkflowStatus: ParseWorkflowStatus(reader.GetString(reader.GetOrdinal("workflow_status"))),
            BlockReasonCode: GetNullableString(reader, "workflow_block_reason_code"),
            MutationInvocationPosture: ParseInvocationPosture(
                reader.GetString(reader.GetOrdinal("mutation_invocation_posture"))),
            GuardedMutationAuditId: GetNullableGuid(reader, "guarded_mutation_audit_id"),
            GuardedMutationStatus: ParseNullableMutationStatus(GetNullableString(reader, "guarded_mutation_status")),
            ExecuteControlledMutationRequested: reader.GetBoolean(
                reader.GetOrdinal("execute_controlled_mutation_requested")),
            MutationInvocationEnabled: reader.GetBoolean(reader.GetOrdinal("mutation_invocation_enabled")),
            DryRunOnly: reader.GetBoolean(reader.GetOrdinal("dry_run_only")),
            RequestedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));

    private static string ToStorageValue(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.NotRequested => "NOT_REQUESTED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval =>
                "READY_FOR_OPERATOR_APPROVAL",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.PreparedButMutationInvocationDisabled =>
                "PREPARED_BUT_MUTATION_INVOCATION_DISABLED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked => "MUTATION_INVOKED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown workflow status.")
        };

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus ParseWorkflowStatus(string value) =>
        value switch
        {
            "READY_FOR_OPERATOR_APPROVAL" =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval,
            "PREPARED_BUT_MUTATION_INVOCATION_DISABLED" =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.PreparedButMutationInvocationDisabled,
            "MUTATION_INVOKED" => FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked,
            "BLOCKED" => FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Unavailable,
            _ => FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.NotRequested
        };

    private static string ToStorageValue(FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode mode) =>
        mode switch
        {
            FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.SingleRecordOnly => "SINGLE_RECORD_ONLY",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.Batch => "BATCH",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown workflow request mode.")
        };

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode ParseRequestMode(string value) =>
        value switch
        {
            "BATCH" => FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.Batch,
            _ => FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.SingleRecordOnly
        };

    private static string ToStorageValue(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture posture) =>
        posture switch
        {
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.NotRequested =>
                "NOT_REQUESTED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.DryRunOnly =>
                "DRY_RUN_ONLY",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Disabled => "DISABLED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Invoked => "INVOKED",
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Blocked => "BLOCKED",
            _ => throw new ArgumentOutOfRangeException(nameof(posture), posture, "Unknown invocation posture.")
        };

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture
        ParseInvocationPosture(string value) =>
        value switch
        {
            "DRY_RUN_ONLY" => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.DryRunOnly,
            "DISABLED" => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Disabled,
            "INVOKED" => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Invoked,
            "BLOCKED" => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Blocked,
            _ => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.NotRequested
        };

    private static string? ToStorageValueOrNull(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? status) =>
        status is null ? null : ToStorageValue(status.Value);

    private static string ToStorageValue(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared => "NOT_PREPARED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled =>
                "PREPARED_BUT_MUTATION_DISABLED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable => "UNAVAILABLE",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation =>
                "PREPARED_FOR_CONTROLLED_MUTATION",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated => "MUTATED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Failed => "FAILED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale => "STALE",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled => "DISABLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown mutation status.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? ParseNullableMutationStatus(
        string? value) =>
        value switch
        {
            null => null,
            "PREPARED_BUT_MUTATION_DISABLED" =>
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled,
            "BLOCKED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable,
            "PREPARED_FOR_CONTROLLED_MUTATION" =>
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation,
            "MUTATED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated,
            "FAILED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Failed,
            "STALE" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
            "DISABLED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled,
            _ => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared
        };

    private static void AddNullable<T>(NpgsqlCommand command, string name, T? value)
    {
        if (value is not null)
        {
            command.Parameters.AddWithValue(name, value);
            return;
        }

        command.Parameters.AddWithValue(name, DBNull.Value);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value, maxLength);

    private static string? GetNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}
