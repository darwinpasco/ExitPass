using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

/// <summary>
/// Verifies Central PMS fiscal reference state persistence scaffolding against the disposable DB harness.
/// </summary>
public sealed class FiscalIssuanceReferenceRepositoryTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task FiscalReferencePatchHarness_AppliesPatchAndRunsValidation()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);

        Assert.True(await TableExistsAsync("core.fiscal_issuance_references"));
        Assert.True(await TableExistsAsync("core.fiscal_issuance_attempt_history"));
        Assert.True(await TableExistsAsync("core.fiscal_issuance_exception_reviews"));
        Assert.True(await TableExistsAsync("core.fiscal_issuance_readback_reconciliations"));
    }

    [Fact]
    public async Task CreateAsync_WhenRecordedFiscalEvidenceIsComplete_PersistsAndReadsReference()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var context = PaymentTestContext.Create(nameof(CreateAsync_WhenRecordedFiscalEvidenceIsComplete_PersistsAndReadsReference));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed fiscal issuance reference test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var request = CreateRecordedRequest(context, attempt, confirmation);

            var created = await repository.CreateAsync(request, CancellationToken.None);
            var byConfirmation = await repository.FindByPaymentConfirmationIdAsync(
                confirmation.PaymentConfirmationId,
                CancellationToken.None);
            var byPaymentAttempt = await repository.FindLatestByPaymentAttemptIdAsync(
                attempt.PaymentAttemptId,
                CancellationToken.None);
            var byUpstreamFinality = await repository.FindByUpstreamFinalityReferenceAsync(
                request.UpstreamFinalityReference,
                request.SitePosServerId,
                request.FiscalDocumentTypeCodeId,
                CancellationToken.None);
            var byPosDocument = await repository.FindByPosServerFiscalDocumentIdAsync(
                request.PosServerFiscalDocumentId!.Value,
                CancellationToken.None);

            Assert.NotEqual(Guid.Empty, created.FiscalIssuanceReferenceId);
            Assert.Equal(confirmation.PaymentConfirmationId, created.PaymentConfirmationId);
            Assert.Equal(attempt.PaymentAttemptId, created.PaymentAttemptId);
            Assert.Equal(context.ParkingSessionId, created.ParkingSessionId);
            Assert.Equal(request.UpstreamFinalityReference, created.UpstreamFinalityReference);
            Assert.Equal(request.PosServerFiscalDocumentId, created.PosServerFiscalDocumentId);
            Assert.Equal(request.FiscalDocumentNumber, created.FiscalDocumentNumber);
            Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, created.FiscalIssuanceState);
            Assert.Equal(FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned, created.FiscalIssuanceEvidenceStatus);
            Assert.Equal(FiscalNumberAssignmentState.Assigned, created.FiscalNumberAssignmentState);
            Assert.Equal(FiscalIssuanceResultClassification.NewlyCreated, created.ResultClassification);
            Assert.Equal(created.FiscalIssuanceReferenceId, byConfirmation?.FiscalIssuanceReferenceId);
            Assert.Equal(created.FiscalIssuanceReferenceId, byPaymentAttempt?.FiscalIssuanceReferenceId);
            Assert.Equal(created.FiscalIssuanceReferenceId, byUpstreamFinality?.FiscalIssuanceReferenceId);
            Assert.Equal(created.FiscalIssuanceReferenceId, byPosDocument?.FiscalIssuanceReferenceId);
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenPendingFiscalIssuance_PersistsPendingStateWithoutPosServerEvidence()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var context = PaymentTestContext.Create(nameof(CreateAsync_WhenPendingFiscalIssuance_PersistsPendingStateWithoutPosServerEvidence));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed pending fiscal issuance reference test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var request = CreatePendingRequest(context, attempt, confirmation);

            var created = await repository.CreateAsync(request, CancellationToken.None);

            Assert.Equal(FiscalIssuanceIntegrationState.PendingFiscalIssuance, created.FiscalIssuanceState);
            Assert.Equal(FiscalNumberAssignmentState.NotAssigned, created.FiscalNumberAssignmentState);
            Assert.Null(created.PosServerFiscalDocumentId);
            Assert.Null(created.FiscalDocumentNumber);
            Assert.Null(created.FiscalIssuanceEvidenceStatus);
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenFailureStateIncludesReason_PersistsExceptionReasonAndErrorPosture()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var context = PaymentTestContext.Create(nameof(CreateAsync_WhenFailureStateIncludesReason_PersistsExceptionReasonAndErrorPosture));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed fiscal issuance failure-state test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var request = CreateFailureRequest(context, attempt, confirmation);

            var created = await repository.CreateAsync(request, CancellationToken.None);

            Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, created.FiscalIssuanceState);
            Assert.Equal(FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound, created.LatestExceptionReason);
            Assert.Equal("fiscal_sequence_policy_not_found", created.LatestErrorCode);
            Assert.Equal(FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection, created.LatestErrorPosture);
            Assert.Equal(FiscalNumberAssignmentState.NotAssigned, created.FiscalNumberAssignmentState);
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ShellTables_WhenFiscalReferenceExists_PersistAttemptExceptionAndReadbackRows()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var context = PaymentTestContext.Create(nameof(ShellTables_WhenFiscalReferenceExists_PersistAttemptExceptionAndReadbackRows));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed fiscal shell table test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var created = await CreateRepository().CreateAsync(
                CreateFailureRequest(context, attempt, confirmation),
                CancellationToken.None);

            await InsertShellRowsAsync(created, confirmation);

            Assert.Equal(1, await CountRowsAsync("core.fiscal_issuance_attempt_history", confirmation.PaymentConfirmationId));
            Assert.Equal(1, await CountRowsAsync("core.fiscal_issuance_exception_reviews", confirmation.PaymentConfirmationId));
            Assert.Equal(1, await CountRowsAsync("core.fiscal_issuance_readback_reconciliations", confirmation.PaymentConfirmationId));
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenDuplicateIdempotencyScope_RejectsDuplicateActiveReference()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var firstContext = PaymentTestContext.Create(nameof(CreateAsync_WhenDuplicateIdempotencyScope_RejectsDuplicateActiveReference));
        var secondContext = PaymentTestContext.Create($"{nameof(CreateAsync_WhenDuplicateIdempotencyScope_RejectsDuplicateActiveReference)}Second");

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, firstContext, "Seed first fiscal uniqueness test data.");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, secondContext, "Seed second fiscal uniqueness test data.");

        try
        {
            var repository = CreateRepository();
            var (firstAttempt, firstConfirmation) = await CreateConfirmedPaymentAsync(firstContext);
            var (secondAttempt, secondConfirmation) = await CreateConfirmedPaymentAsync(secondContext);
            var sitePosServerId = Guid.NewGuid();
            var fiscalDocumentTypeCodeId = Guid.NewGuid();
            var upstreamFinalityReference = $"upstream-finality-{Guid.NewGuid():N}";

            await repository.CreateAsync(
                CreateRecordedRequest(
                    firstContext,
                    firstAttempt,
                    firstConfirmation,
                    sitePosServerId,
                    fiscalDocumentTypeCodeId,
                    upstreamFinalityReference),
                CancellationToken.None);

            var duplicateRequest = CreateRecordedRequest(
                secondContext,
                secondAttempt,
                secondConfirmation,
                sitePosServerId,
                fiscalDocumentTypeCodeId,
                upstreamFinalityReference);

            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                repository.CreateAsync(duplicateRequest, CancellationToken.None));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(firstContext);
            await CleanupFiscalReferenceRowsAsync(secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenDuplicatePosServerFiscalDocumentId_RejectsDuplicateActiveReference()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);
        var firstContext = PaymentTestContext.Create(nameof(CreateAsync_WhenDuplicatePosServerFiscalDocumentId_RejectsDuplicateActiveReference));
        var secondContext = PaymentTestContext.Create($"{nameof(CreateAsync_WhenDuplicatePosServerFiscalDocumentId_RejectsDuplicateActiveReference)}Second");

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, firstContext, "Seed first POS document uniqueness test data.");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, secondContext, "Seed second POS document uniqueness test data.");

        try
        {
            var repository = CreateRepository();
            var (firstAttempt, firstConfirmation) = await CreateConfirmedPaymentAsync(firstContext);
            var (secondAttempt, secondConfirmation) = await CreateConfirmedPaymentAsync(secondContext);
            var posServerFiscalDocumentId = Guid.NewGuid();

            await repository.CreateAsync(
                CreateRecordedRequest(
                    firstContext,
                    firstAttempt,
                    firstConfirmation,
                    posServerFiscalDocumentId: posServerFiscalDocumentId),
                CancellationToken.None);

            var duplicateRequest = CreateRecordedRequest(
                secondContext,
                secondAttempt,
                secondConfirmation,
                posServerFiscalDocumentId: posServerFiscalDocumentId);

            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                repository.CreateAsync(duplicateRequest, CancellationToken.None));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        }
        finally
        {
            await CleanupFiscalReferenceRowsAsync(firstContext);
            await CleanupFiscalReferenceRowsAsync(secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
        }
    }

    [Fact]
    public async Task FiscalReferenceTables_DoNotContainSensitiveRawPayloadColumns()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(ConnectionString);

        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'core'
              AND table_name IN (
                  'fiscal_issuance_references',
                  'fiscal_issuance_attempt_history',
                  'fiscal_issuance_exception_reviews',
                  'fiscal_issuance_readback_reconciliations'
              )
              AND (
                  column_name ILIKE '%raw_payload%'
                  OR column_name ILIKE '%callback_payload%'
                  OR column_name ILIKE '%pan%'
                  OR column_name ILIKE '%cvv%'
                  OR column_name ILIKE '%secret%'
                  OR column_name ILIKE '%token%'
              );
            """;

        Assert.Equal(0L, await ExecuteScalarLongAsync(sql));
    }

    private static PostgresFiscalIssuanceReferenceRepository CreateRepository() =>
        new(ConnectionString);

    private static async Task<(CreateAttemptResult Attempt, RecordPaymentConfirmationResult Confirmation)> CreateConfirmedPaymentAsync(
        PaymentTestContext context)
    {
        var attempt = await CreateAttemptAsync(
            ConnectionString,
            context,
            $"fiscal-ref-idem-{Guid.NewGuid():N}",
            "fiscal-reference-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"PCONF-FISCAL-{Guid.NewGuid():N}",
            "fiscal-reference-test",
            context.CorrelationId);

        Assert.NotNull(confirmation);
        return (attempt, confirmation!);
    }

    private static CreateFiscalIssuanceReferenceRequest CreateRecordedRequest(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation,
        Guid? sitePosServerId = null,
        Guid? fiscalDocumentTypeCodeId = null,
        string? upstreamFinalityReference = null,
        Guid? posServerFiscalDocumentId = null)
    {
        var sequenceValue = Random.Shared.Next(100000, 999999);
        return new CreateFiscalIssuanceReferenceRequest(
            PaymentConfirmationId: confirmation.PaymentConfirmationId,
            PaymentAttemptId: attempt.PaymentAttemptId,
            ParkingSessionId: context.ParkingSessionId,
            TariffSnapshotId: context.TariffSnapshotId,
            SiteId: context.SiteId,
            SitePosServerId: sitePosServerId ?? Guid.NewGuid(),
            SitePosServerRef: $"site-pos-{context.SiteCode}",
            FiscalDocumentTypeCodeId: fiscalDocumentTypeCodeId ?? Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            PayableBasisRef: context.TariffSnapshotId.ToString("N"),
            UpstreamFinalityReference: upstreamFinalityReference ?? $"upstream-finality-{confirmation.PaymentConfirmationId:N}",
            PosServerFiscalDocumentId: posServerFiscalDocumentId ?? Guid.NewGuid(),
            FiscalIdentityId: Guid.NewGuid(),
            FiscalSequencePolicyId: Guid.NewGuid(),
            FiscalSequenceValue: sequenceValue,
            FiscalDocumentNumber: $"SI-{sequenceValue}",
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.UtcNow,
            FiscalNumberAssignedByRef: "pos-server-runtime-test",
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: context.CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            RecordedByServiceIdentityId: context.RequestedByUserId);
    }

    private static CreateFiscalIssuanceReferenceRequest CreatePendingRequest(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation) =>
        new(
            PaymentConfirmationId: confirmation.PaymentConfirmationId,
            PaymentAttemptId: attempt.PaymentAttemptId,
            ParkingSessionId: context.ParkingSessionId,
            TariffSnapshotId: context.TariffSnapshotId,
            SiteId: context.SiteId,
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: $"site-pos-{context.SiteCode}",
            FiscalDocumentTypeCodeId: Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            PayableBasisRef: context.TariffSnapshotId.ToString("N"),
            UpstreamFinalityReference: $"upstream-finality-{confirmation.PaymentConfirmationId:N}",
            PosServerFiscalDocumentId: null,
            FiscalIdentityId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: context.CorrelationId,
            PosServerResponseTimestamp: null,
            RecordedByServiceIdentityId: context.RequestedByUserId);

    private static CreateFiscalIssuanceReferenceRequest CreateFailureRequest(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation) =>
        CreatePendingRequest(context, attempt, confirmation) with
        {
            FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
            LatestExceptionReason = FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound,
            LatestErrorCode = "fiscal_sequence_policy_not_found",
            LatestErrorPosture = FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
        };

    private static async Task InsertShellRowsAsync(
        FiscalIssuanceReferenceRecord reference,
        RecordPaymentConfirmationResult confirmation)
    {
        const string sql = """
            INSERT INTO core.fiscal_issuance_attempt_history (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                attempt_sequence_number,
                trigger_source,
                action_type,
                request_correlation_id,
                upstream_finality_reference,
                pos_server_http_status,
                pos_server_response_code,
                error_code,
                error_posture,
                completed_at,
                outcome_classification
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @payment_confirmation_id,
                1,
                'AUTOMATIC',
                'CREATE',
                @correlation_id,
                @upstream_finality_reference,
                400,
                'configuration_error',
                'fiscal_sequence_policy_not_found',
                'RETRY_AFTER_CONFIGURATION_CORRECTION',
                now(),
                'FAILED_CONFIGURATION'
            );

            INSERT INTO core.fiscal_issuance_exception_reviews (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                current_exception_state,
                exception_reason_code,
                exception_category,
                review_status,
                supervisor_escalation_required,
                customer_impacting
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @payment_confirmation_id,
                'FISCAL_ISSUANCE_FAILED_CONFIGURATION',
                'FISCAL_SEQUENCE_POLICY_NOT_FOUND',
                'CONFIGURATION',
                'OPEN',
                true,
                true
            );

            INSERT INTO core.fiscal_issuance_readback_reconciliations (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                pos_server_fiscal_document_id,
                readback_completed_at,
                readback_http_status,
                readback_result_code,
                comparison_result,
                mismatch_reason
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @payment_confirmation_id,
                @pos_server_fiscal_document_id,
                now(),
                404,
                'not_found',
                'NOT_FOUND',
                'readback_not_found'
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fiscal_issuance_reference_id", reference.FiscalIssuanceReferenceId);
        command.Parameters.AddWithValue("payment_confirmation_id", confirmation.PaymentConfirmationId);
        command.Parameters.AddWithValue("correlation_id", reference.CorrelationId ?? Guid.NewGuid());
        command.Parameters.AddWithValue("upstream_finality_reference", reference.UpstreamFinalityReference);
        command.Parameters.AddWithValue("pos_server_fiscal_document_id", reference.PosServerFiscalDocumentId ?? Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupFiscalReferenceRowsAsync(PaymentTestContext context)
    {
        const string sql = """
            DELETE FROM core.fiscal_issuance_readback_reconciliations
            WHERE payment_confirmation_id IN (
                SELECT pc.payment_confirmation_id
                FROM core.payment_confirmations pc
                INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                WHERE pa.parking_session_id = @parking_session_id
            );

            DELETE FROM core.fiscal_issuance_exception_reviews
            WHERE payment_confirmation_id IN (
                SELECT pc.payment_confirmation_id
                FROM core.payment_confirmations pc
                INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                WHERE pa.parking_session_id = @parking_session_id
            );

            DELETE FROM core.fiscal_issuance_attempt_history
            WHERE payment_confirmation_id IN (
                SELECT pc.payment_confirmation_id
                FROM core.payment_confirmations pc
                INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                WHERE pa.parking_session_id = @parking_session_id
            );

            DELETE FROM core.fiscal_issuance_references
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string qualifiedTableName)
    {
        const string sql = "SELECT to_regclass(@table_name) IS NOT NULL;";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", qualifiedTableName);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<long> CountRowsAsync(string qualifiedTableName, Guid paymentConfirmationId)
    {
        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE payment_confirmation_id = @payment_confirmation_id;";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : 0;
    }

    private static async Task<long> ExecuteScalarLongAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : 0;
    }
}
