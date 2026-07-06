using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionControlledRetryExecutionAuditRepository :
    IFiscalExceptionControlledRetryExecutionAuditRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionControlledRetryExecutionAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionControlledRetryExecutionAttemptRecord> RecordAsync(
        FiscalExceptionControlledRetryExecutionAttemptWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_retry_execution_attempts (
                fiscal_issuance_reference_id,
                retry_command_preparation_attempt_id,
                retry_schedule_preparation_attempt_id,
                readback_classification_basis,
                semantic_request_hash_value,
                semantic_request_hash_algorithm,
                semantic_request_hash_source_version,
                upstream_finality_reference,
                execution_status,
                block_reason_code,
                pos_server_outcome,
                pos_server_result_classification,
                pos_server_fiscal_document_id,
                fiscal_document_number,
                fiscal_identity_id,
                fiscal_sequence_policy_id,
                fiscal_sequence_value,
                fiscal_series,
                fiscal_number_prefix_text,
                fiscal_number_suffix_text,
                fiscal_number_assigned_at,
                fiscal_number_assigned_by_ref,
                attempted_at,
                completed_at,
                actor_service_identity_id,
                correlation_id,
                safe_summary
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @retry_command_preparation_attempt_id,
                @retry_schedule_preparation_attempt_id,
                @readback_classification_basis,
                @semantic_request_hash_value,
                @semantic_request_hash_algorithm,
                @semantic_request_hash_source_version,
                @upstream_finality_reference,
                @execution_status,
                @block_reason_code,
                @pos_server_outcome,
                @pos_server_result_classification,
                @pos_server_fiscal_document_id,
                @fiscal_document_number,
                @fiscal_identity_id,
                @fiscal_sequence_policy_id,
                @fiscal_sequence_value,
                @fiscal_series,
                @fiscal_number_prefix_text,
                @fiscal_number_suffix_text,
                @fiscal_number_assigned_at,
                @fiscal_number_assigned_by_ref,
                @attempted_at,
                @completed_at,
                @actor_service_identity_id,
                @correlation_id,
                @safe_summary
            )
            RETURNING
                retry_execution_attempt_id,
                fiscal_issuance_reference_id,
                retry_command_preparation_attempt_id,
                retry_schedule_preparation_attempt_id,
                readback_classification_basis,
                semantic_request_hash_value,
                semantic_request_hash_algorithm,
                semantic_request_hash_source_version,
                upstream_finality_reference,
                execution_status,
                block_reason_code,
                pos_server_outcome,
                pos_server_result_classification,
                pos_server_fiscal_document_id,
                fiscal_document_number,
                fiscal_identity_id,
                fiscal_sequence_policy_id,
                fiscal_sequence_value,
                fiscal_series,
                fiscal_number_prefix_text,
                fiscal_number_suffix_text,
                fiscal_number_assigned_at,
                fiscal_number_assigned_by_ref,
                attempted_at,
                completed_at,
                actor_service_identity_id,
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

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "retry_command_preparation_attempt_id", attempt.RetryCommandPreparationAttemptId);
        AddNullable(command, "retry_schedule_preparation_attempt_id", attempt.RetrySchedulePreparationAttemptId);
        AddNullable(command, "readback_classification_basis", ToStorageValue(attempt.ReadbackClassificationBasis));
        AddNullable(command, "semantic_request_hash_value", TruncateOrNull(attempt.SemanticRequestHashValue, 64));
        AddNullable(command, "semantic_request_hash_algorithm", TruncateOrNull(attempt.SemanticRequestHashAlgorithm, 32));
        AddNullable(command, "semantic_request_hash_source_version", TruncateOrNull(attempt.SemanticRequestHashSourceVersion, 80));
        AddNullable(command, "upstream_finality_reference", TruncateOrNull(attempt.UpstreamFinalityReference, 200));
        command.Parameters.AddWithValue("execution_status", ToStorageValue(attempt.ExecutionStatus));
        AddNullable(command, "block_reason_code", TruncateOrNull(attempt.BlockReasonCode, 160));
        AddNullable(command, "pos_server_outcome", ToStorageValue(attempt.PosServerOutcome));
        AddNullable(command, "pos_server_result_classification", ToStorageValue(attempt.PosServerResultClassification));
        AddNullable(command, "pos_server_fiscal_document_id", attempt.PosServerFiscalDocumentId);
        AddNullable(command, "fiscal_document_number", TruncateOrNull(attempt.FiscalDocumentNumber, 80));
        AddNullable(command, "fiscal_identity_id", attempt.FiscalIdentityId);
        AddNullable(command, "fiscal_sequence_policy_id", attempt.FiscalSequencePolicyId);
        AddNullable(command, "fiscal_sequence_value", attempt.FiscalSequenceValue);
        AddNullable(command, "fiscal_series", TruncateOrNull(attempt.FiscalSeries, 40));
        AddNullable(command, "fiscal_number_prefix_text", TruncateOrNull(attempt.FiscalNumberPrefixText, 40));
        AddNullable(command, "fiscal_number_suffix_text", TruncateOrNull(attempt.FiscalNumberSuffixText, 40));
        AddNullable(command, "fiscal_number_assigned_at", attempt.FiscalNumberAssignedAt);
        AddNullable(command, "fiscal_number_assigned_by_ref", TruncateOrNull(attempt.FiscalNumberAssignedByRef, 160));
        command.Parameters.AddWithValue("attempted_at", attempt.AttemptedAt);
        AddNullable(command, "completed_at", attempt.CompletedAt);
        AddNullable(command, "actor_service_identity_id", attempt.ServiceIdentityId);
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Fiscal exception retry execution insert returned no rows.");
        }

        return MapRecord(reader);
    }

    private static void Validate(FiscalExceptionControlledRetryExecutionAttemptWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Retry execution safe summary is required.", nameof(attempt));
        }
    }

    private static FiscalExceptionControlledRetryExecutionAttemptRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            RetryExecutionAttemptId: reader.GetGuid(reader.GetOrdinal("retry_execution_attempt_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            RetryCommandPreparationAttemptId: GetNullableGuid(reader, "retry_command_preparation_attempt_id"),
            RetrySchedulePreparationAttemptId: GetNullableGuid(reader, "retry_schedule_preparation_attempt_id"),
            ReadbackClassificationBasis: ParseReadbackClassification(
                GetNullableString(reader, "readback_classification_basis")),
            SemanticRequestHashValue: GetNullableString(reader, "semantic_request_hash_value"),
            SemanticRequestHashAlgorithm: GetNullableString(reader, "semantic_request_hash_algorithm"),
            SemanticRequestHashSourceVersion: GetNullableString(reader, "semantic_request_hash_source_version"),
            UpstreamFinalityReference: GetNullableString(reader, "upstream_finality_reference"),
            ExecutionStatus: ParseExecutionStatus(reader.GetString(reader.GetOrdinal("execution_status"))),
            BlockReasonCode: GetNullableString(reader, "block_reason_code"),
            PosServerOutcome: ParsePosServerOutcome(GetNullableString(reader, "pos_server_outcome")),
            PosServerResultClassification: ParseResultClassification(
                GetNullableString(reader, "pos_server_result_classification")),
            PosServerFiscalDocumentId: GetNullableGuid(reader, "pos_server_fiscal_document_id"),
            FiscalDocumentNumber: GetNullableString(reader, "fiscal_document_number"),
            FiscalIdentityId: GetNullableGuid(reader, "fiscal_identity_id"),
            FiscalSequencePolicyId: GetNullableGuid(reader, "fiscal_sequence_policy_id"),
            FiscalSequenceValue: GetNullableInt64(reader, "fiscal_sequence_value"),
            FiscalSeries: GetNullableString(reader, "fiscal_series"),
            FiscalNumberPrefixText: GetNullableString(reader, "fiscal_number_prefix_text"),
            FiscalNumberSuffixText: GetNullableString(reader, "fiscal_number_suffix_text"),
            FiscalNumberAssignedAt: GetNullableDateTimeOffset(reader, "fiscal_number_assigned_at"),
            FiscalNumberAssignedByRef: GetNullableString(reader, "fiscal_number_assigned_by_ref"),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            CompletedAt: GetNullableDateTimeOffset(reader, "completed_at"),
            ServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));

    private static string? ToStorageValue(FiscalExceptionReadbackClassification? classification) =>
        classification switch
        {
            null => null,
            FiscalExceptionReadbackClassification.Matched => "MATCHED",
            FiscalExceptionReadbackClassification.NotFound => "NOT_FOUND",
            FiscalExceptionReadbackClassification.Mismatch => "MISMATCH",
            FiscalExceptionReadbackClassification.Failed => "FAILED",
            FiscalExceptionReadbackClassification.Unavailable => "UNAVAILABLE",
            FiscalExceptionReadbackClassification.Unknown => "UNKNOWN",
            FiscalExceptionReadbackClassification.IdentifierMissing => "IDENTIFIER_MISSING",
            FiscalExceptionReadbackClassification.NotSupportedYet => "NOT_SUPPORTED_YET",
            _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unknown readback classification.")
        };

    private static string ToStorageValue(FiscalExceptionControlledRetryExecutionStatus status) =>
        status switch
        {
            FiscalExceptionControlledRetryExecutionStatus.NotAttempted => "NOT_ATTEMPTED",
            FiscalExceptionControlledRetryExecutionStatus.Disabled => "DISABLED",
            FiscalExceptionControlledRetryExecutionStatus.DryRunReady => "DRY_RUN_READY",
            FiscalExceptionControlledRetryExecutionStatus.Executed => "EXECUTED",
            FiscalExceptionControlledRetryExecutionStatus.ReplayMatched => "REPLAY_MATCHED",
            FiscalExceptionControlledRetryExecutionStatus.Conflict => "CONFLICT",
            FiscalExceptionControlledRetryExecutionStatus.Blocked => "BLOCKED",
            FiscalExceptionControlledRetryExecutionStatus.Unavailable => "UNAVAILABLE",
            FiscalExceptionControlledRetryExecutionStatus.Unknown => "UNKNOWN",
            FiscalExceptionControlledRetryExecutionStatus.Failed => "FAILED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown retry execution status.")
        };

    private static string? ToStorageValue(PosServerFiscalDocumentOutcome? outcome) =>
        outcome switch
        {
            null => null,
            PosServerFiscalDocumentOutcome.Accepted => "ACCEPTED",
            PosServerFiscalDocumentOutcome.Conflict => "CONFLICT",
            PosServerFiscalDocumentOutcome.FailedRequest => "FAILED_REQUEST",
            PosServerFiscalDocumentOutcome.FailedConfiguration => "FAILED_CONFIGURATION",
            PosServerFiscalDocumentOutcome.FailedService => "FAILED_SERVICE",
            PosServerFiscalDocumentOutcome.InvalidResponse => "INVALID_RESPONSE",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown POS Server outcome.")
        };

    private static string? ToStorageValue(FiscalIssuanceResultClassification? classification) =>
        classification switch
        {
            null => null,
            FiscalIssuanceResultClassification.NewlyCreated => "NEWLY_CREATED",
            FiscalIssuanceResultClassification.IdempotentReplay => "IDEMPOTENT_REPLAY",
            _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unknown result classification.")
        };

    private static FiscalExceptionControlledRetryExecutionStatus ParseExecutionStatus(string value) =>
        value switch
        {
            "DISABLED" => FiscalExceptionControlledRetryExecutionStatus.Disabled,
            "DRY_RUN_READY" => FiscalExceptionControlledRetryExecutionStatus.DryRunReady,
            "EXECUTED" => FiscalExceptionControlledRetryExecutionStatus.Executed,
            "REPLAY_MATCHED" => FiscalExceptionControlledRetryExecutionStatus.ReplayMatched,
            "CONFLICT" => FiscalExceptionControlledRetryExecutionStatus.Conflict,
            "BLOCKED" => FiscalExceptionControlledRetryExecutionStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionControlledRetryExecutionStatus.Unavailable,
            "UNKNOWN" => FiscalExceptionControlledRetryExecutionStatus.Unknown,
            "FAILED" => FiscalExceptionControlledRetryExecutionStatus.Failed,
            _ => FiscalExceptionControlledRetryExecutionStatus.NotAttempted
        };

    private static PosServerFiscalDocumentOutcome? ParsePosServerOutcome(string? value) =>
        value switch
        {
            null => null,
            "ACCEPTED" => PosServerFiscalDocumentOutcome.Accepted,
            "CONFLICT" => PosServerFiscalDocumentOutcome.Conflict,
            "FAILED_REQUEST" => PosServerFiscalDocumentOutcome.FailedRequest,
            "FAILED_CONFIGURATION" => PosServerFiscalDocumentOutcome.FailedConfiguration,
            "FAILED_SERVICE" => PosServerFiscalDocumentOutcome.FailedService,
            "INVALID_RESPONSE" => PosServerFiscalDocumentOutcome.InvalidResponse,
            _ => PosServerFiscalDocumentOutcome.InvalidResponse
        };

    private static FiscalIssuanceResultClassification? ParseResultClassification(string? value) =>
        value switch
        {
            null => null,
            "NEWLY_CREATED" => FiscalIssuanceResultClassification.NewlyCreated,
            "IDEMPOTENT_REPLAY" => FiscalIssuanceResultClassification.IdempotentReplay,
            _ => null
        };

    private static FiscalExceptionReadbackClassification? ParseReadbackClassification(string? value) =>
        value switch
        {
            null => null,
            "MATCHED" => FiscalExceptionReadbackClassification.Matched,
            "NOT_FOUND" => FiscalExceptionReadbackClassification.NotFound,
            "MISMATCH" => FiscalExceptionReadbackClassification.Mismatch,
            "FAILED" => FiscalExceptionReadbackClassification.Failed,
            "UNAVAILABLE" => FiscalExceptionReadbackClassification.Unavailable,
            "UNKNOWN" => FiscalExceptionReadbackClassification.Unknown,
            "IDENTIFIER_MISSING" => FiscalExceptionReadbackClassification.IdentifierMissing,
            "NOT_SUPPORTED_YET" => FiscalExceptionReadbackClassification.NotSupportedYet,
            _ => FiscalExceptionReadbackClassification.Unknown
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

        if (underlyingType == typeof(long))
        {
            dbType = NpgsqlDbType.Bigint;
            return true;
        }

        if (underlyingType == typeof(DateTimeOffset))
        {
            dbType = NpgsqlDbType.TimestampTz;
            return true;
        }

        dbType = default;
        return false;
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? GetNullableInt64(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
