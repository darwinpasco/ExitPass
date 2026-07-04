using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionReadbackAttemptRepository :
    IFiscalExceptionReadbackAttemptRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionReadbackAttemptRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
        FiscalExceptionReadbackAttemptWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_readback_reconciliations (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                pos_server_fiscal_document_id,
                readback_requested_at,
                readback_completed_at,
                readback_http_status,
                readback_result_code,
                comparison_result,
                mismatch_reason,
                actor_service_identity_id
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @payment_confirmation_id,
                @pos_server_fiscal_document_id,
                @readback_requested_at,
                @readback_completed_at,
                @readback_http_status,
                @readback_result_code,
                @comparison_result,
                @mismatch_reason,
                @actor_service_identity_id
            )
            RETURNING
                fiscal_issuance_readback_id,
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                pos_server_fiscal_document_id,
                readback_completed_at,
                readback_http_status,
                readback_result_code,
                comparison_result,
                mismatch_reason,
                actor_service_identity_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        command.Parameters.AddWithValue("payment_confirmation_id", attempt.PaymentConfirmationId);
        AddNullable(command, "pos_server_fiscal_document_id", attempt.PosServerFiscalDocumentId);
        command.Parameters.AddWithValue("readback_requested_at", attempt.AttemptedAt);
        command.Parameters.AddWithValue("readback_completed_at", attempt.AttemptedAt);
        AddNullable(command, "readback_http_status", attempt.PosServerHttpStatus);
        command.Parameters.AddWithValue("readback_result_code", Truncate(attempt.SafeResultCode, 120));
        command.Parameters.AddWithValue("comparison_result", ToComparisonResult(attempt.Classification));
        var mismatchReason = ToMismatchReason(attempt);
        AddNullable(command, "mismatch_reason", mismatchReason is null ? null : Truncate(mismatchReason, 160));
        AddNullable(command, "actor_service_identity_id", attempt.ServiceIdentityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Fiscal exception readback attempt insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
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
                fiscal_issuance_readback_id,
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                pos_server_fiscal_document_id,
                readback_completed_at,
                readback_http_status,
                readback_result_code,
                comparison_result,
                mismatch_reason,
                actor_service_identity_id,
                COUNT(*) OVER ()::integer AS attempt_count
            FROM core.fiscal_issuance_readback_reconciliations
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            ORDER BY COALESCE(readback_completed_at, readback_requested_at) DESC,
                     readback_requested_at DESC
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

        var classification = ParseClassification(
            GetNullableString(reader, "comparison_result"),
            GetNullableString(reader, "readback_result_code"));

        return new FiscalExceptionReadbackAttemptSummary(
            Classification: classification,
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("readback_completed_at")),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            SafeErrorSummary: GetNullableString(reader, "mismatch_reason"));
    }

    private static void Validate(FiscalExceptionReadbackAttemptWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (attempt.PaymentConfirmationId == Guid.Empty)
        {
            throw new ArgumentException("Payment confirmation id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.IdentifierType))
        {
            throw new ArgumentException("Readback identifier type is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeResultCode))
        {
            throw new ArgumentException("Readback result code is required.", nameof(attempt));
        }
    }

    private static FiscalExceptionReadbackAttemptRecord MapRecord(NpgsqlDataReader reader)
    {
        var resultCode = GetNullableString(reader, "readback_result_code");
        return new FiscalExceptionReadbackAttemptRecord(
            ReadbackAttemptId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_readback_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("readback_completed_at")),
            Classification: ParseClassification(
                GetNullableString(reader, "comparison_result"),
                resultCode),
            SafeResultCode: resultCode,
            SafeErrorSummary: GetNullableString(reader, "mismatch_reason"),
            PosServerFiscalDocumentId: GetNullableGuid(reader, "pos_server_fiscal_document_id"),
            PosServerHttpStatus: GetNullableInt32(reader, "readback_http_status"),
            ServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"));
    }

    private static string ToComparisonResult(FiscalExceptionReadbackClassification classification) =>
        classification switch
        {
            FiscalExceptionReadbackClassification.Matched => "MATCHED",
            FiscalExceptionReadbackClassification.NotFound => "NOT_FOUND",
            FiscalExceptionReadbackClassification.Mismatch => "MISMATCHED",
            FiscalExceptionReadbackClassification.Failed => "SERVICE_FAILED",
            FiscalExceptionReadbackClassification.Unavailable => "SERVICE_FAILED",
            FiscalExceptionReadbackClassification.Unknown => "INCONCLUSIVE",
            FiscalExceptionReadbackClassification.IdentifierMissing => "INCONCLUSIVE",
            FiscalExceptionReadbackClassification.NotSupportedYet => "INCONCLUSIVE",
            _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unknown readback classification.")
        };

    private static FiscalExceptionReadbackClassification ParseClassification(
        string? comparisonResult,
        string? resultCode) =>
        resultCode switch
        {
            "matched" => FiscalExceptionReadbackClassification.Matched,
            "not_found" => FiscalExceptionReadbackClassification.NotFound,
            "mismatch" => FiscalExceptionReadbackClassification.Mismatch,
            "failed" => FiscalExceptionReadbackClassification.Failed,
            "unavailable" => FiscalExceptionReadbackClassification.Unavailable,
            "unknown" => FiscalExceptionReadbackClassification.Unknown,
            "identifier_missing" => FiscalExceptionReadbackClassification.IdentifierMissing,
            "not_supported_yet" => FiscalExceptionReadbackClassification.NotSupportedYet,
            _ => comparisonResult switch
            {
                "MATCHED" => FiscalExceptionReadbackClassification.Matched,
                "NOT_FOUND" => FiscalExceptionReadbackClassification.NotFound,
                "MISMATCHED" => FiscalExceptionReadbackClassification.Mismatch,
                "SERVICE_FAILED" => FiscalExceptionReadbackClassification.Failed,
                _ => FiscalExceptionReadbackClassification.Unknown
            }
        };

    private static string? ToMismatchReason(FiscalExceptionReadbackAttemptWrite attempt)
    {
        var safeSummary = attempt.SafeErrorSummary;
        var identifier = string.IsNullOrWhiteSpace(attempt.IdentifierValue)
            ? attempt.IdentifierType
            : $"{attempt.IdentifierType}:{attempt.IdentifierValue}";

        return string.IsNullOrWhiteSpace(safeSummary)
            ? identifier
            : $"{safeSummary}; identifier={identifier}";
    }

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

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

}
