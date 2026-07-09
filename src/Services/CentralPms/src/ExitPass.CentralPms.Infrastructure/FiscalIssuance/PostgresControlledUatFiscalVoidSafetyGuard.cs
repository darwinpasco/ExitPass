using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresControlledUatFiscalVoidSafetyGuard : IControlledUatFiscalVoidSafetyGuard
{
    private readonly string _connectionString;

    public PostgresControlledUatFiscalVoidSafetyGuard(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ControlledUatFiscalVoidSafetyGuardResult> ValidateAsync(
        ControlledUatFiscalVoidSafetyGuardRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApprovedRealVoidRequest(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(Sql, connection)
        {
            CommandTimeout = 30
        };

        AddParameters(command, request);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ControlledUatFiscalVoidSafetyGuardResult.Rejected(
                "controlled_uat_real_void_safety_guard_failed",
                ["controlled_uat_real_void_safety_guard_failed"]);
        }

        if (reader.GetBoolean(reader.GetOrdinal("unsafe_database_name")))
        {
            return ControlledUatFiscalVoidSafetyGuardResult.Rejected(
                "controlled_uat_real_void_unsafe_database_name",
                ["unsafe_database_name"]);
        }

        if (!reader.GetBoolean(reader.GetOrdinal("approved_document_found")))
        {
            return ControlledUatFiscalVoidSafetyGuardResult.Rejected(
                "controlled_uat_real_void_document_not_found",
                ["approved_fiscal_document_not_found"]);
        }

        return ControlledUatFiscalVoidSafetyGuardResult.Accepted();
    }

    public static void ValidateApprovedRealVoidRequest(ControlledUatFiscalVoidSafetyGuardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        Require(request.ProfileId == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.ProfileId, "profile_id_not_approved", errors);
        Require(request.FiscalIssuanceReferenceId == FiscalIssuanceControlledUatVoidSmokeService.ApprovedFiscalIssuanceReferenceId, "fiscal_issuance_reference_id_not_approved", errors);
        Require(request.PosServerFiscalDocumentId == FiscalIssuanceControlledUatVoidSmokeService.ApprovedPosServerFiscalDocumentId, "pos_server_fiscal_document_id_not_approved", errors);
        Require(request.FiscalDocumentNumber == FiscalIssuanceControlledUatVoidSmokeService.ApprovedFiscalDocumentNumber, "fiscal_document_number_not_approved", errors);
        Require(request.PaymentFinalityRef == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.UpstreamFinalityRef, "payment_finality_ref_not_approved", errors);
        Require(request.FiscalSequenceValue == 2, "fiscal_sequence_value_not_approved", errors);
        Require(request.ReasonCode == FiscalIssuanceControlledUatVoidSmokeService.ApprovedReasonCode, "reason_code_not_approved", errors);
        Require(request.CorrelationId.ToString("D") == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.CorrelationId, "correlation_id_not_approved", errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Controlled UAT real fiscal void request does not match the approved target: {string.Join(", ", errors)}",
                nameof(request));
        }
    }

    private static void AddParameters(NpgsqlCommand command, ControlledUatFiscalVoidSafetyGuardRequest request)
    {
        command.Parameters.Add("fiscal_document_id", NpgsqlDbType.Uuid).Value = request.PosServerFiscalDocumentId;
        command.Parameters.Add("fiscal_document_number", NpgsqlDbType.Text).Value = request.FiscalDocumentNumber;
        command.Parameters.Add("payment_finality_ref", NpgsqlDbType.Text).Value = request.PaymentFinalityRef;
        command.Parameters.Add("fiscal_sequence_value", NpgsqlDbType.Bigint).Value = request.FiscalSequenceValue!.Value;
    }

    private static void Require(bool condition, string error, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    private const string Sql = """
        SELECT
            current_database() ~* '(prod|production|shared|live)' AS unsafe_database_name,
            EXISTS (
                SELECT 1
                FROM pos.fiscal_documents
                WHERE fiscal_document_id = @fiscal_document_id
                  AND fiscal_document_number = @fiscal_document_number
                  AND payment_finality_ref = @payment_finality_ref
                  AND fiscal_sequence_value = @fiscal_sequence_value
                  AND is_active = TRUE
            ) AS approved_document_found;
        """;
}
