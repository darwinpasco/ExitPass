using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalIssuanceReferenceRepository : IFiscalIssuanceReferenceRepository
{
    private readonly string _connectionString;

    public PostgresFiscalIssuanceReferenceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalIssuanceReferenceRecord> CreateAsync(
        CreateFiscalIssuanceReferenceRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = request.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Fiscal issuance reference request is invalid: {string.Join(", ", validationErrors)}",
                nameof(request));
        }

        const string sql = """
            INSERT INTO core.fiscal_issuance_references (
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                site_id,
                site_pos_server_id,
                site_pos_server_ref,
                fiscal_document_type_code_id,
                fiscal_document_type_code_key,
                payable_basis_ref,
                upstream_finality_reference,
                pos_server_fiscal_document_id,
                fiscal_identity_id,
                fiscal_sequence_policy_id,
                fiscal_sequence_value,
                fiscal_document_number,
                fiscal_series,
                fiscal_number_prefix_text,
                fiscal_number_suffix_text,
                fiscal_number_assigned_at,
                fiscal_number_assigned_by_ref,
                fiscal_document_status_code_id,
                result_classification,
                fiscal_issuance_evidence_status,
                fiscal_number_assignment_state,
                fiscal_issuance_state,
                latest_exception_reason,
                latest_error_code,
                latest_error_posture,
                correlation_id,
                pos_server_response_timestamp,
                recorded_by_service_identity_id
            )
            VALUES (
                @payment_confirmation_id,
                @payment_attempt_id,
                @parking_session_id,
                @tariff_snapshot_id,
                @site_id,
                @site_pos_server_id,
                @site_pos_server_ref,
                @fiscal_document_type_code_id,
                @fiscal_document_type_code_key,
                @payable_basis_ref,
                @upstream_finality_reference,
                @pos_server_fiscal_document_id,
                @fiscal_identity_id,
                @fiscal_sequence_policy_id,
                @fiscal_sequence_value,
                @fiscal_document_number,
                @fiscal_series,
                @fiscal_number_prefix_text,
                @fiscal_number_suffix_text,
                @fiscal_number_assigned_at,
                @fiscal_number_assigned_by_ref,
                @fiscal_document_status_code_id,
                @result_classification,
                @fiscal_issuance_evidence_status,
                @fiscal_number_assignment_state,
                @fiscal_issuance_state,
                @latest_exception_reason,
                @latest_error_code,
                @latest_error_posture,
                @correlation_id,
                @pos_server_response_timestamp,
                @recorded_by_service_identity_id
            )
            RETURNING
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                site_id,
                site_pos_server_id,
                site_pos_server_ref,
                payable_basis_ref,
                upstream_finality_reference,
                pos_server_fiscal_document_id,
                fiscal_identity_id,
                fiscal_sequence_policy_id,
                fiscal_sequence_value,
                fiscal_document_number,
                fiscal_series,
                fiscal_number_prefix_text,
                fiscal_number_suffix_text,
                fiscal_number_assigned_at,
                fiscal_number_assigned_by_ref,
                fiscal_document_status_code_id,
                result_classification,
                fiscal_issuance_evidence_status,
                fiscal_number_assignment_state,
                fiscal_issuance_state,
                latest_exception_reason,
                latest_error_code,
                latest_error_posture,
                correlation_id,
                pos_server_response_timestamp,
                first_recorded_at,
                last_updated_at,
                recorded_by_service_identity_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        AddCreateParameters(command, request);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Fiscal issuance reference insert returned no rows.");
        }

        return MapReference(reader);
    }

    public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(
        Guid paymentConfirmationId,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "WHERE payment_confirmation_id = @payment_confirmation_id AND is_active = true",
            command => command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId),
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(
        string upstreamFinalityReference,
        Guid? sitePosServerId,
        Guid? fiscalDocumentTypeCodeId,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            WHERE upstream_finality_reference = @upstream_finality_reference
              AND (@site_pos_server_id IS NULL OR site_pos_server_id = @site_pos_server_id)
              AND (@fiscal_document_type_code_id IS NULL OR fiscal_document_type_code_id = @fiscal_document_type_code_id)
              AND is_active = true
            """,
            command =>
            {
                command.Parameters.AddWithValue("upstream_finality_reference", upstreamFinalityReference);
                AddNullable(command, "site_pos_server_id", sitePosServerId);
                AddNullable(command, "fiscal_document_type_code_id", fiscalDocumentTypeCodeId);
            },
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(
        Guid posServerFiscalDocumentId,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "WHERE pos_server_fiscal_document_id = @pos_server_fiscal_document_id AND is_active = true",
            command => command.Parameters.AddWithValue("pos_server_fiscal_document_id", posServerFiscalDocumentId),
            cancellationToken);

    private async Task<FiscalIssuanceReferenceRecord?> QuerySingleAsync(
        string whereClause,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                site_id,
                site_pos_server_id,
                site_pos_server_ref,
                payable_basis_ref,
                upstream_finality_reference,
                pos_server_fiscal_document_id,
                fiscal_identity_id,
                fiscal_sequence_policy_id,
                fiscal_sequence_value,
                fiscal_document_number,
                fiscal_series,
                fiscal_number_prefix_text,
                fiscal_number_suffix_text,
                fiscal_number_assigned_at,
                fiscal_number_assigned_by_ref,
                fiscal_document_status_code_id,
                result_classification,
                fiscal_issuance_evidence_status,
                fiscal_number_assignment_state,
                fiscal_issuance_state,
                latest_exception_reason,
                latest_error_code,
                latest_error_posture,
                correlation_id,
                pos_server_response_timestamp,
                first_recorded_at,
                last_updated_at,
                recorded_by_service_identity_id
            FROM core.fiscal_issuance_references
            {whereClause}
            ORDER BY first_recorded_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        configure(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? MapReference(reader)
            : null;
    }

    private static void AddCreateParameters(NpgsqlCommand command, CreateFiscalIssuanceReferenceRequest request)
    {
        command.Parameters.AddWithValue("payment_confirmation_id", request.PaymentConfirmationId);
        command.Parameters.AddWithValue("payment_attempt_id", request.PaymentAttemptId);
        command.Parameters.AddWithValue("parking_session_id", request.ParkingSessionId);
        AddNullable(command, "tariff_snapshot_id", request.TariffSnapshotId);
        AddNullable(command, "site_id", request.SiteId);
        AddNullable(command, "site_pos_server_id", request.SitePosServerId);
        AddNullable(command, "site_pos_server_ref", request.SitePosServerRef);
        AddNullable(command, "fiscal_document_type_code_id", request.FiscalDocumentTypeCodeId);
        AddNullable(command, "fiscal_document_type_code_key", request.FiscalDocumentTypeCodeKey);
        AddNullable(command, "payable_basis_ref", request.PayableBasisRef);
        command.Parameters.AddWithValue("upstream_finality_reference", request.UpstreamFinalityReference);
        AddNullable(command, "pos_server_fiscal_document_id", request.PosServerFiscalDocumentId);
        AddNullable(command, "fiscal_identity_id", request.FiscalIdentityId);
        AddNullable(command, "fiscal_sequence_policy_id", request.FiscalSequencePolicyId);
        AddNullable(command, "fiscal_sequence_value", request.FiscalSequenceValue);
        AddNullable(command, "fiscal_document_number", request.FiscalDocumentNumber);
        AddNullable(command, "fiscal_series", request.FiscalSeries);
        AddNullable(command, "fiscal_number_prefix_text", request.FiscalNumberPrefixText);
        AddNullable(command, "fiscal_number_suffix_text", request.FiscalNumberSuffixText);
        AddNullable(command, "fiscal_number_assigned_at", request.FiscalNumberAssignedAt);
        AddNullable(command, "fiscal_number_assigned_by_ref", request.FiscalNumberAssignedByRef);
        AddNullable(command, "fiscal_document_status_code_id", request.FiscalDocumentStatusCodeId);
        AddNullable(command, "result_classification", ToDatabaseValue(request.ResultClassification));
        AddNullable(command, "fiscal_issuance_evidence_status", ToDatabaseValue(request.FiscalIssuanceEvidenceStatus));
        command.Parameters.AddWithValue("fiscal_number_assignment_state", ToDatabaseValue(request.FiscalNumberAssignmentState));
        command.Parameters.AddWithValue("fiscal_issuance_state", ToDatabaseValue(request.FiscalIssuanceState));
        AddNullable(command, "latest_exception_reason", ToDatabaseValue(request.LatestExceptionReason));
        AddNullable(command, "latest_error_code", request.LatestErrorCode);
        AddNullable(command, "latest_error_posture", ToDatabaseValue(request.LatestErrorPosture));
        AddNullable(command, "correlation_id", request.CorrelationId);
        AddNullable(command, "pos_server_response_timestamp", request.PosServerResponseTimestamp);
        AddNullable(command, "recorded_by_service_identity_id", request.RecordedByServiceIdentityId);
    }

    private static FiscalIssuanceReferenceRecord MapReference(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            GetNullableGuid(reader, "tariff_snapshot_id"),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_pos_server_id"),
            GetNullableString(reader, "site_pos_server_ref"),
            GetNullableString(reader, "payable_basis_ref"),
            reader.GetString(reader.GetOrdinal("upstream_finality_reference")),
            GetNullableGuid(reader, "pos_server_fiscal_document_id"),
            GetNullableGuid(reader, "fiscal_identity_id"),
            GetNullableGuid(reader, "fiscal_sequence_policy_id"),
            GetNullableLong(reader, "fiscal_sequence_value"),
            GetNullableString(reader, "fiscal_document_number"),
            GetNullableString(reader, "fiscal_series"),
            GetNullableString(reader, "fiscal_number_prefix_text"),
            GetNullableString(reader, "fiscal_number_suffix_text"),
            GetNullableDateTimeOffset(reader, "fiscal_number_assigned_at"),
            GetNullableString(reader, "fiscal_number_assigned_by_ref"),
            GetNullableGuid(reader, "fiscal_document_status_code_id"),
            ParseResultClassification(GetNullableString(reader, "result_classification")),
            ParseEvidenceStatus(GetNullableString(reader, "fiscal_issuance_evidence_status")),
            ParseAssignmentState(reader.GetString(reader.GetOrdinal("fiscal_number_assignment_state"))),
            ParseIntegrationState(reader.GetString(reader.GetOrdinal("fiscal_issuance_state"))),
            ParseExceptionReason(GetNullableString(reader, "latest_exception_reason")),
            GetNullableString(reader, "latest_error_code"),
            ParseErrorPosture(GetNullableString(reader, "latest_error_posture")),
            GetNullableGuid(reader, "correlation_id"),
            GetNullableDateTimeOffset(reader, "pos_server_response_timestamp"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("first_recorded_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_updated_at")),
            GetNullableGuid(reader, "recorded_by_service_identity_id"));

    private static void AddNullable<T>(NpgsqlCommand command, string name, T? value)
    {
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
    }

    private static string ToDatabaseValue(FiscalIssuanceIntegrationState state) =>
        state switch
        {
            FiscalIssuanceIntegrationState.NotRequired => "NOT_REQUIRED",
            FiscalIssuanceIntegrationState.PendingFiscalIssuance => "PENDING_FISCAL_ISSUANCE",
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested => "FISCAL_ISSUANCE_REQUESTED",
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded => "FISCAL_ISSUANCE_RECORDED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed => "FISCAL_ISSUANCE_REPLAYED",
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict => "FISCAL_ISSUANCE_CONFLICT",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest => "FISCAL_ISSUANCE_FAILED_REQUEST",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => "FISCAL_ISSUANCE_FAILED_CONFIGURATION",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService => "FISCAL_ISSUANCE_FAILED_SERVICE",
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => "FISCAL_ISSUANCE_UNKNOWN",
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => "FISCAL_ISSUANCE_MANUAL_REVIEW",
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased => "FISCAL_ISSUANCE_EXCEPTION_RELEASED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled => "FISCAL_ISSUANCE_RECONCILED",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown fiscal issuance state.")
        };

    private static string ToDatabaseValue(FiscalNumberAssignmentState state) =>
        state switch
        {
            FiscalNumberAssignmentState.NotAssigned => "NOT_ASSIGNED",
            FiscalNumberAssignmentState.Assigned => "ASSIGNED",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown fiscal number assignment state.")
        };

    private static string? ToDatabaseValue(FiscalIssuanceResultClassification? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceResultClassification.NewlyCreated => "NEWLY_CREATED",
            FiscalIssuanceResultClassification.IdempotentReplay => "IDEMPOTENT_REPLAY",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fiscal issuance result classification.")
        };

    private static string? ToDatabaseValue(FiscalIssuanceEvidenceStatus? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned => "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fiscal issuance evidence status.")
        };

    private static string? ToDatabaseValue(FiscalIssuanceErrorPosture? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange => "DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE",
            FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection => "RETRY_AFTER_CONFIGURATION_CORRECTION",
            FiscalIssuanceErrorPosture.RetryAfterServiceRecovery => "RETRY_AFTER_SERVICE_RECOVERY",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fiscal issuance error posture.")
        };

    private static string? ToDatabaseValue(FiscalIssuanceExceptionReason? value) =>
        value is null ? null : ToSnakeUpper(value.Value.ToString());

    private static FiscalIssuanceIntegrationState ParseIntegrationState(string value) =>
        value switch
        {
            "NOT_REQUIRED" => FiscalIssuanceIntegrationState.NotRequired,
            "PENDING_FISCAL_ISSUANCE" => FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            "FISCAL_ISSUANCE_REQUESTED" => FiscalIssuanceIntegrationState.FiscalIssuanceRequested,
            "FISCAL_ISSUANCE_RECORDED" => FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            "FISCAL_ISSUANCE_REPLAYED" => FiscalIssuanceIntegrationState.FiscalIssuanceReplayed,
            "FISCAL_ISSUANCE_CONFLICT" => FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
            "FISCAL_ISSUANCE_FAILED_REQUEST" => FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest,
            "FISCAL_ISSUANCE_FAILED_CONFIGURATION" => FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
            "FISCAL_ISSUANCE_FAILED_SERVICE" => FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
            "FISCAL_ISSUANCE_UNKNOWN" => FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            "FISCAL_ISSUANCE_MANUAL_REVIEW" => FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
            "FISCAL_ISSUANCE_EXCEPTION_RELEASED" => FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased,
            "FISCAL_ISSUANCE_RECONCILED" => FiscalIssuanceIntegrationState.FiscalIssuanceReconciled,
            _ => throw new InvalidOperationException($"Unknown fiscal issuance state '{value}'.")
        };

    private static FiscalNumberAssignmentState ParseAssignmentState(string value) =>
        value switch
        {
            "NOT_ASSIGNED" => FiscalNumberAssignmentState.NotAssigned,
            "ASSIGNED" => FiscalNumberAssignmentState.Assigned,
            _ => throw new InvalidOperationException($"Unknown fiscal number assignment state '{value}'.")
        };

    private static FiscalIssuanceResultClassification? ParseResultClassification(string? value) =>
        value switch
        {
            null => null,
            "NEWLY_CREATED" => FiscalIssuanceResultClassification.NewlyCreated,
            "IDEMPOTENT_REPLAY" => FiscalIssuanceResultClassification.IdempotentReplay,
            _ => throw new InvalidOperationException($"Unknown fiscal issuance result classification '{value}'.")
        };

    private static FiscalIssuanceEvidenceStatus? ParseEvidenceStatus(string? value) =>
        value switch
        {
            null => null,
            "FISCAL_DOCUMENT_NUMBER_ASSIGNED" => FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            _ => throw new InvalidOperationException($"Unknown fiscal issuance evidence status '{value}'.")
        };

    private static FiscalIssuanceErrorPosture? ParseErrorPosture(string? value) =>
        value switch
        {
            null => null,
            "DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE" => FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
            "RETRY_AFTER_CONFIGURATION_CORRECTION" => FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection,
            "RETRY_AFTER_SERVICE_RECOVERY" => FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            _ => throw new InvalidOperationException($"Unknown fiscal issuance error posture '{value}'.")
        };

    private static FiscalIssuanceExceptionReason? ParseExceptionReason(string? value) =>
        value is null
            ? null
            : Enum.TryParse<FiscalIssuanceExceptionReason>(ToPascalCase(value), out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Unknown fiscal issuance exception reason '{value}'.");

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? GetNullableLong(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string ToSnakeUpper(string value)
    {
        var result = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Add('_');
            }

            result.Add(char.ToUpperInvariant(character));
        }

        return new string(result.ToArray());
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => string.Concat(
            char.ToUpperInvariant(part[0]),
            part[1..].ToLowerInvariant())));
    }
}
