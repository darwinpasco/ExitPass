using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionRetryCommandPreparationAuditRepository :
    IFiscalExceptionRetryCommandPreparationAuditRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionRetryCommandPreparationAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionRetryCommandPreparationAttemptRecord> RecordAsync(
        FiscalExceptionRetryCommandPreparationAttemptWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_retry_command_preparations (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                site_id,
                site_pos_server_id,
                site_pos_server_ref,
                latest_readback_classification,
                retry_eligibility_decision,
                command_preparation_status,
                command_block_reason_code,
                semantic_request_hash_availability,
                idempotency_context_availability,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @payment_confirmation_id,
                @payment_attempt_id,
                @parking_session_id,
                @site_id,
                @site_pos_server_id,
                @site_pos_server_ref,
                @latest_readback_classification,
                @retry_eligibility_decision,
                @command_preparation_status,
                @command_block_reason_code,
                @semantic_request_hash_availability,
                @idempotency_context_availability,
                @attempted_at,
                @safe_summary,
                @correlation_id,
                @actor_service_identity_id
            )
            RETURNING
                retry_command_preparation_attempt_id,
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                site_id,
                site_pos_server_id,
                site_pos_server_ref,
                latest_readback_classification,
                retry_eligibility_decision,
                command_preparation_status,
                command_block_reason_code,
                semantic_request_hash_availability,
                idempotency_context_availability,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                created_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "payment_confirmation_id", attempt.PaymentConfirmationId);
        AddNullable(command, "payment_attempt_id", attempt.PaymentAttemptId);
        AddNullable(command, "parking_session_id", attempt.ParkingSessionId);
        AddNullable(command, "site_id", attempt.SiteId);
        AddNullable(command, "site_pos_server_id", attempt.SitePosServerId);
        AddNullable(command, "site_pos_server_ref", TruncateOrNull(attempt.SitePosServerRef, 128));
        AddNullable(command, "latest_readback_classification", ToStorageValue(attempt.LatestReadbackClassificationBasis));
        command.Parameters.AddWithValue("retry_eligibility_decision", ToStorageValue(attempt.RetryEligibilityDecisionBasis));
        command.Parameters.AddWithValue("command_preparation_status", ToStorageValue(attempt.CommandPreparationStatus));
        AddNullable(command, "command_block_reason_code", TruncateOrNull(attempt.CommandBlockReasonCode, 160));
        command.Parameters.AddWithValue(
            "semantic_request_hash_availability",
            ToStorageValue(attempt.SemanticRequestHashAvailabilityStatus));
        command.Parameters.AddWithValue(
            "idempotency_context_availability",
            ToStorageValue(attempt.IdempotencyContextAvailabilityStatus));
        command.Parameters.AddWithValue("attempted_at", attempt.AttemptedAt);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        AddNullable(command, "actor_service_identity_id", attempt.ServiceIdentityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Fiscal exception retry command preparation insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionRetryCommandPreparationAttemptSummary?> GetSummaryAsync(
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
                command_preparation_status,
                command_block_reason_code,
                semantic_request_hash_availability,
                idempotency_context_availability,
                attempted_at,
                safe_summary,
                COUNT(*) OVER ()::integer AS attempt_count
            FROM core.fiscal_issuance_retry_command_preparations
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

        return new FiscalExceptionRetryCommandPreparationAttemptSummary(
            LastCommandPreparationStatus: ParsePreparationStatus(reader.GetString(reader.GetOrdinal("command_preparation_status"))),
            LastAttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastCommandBlockReasonCode: GetNullableString(reader, "command_block_reason_code"),
            SemanticRequestHashAvailabilityStatus: ParseSemanticStatus(
                reader.GetString(reader.GetOrdinal("semantic_request_hash_availability"))),
            IdempotencyContextAvailabilityStatus: ParseIdempotencyStatus(
                reader.GetString(reader.GetOrdinal("idempotency_context_availability"))),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")));
    }

    private static void Validate(FiscalExceptionRetryCommandPreparationAttemptWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Retry command preparation safe summary is required.", nameof(attempt));
        }
    }

    private static FiscalExceptionRetryCommandPreparationAttemptRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            RetryCommandPreparationAttemptId: reader.GetGuid(reader.GetOrdinal("retry_command_preparation_attempt_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            PaymentConfirmationId: GetNullableGuid(reader, "payment_confirmation_id"),
            PaymentAttemptId: GetNullableGuid(reader, "payment_attempt_id"),
            ParkingSessionId: GetNullableGuid(reader, "parking_session_id"),
            SiteId: GetNullableGuid(reader, "site_id"),
            SitePosServerId: GetNullableGuid(reader, "site_pos_server_id"),
            SitePosServerRef: GetNullableString(reader, "site_pos_server_ref"),
            LatestReadbackClassificationBasis: ParseReadbackClassification(
                GetNullableString(reader, "latest_readback_classification")),
            RetryEligibilityDecisionBasis: ParseRetryEligibilityDecision(
                reader.GetString(reader.GetOrdinal("retry_eligibility_decision"))),
            CommandPreparationStatus: ParsePreparationStatus(
                reader.GetString(reader.GetOrdinal("command_preparation_status"))),
            CommandBlockReasonCode: GetNullableString(reader, "command_block_reason_code"),
            SemanticRequestHashAvailabilityStatus: ParseSemanticStatus(
                reader.GetString(reader.GetOrdinal("semantic_request_hash_availability"))),
            IdempotencyContextAvailabilityStatus: ParseIdempotencyStatus(
                reader.GetString(reader.GetOrdinal("idempotency_context_availability"))),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            ServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
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

    private static string ToStorageValue(FiscalExceptionRetryEligibilityDecision decision) =>
        decision switch
        {
            FiscalExceptionRetryEligibilityDecision.NotEvaluated => "NOT_EVALUATED",
            FiscalExceptionRetryEligibilityDecision.Eligible => "ELIGIBLE",
            FiscalExceptionRetryEligibilityDecision.Blocked => "BLOCKED",
            FiscalExceptionRetryEligibilityDecision.Unavailable => "UNAVAILABLE",
            FiscalExceptionRetryEligibilityDecision.NotRequired => "NOT_REQUIRED",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown retry eligibility decision.")
        };

    private static string ToStorageValue(FiscalExceptionRetryCommandPreparationStatus status) =>
        status switch
        {
            FiscalExceptionRetryCommandPreparationStatus.NotPrepared => "NOT_PREPARED",
            FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable => "PREPARED_NON_EXECUTABLE",
            FiscalExceptionRetryCommandPreparationStatus.Blocked => "BLOCKED",
            FiscalExceptionRetryCommandPreparationStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown retry command preparation status.")
        };

    private static string ToStorageValue(FiscalExceptionSemanticRequestHashAvailabilityStatus status) =>
        status switch
        {
            FiscalExceptionSemanticRequestHashAvailabilityStatus.NotAvailableInCurrentModel => "NOT_AVAILABLE_IN_CURRENT_MODEL",
            FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed => "AVAILABLE_AND_CONFIRMED",
            FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing => "REQUIRED_BUT_MISSING",
            FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButUnconfirmed => "REQUIRED_BUT_UNCONFIRMED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown semantic request hash status.")
        };

    private static string ToStorageValue(FiscalExceptionIdempotencyContextAvailabilityStatus status) =>
        status switch
        {
            FiscalExceptionIdempotencyContextAvailabilityStatus.NotEvaluated => "NOT_EVALUATED",
            FiscalExceptionIdempotencyContextAvailabilityStatus.Available => "AVAILABLE",
            FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference => "MISSING_UPSTREAM_FINALITY_REFERENCE",
            FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected => "NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown idempotency status.")
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

    private static FiscalExceptionRetryEligibilityDecision ParseRetryEligibilityDecision(string value) =>
        value switch
        {
            "ELIGIBLE" => FiscalExceptionRetryEligibilityDecision.Eligible,
            "BLOCKED" => FiscalExceptionRetryEligibilityDecision.Blocked,
            "UNAVAILABLE" => FiscalExceptionRetryEligibilityDecision.Unavailable,
            "NOT_REQUIRED" => FiscalExceptionRetryEligibilityDecision.NotRequired,
            _ => FiscalExceptionRetryEligibilityDecision.NotEvaluated
        };

    private static FiscalExceptionRetryCommandPreparationStatus ParsePreparationStatus(string value) =>
        value switch
        {
            "PREPARED_NON_EXECUTABLE" => FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable,
            "BLOCKED" => FiscalExceptionRetryCommandPreparationStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionRetryCommandPreparationStatus.Unavailable,
            _ => FiscalExceptionRetryCommandPreparationStatus.NotPrepared
        };

    private static FiscalExceptionSemanticRequestHashAvailabilityStatus ParseSemanticStatus(string value) =>
        value switch
        {
            "AVAILABLE_AND_CONFIRMED" => FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed,
            "REQUIRED_BUT_MISSING" => FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing,
            "REQUIRED_BUT_UNCONFIRMED" => FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButUnconfirmed,
            _ => FiscalExceptionSemanticRequestHashAvailabilityStatus.NotAvailableInCurrentModel
        };

    private static FiscalExceptionIdempotencyContextAvailabilityStatus ParseIdempotencyStatus(string value) =>
        value switch
        {
            "AVAILABLE" => FiscalExceptionIdempotencyContextAvailabilityStatus.Available,
            "MISSING_UPSTREAM_FINALITY_REFERENCE" => FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference,
            "NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED" => FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected,
            _ => FiscalExceptionIdempotencyContextAvailabilityStatus.NotEvaluated
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

        dbType = default;
        return false;
    }

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

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
