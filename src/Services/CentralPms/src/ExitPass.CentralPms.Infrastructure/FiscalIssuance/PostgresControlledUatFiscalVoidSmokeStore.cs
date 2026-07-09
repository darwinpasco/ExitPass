using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresControlledUatFiscalVoidSmokeStore : IControlledUatFiscalVoidSmokeStore
{
    private const string VoidPosture = "CONTROLLED_UAT_VOID_SMOKE_RECORDED";

    private readonly string _connectionString;

    public PostgresControlledUatFiscalVoidSmokeStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ControlledUatFiscalVoidSmokeStoreResult> RecordApprovedVoidPostureAsync(
        ControlledUatFiscalVoidSmokeStoreRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApprovedVoidSmokeRequest(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(Sql, connection, transaction)
        {
            CommandTimeout = 30
        };

        AddParameters(command, request);

        ControlledUatFiscalVoidSmokeStoreResult result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ControlledUatFiscalVoidSmokeStoreResult.Rejected(
                    "controlled_uat_void_smoke_document_not_found",
                    ["approved_fiscal_document_not_found"]);
            }

            result = new ControlledUatFiscalVoidSmokeStoreResult(
                Succeeded: true,
                Status: "controlled_uat_void_smoke_recorded",
                Errors: Array.Empty<string>(),
                FiscalDocumentNumber: reader.GetString(reader.GetOrdinal("fiscal_document_number")),
                FiscalSequenceValue: reader.GetInt64(reader.GetOrdinal("fiscal_sequence_value")),
                FiscalDocumentStatusPosture: reader.GetString(reader.GetOrdinal("fiscal_document_status_posture")),
                StatusHistoryRecorded: reader.GetBoolean(reader.GetOrdinal("status_history_recorded")),
                AlreadyRecorded: reader.GetBoolean(reader.GetOrdinal("already_recorded")));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static void ValidateApprovedVoidSmokeRequest(ControlledUatFiscalVoidSmokeStoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        Require(request.ProfileId == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.ProfileId, "profile_id_not_approved", errors);
        Require(request.FiscalIssuanceReferenceId == FiscalIssuanceControlledUatVoidSmokeService.ApprovedFiscalIssuanceReferenceId, "fiscal_issuance_reference_id_not_approved", errors);
        Require(request.PosServerFiscalDocumentId == FiscalIssuanceControlledUatVoidSmokeService.ApprovedPosServerFiscalDocumentId, "pos_server_fiscal_document_id_not_approved", errors);
        Require(request.FiscalDocumentNumber == FiscalIssuanceControlledUatVoidSmokeService.ApprovedFiscalDocumentNumber, "fiscal_document_number_not_approved", errors);
        Require(request.PaymentFinalityRef == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.UpstreamFinalityRef, "payment_finality_ref_not_approved", errors);
        Require(request.ReasonCode == FiscalIssuanceControlledUatVoidSmokeService.ApprovedReasonCode, "reason_code_not_approved", errors);
        Require(request.CorrelationId.ToString("D") == FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.CorrelationId, "correlation_id_not_approved", errors);
        Require(!string.IsNullOrWhiteSpace(request.ApprovedBy), "approved_by_required", errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Controlled UAT fiscal void smoke request does not match the approved target: {string.Join(", ", errors)}",
                nameof(request));
        }
    }

    private static void AddParameters(NpgsqlCommand command, ControlledUatFiscalVoidSmokeStoreRequest request)
    {
        command.Parameters.Add("fiscal_document_id", NpgsqlDbType.Uuid).Value = request.PosServerFiscalDocumentId;
        command.Parameters.Add("fiscal_document_number", NpgsqlDbType.Text).Value = request.FiscalDocumentNumber;
        command.Parameters.Add("payment_finality_ref", NpgsqlDbType.Text).Value = request.PaymentFinalityRef;
        command.Parameters.Add("reason_code", NpgsqlDbType.Text).Value = request.ReasonCode;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = request.CorrelationId;
        command.Parameters.Add("approved_by", NpgsqlDbType.Text).Value = request.ApprovedBy;
        command.Parameters.Add("profile_id", NpgsqlDbType.Text).Value = request.ProfileId;
        command.Parameters.Add("fiscal_issuance_reference_id", NpgsqlDbType.Uuid).Value = request.FiscalIssuanceReferenceId;
        command.Parameters.Add("posture", NpgsqlDbType.Text).Value = VoidPosture;
    }

    private static void Require(bool condition, string error, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    private const string Sql = """
        DO $$
        DECLARE
            db_name text := current_database();
        BEGIN
            IF db_name ~* '(prod|production|shared|live)' THEN
                RAISE EXCEPTION 'Refusing controlled UAT fiscal void smoke for unsafe database name: %', db_name;
            END IF;
        END $$;

        WITH selected_document AS (
            SELECT
                fiscal_document_id,
                fiscal_document_status_code_id,
                fiscal_sequence_value,
                fiscal_document_number,
                COALESCE(document_context, '{}'::jsonb) AS document_context,
                COALESCE((
                    COALESCE(document_context, '{}'::jsonb)
                        #>> '{controlledUatVoidSmoke,posture}'
                ) = @posture, FALSE) AS already_recorded
            FROM pos.fiscal_documents
            WHERE fiscal_document_id = @fiscal_document_id
              AND fiscal_document_number = @fiscal_document_number
              AND payment_finality_ref = @payment_finality_ref
              AND fiscal_sequence_value = 2
              AND is_active = TRUE
            FOR UPDATE
        ),
        updated_document AS (
            UPDATE pos.fiscal_documents document
            SET
                document_context = selected_document.document_context || jsonb_build_object(
                    'controlledUatVoidSmoke',
                    jsonb_build_object(
                        'posture', @posture,
                        'reasonCode', @reason_code,
                        'profileId', @profile_id,
                        'fiscalIssuanceReferenceId', @fiscal_issuance_reference_id::text,
                        'correlationId', @correlation_id::text,
                        'approvedBy', @approved_by,
                        'recordedAt', to_jsonb(CURRENT_TIMESTAMP)
                    )
                ),
                updated_at = CURRENT_TIMESTAMP
            FROM selected_document
            WHERE document.fiscal_document_id = selected_document.fiscal_document_id
            RETURNING
                document.fiscal_document_id,
                selected_document.fiscal_document_status_code_id AS prior_status_code_id,
                document.fiscal_document_status_code_id AS new_status_code_id,
                document.fiscal_sequence_value,
                document.fiscal_document_number,
                selected_document.already_recorded
        ),
        inserted_history AS (
            INSERT INTO pos.fiscal_document_status_history (
                fiscal_document_status_history_id,
                fiscal_document_id,
                prior_fiscal_document_status_code_id,
                new_fiscal_document_status_code_id,
                status_reason_code_id,
                status_reason_text,
                changed_at,
                actor_ref,
                service_identity_ref,
                created_at
            )
            SELECT
                gen_random_uuid(),
                fiscal_document_id,
                prior_status_code_id,
                new_status_code_id,
                NULL,
                @reason_code,
                CURRENT_TIMESTAMP,
                @approved_by,
                'central-pms-controlled-uat-void-smoke',
                CURRENT_TIMESTAMP
            FROM updated_document
            WHERE already_recorded = FALSE
            RETURNING fiscal_document_id
        )
        SELECT
            updated_document.fiscal_document_id,
            updated_document.fiscal_document_number,
            updated_document.fiscal_sequence_value,
            @posture AS fiscal_document_status_posture,
            EXISTS (SELECT 1 FROM inserted_history) AS status_history_recorded,
            updated_document.already_recorded
        FROM updated_document;
        """;
}
