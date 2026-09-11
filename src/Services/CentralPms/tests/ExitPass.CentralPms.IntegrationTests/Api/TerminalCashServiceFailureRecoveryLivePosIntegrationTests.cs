using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.Common;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Opt-in proof against a task-owned Central database and a separately supplied disposable POS runtime.
/// </summary>
public sealed class TerminalCashServiceFailureRecoveryLivePosIntegrationTests
{
    private const string EnabledVariable = "EXITPASS_TERMINAL_CASH_SERVICE_RECOVERY_LIVE_POS";
    private const string BaseUrlVariable = "EXITPASS_TERMINAL_CASH_SERVICE_RECOVERY_POS_BASE_URL";
    private const string ApiKeyVariable = "EXITPASS_TERMINAL_CASH_SERVICE_RECOVERY_POS_API_KEY";
    private const string PosDatabaseVariable = "EXITPASS_TERMINAL_CASH_SERVICE_RECOVERY_POS_DB";
    private const long AmountMinorUnits = 4_500;
    private static readonly Guid SitePosServerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string SitePosServerRef = "site-pos-server-smoke";

    [Fact]
    public async Task ServiceFailureRecovery_WhenLiveProofEnabled_RecoversOnceAndSecondCallReadsBack()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable)
            ?? throw new InvalidOperationException($"{BaseUrlVariable} is required for the opt-in proof.");
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable)
            ?? throw new InvalidOperationException($"{ApiKeyVariable} is required for the opt-in proof.");
        var posConnectionString = Environment.GetEnvironmentVariable(PosDatabaseVariable)
            ?? throw new InvalidOperationException($"{PosDatabaseVariable} is required for the opt-in proof.");
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var context = PaymentTestContext.Create(nameof(ServiceFailureRecovery_WhenLiveProofEnabled_RecoversOnceAndSecondCallReadsBack));
        var tenderId = Guid.NewGuid();
        var fiscalCorrelationId = Guid.NewGuid();
        var keyFile = Path.Combine(Path.GetTempPath(), $"exitpass-pos-key-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(keyFile, apiKey);
            await EnsureTerminalCashPatchAppliedAsync(connectionString);
            await PaymentTestDataHelper.ResetAndSeedAsync(connectionString, context, "terminal cash service recovery live POS proof");
            await SetTariffAmountAsync(connectionString, context.TariffSnapshotId);
            await SeedSalesInvoiceHeaderProfileAsync(posConnectionString, context.SiteId);

            var clock = new SystemClock();
            var terminalPayments = new TerminalCashPaymentService(
                new TerminalCashPaymentRepository(connectionString),
                clock);
            var payment = await terminalPayments.CreateOrReadAsync(
                new TerminalCashPaymentCommand(
                    tenderId,
                    Guid.NewGuid(),
                    context.ParkingSessionId,
                    context.TariffSnapshotId,
                    context.RequestedByUserId.ToString("D"),
                    $"cashier-session-{Guid.NewGuid():N}",
                    "shift-service-recovery-proof",
                    "terminal-service-recovery-proof",
                    context.SiteId,
                    context.SiteGroupId,
                    SitePosServerId.ToString("D"),
                    "PHP",
                    AmountMinorUnits,
                    AmountMinorUnits,
                    0,
                    DateTimeOffset.UtcNow,
                    [new TerminalCashDenominationEntry("PHP45", AmountMinorUnits, 1)],
                    $"cash-received-{Guid.NewGuid():N}",
                    $"terminal-cash-payment-{tenderId:N}",
                    context.CorrelationId),
                CancellationToken.None);

            var options = CreateOptions(context.SiteId, baseUrl, keyFile);
            Assert.True(options.EvaluateReadiness().IsReady);
            var referenceRepository = new PostgresFiscalIssuanceReferenceRepository(connectionString);
            var orchestration = new FiscalIssuanceOrchestrationService(referenceRepository);
            var mapper = new PosServerFiscalDocumentRequestMapper();
            var hashCalculator = new FiscalSemanticRequestHashCalculator();
            var failureIntegration = new FiscalIssuancePosServerLiveIntegrationService(
                options,
                mapper,
                hashCalculator,
                new PersistenceFailurePosClient(),
                orchestration);
            var exitAuthorization = CreateExitAuthorizationUseCase(connectionString, referenceRepository, clock);
            var audit = new PostgresFiscalExceptionControlledRetryExecutionAuditRepository(connectionString);
            var guard = new PostgresTerminalCashFiscalConflictRecoveryGuardRepository(connectionString);
            var recoveryLock = new PostgresTerminalCashFiscalConflictRecoveryLock(connectionString);
            var acknowledgment = new RecordingVendorAcknowledgmentWorkflow();
            var initialService = CreateService(
                terminalPayments,
                referenceRepository,
                orchestration,
                failureIntegration,
                exitAuthorization,
                options,
                mapper,
                hashCalculator,
                audit,
                guard,
                recoveryLock,
                acknowledgment);

            var failed = await initialService.IssueOrReadAsync(
                new TerminalCashFiscalIssuanceCommand(
                    tenderId,
                    $"terminal-cash-fiscal-{tenderId:N}",
                    fiscalCorrelationId),
                CancellationToken.None);
            Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService, failed.FiscalIssuanceState);
            Assert.Equal("persistence_write_failed", failed.SafeErrorCode);

            var failedReference = await referenceRepository.FindByFiscalIssuanceReferenceIdAsync(
                failed.FiscalIssuanceReferenceId,
                CancellationToken.None);
            Assert.NotNull(failedReference);
            Assert.Equal(FiscalIssuanceErrorPosture.RetryAfterServiceRecovery, failedReference!.LatestErrorPosture);
            Assert.False(string.IsNullOrWhiteSpace(failedReference.SemanticRequestHashValue));

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var liveClient = new RecordingPosClient(
                new HttpPosServerFiscalDocumentClient(httpClient, Options.Create(options)));
            var liveIntegration = new FiscalIssuancePosServerLiveIntegrationService(
                options,
                mapper,
                hashCalculator,
                liveClient,
                orchestration);
            var recoveryService = CreateService(
                terminalPayments,
                referenceRepository,
                orchestration,
                liveIntegration,
                exitAuthorization,
                options,
                mapper,
                hashCalculator,
                audit,
                guard,
                recoveryLock,
                acknowledgment);
            var recoveryCommand = new TerminalCashFiscalConflictRecoveryCommand(
                tenderId,
                failed.FiscalIssuanceReferenceId,
                payment.PaymentAttemptId,
                payment.PaymentConfirmationId,
                context.ParkingSessionId,
                context.TariffSnapshotId,
                AmountMinorUnits,
                "PHP",
                "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
                failedReference.SemanticRequestHashValue!,
                failedReference.UpstreamFinalityReference,
                $"terminal-cash-fiscal-{tenderId:N}",
                context.CorrelationId,
                fiscalCorrelationId,
                fiscalCorrelationId,
                context.RequestedByUserId,
                "AUTHORIZED_FOR_TERMINAL_CASH_SERVICE_FAILURE_RECOVERY_IMPLEMENTATION",
                "persistence_write_failed",
                "The disposable POS persistence condition is corrected and ready.");

            var first = await recoveryService.RecoverServiceFailureAsync(recoveryCommand, CancellationToken.None);
            Assert.True(
                string.Equals(first.RecoveryStatus, "EXECUTED", StringComparison.Ordinal),
                $"Recovery status was {first.RecoveryStatus}; POS code was {first.FiscalIssuance.SafeErrorCode}; posture was {first.FiscalIssuance.SafeErrorPosture}; POS message was {liveClient.LastCreateResult?.Message}.");
            Assert.True(first.RecoveryExecuted);
            Assert.False(first.IdempotentReadback);
            Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, first.FiscalIssuance.FiscalIssuanceState);
            Assert.Equal(FiscalIssuanceResultClassification.NewlyCreated, first.FiscalIssuance.ResultClassification);
            Assert.NotNull(first.FiscalIssuance.PosFiscalDocumentId);
            Assert.NotNull(first.FiscalIssuance.FiscalDocumentNumber);
            Assert.True(first.FiscalIssuance.ExitAuthorizationIssued);

            var countsAfterFirst = await ReadCountsAsync(
                connectionString,
                tenderId,
                payment.PaymentAttemptId,
                payment.PaymentConfirmationId,
                failed.FiscalIssuanceReferenceId,
                context.ParkingSessionId);
            Assert.Equal(new RecoveryCounts(1, 1, 1, 1, 1, 2, 1, 0), countsAfterFirst);

            var second = await recoveryService.RecoverServiceFailureAsync(
                recoveryCommand,
                CancellationToken.None);
            Assert.Equal("ALREADY_COMPLETED", second.RecoveryStatus);
            Assert.False(second.RecoveryExecuted);
            Assert.True(second.IdempotentReadback);
            Assert.Equal(first.FiscalIssuance.PosFiscalDocumentId, second.FiscalIssuance.PosFiscalDocumentId);
            Assert.Equal(first.FiscalIssuance.FiscalDocumentNumber, second.FiscalIssuance.FiscalDocumentNumber);

            var countsAfterSecond = await ReadCountsAsync(
                connectionString,
                tenderId,
                payment.PaymentAttemptId,
                payment.PaymentConfirmationId,
                failed.FiscalIssuanceReferenceId,
                context.ParkingSessionId);
            Assert.Equal(new RecoveryCounts(1, 1, 1, 1, 1, 3, 1, 1), countsAfterSecond);
            Assert.Equal(2, acknowledgment.CallCount);

            Console.WriteLine(
                "TERMINAL_CASH_SERVICE_RECOVERY_LIVE_POS " +
                $"tender={tenderId:D} fiscalReference={failed.FiscalIssuanceReferenceId:D} " +
                $"posDocument={first.FiscalIssuance.PosFiscalDocumentId:D} " +
                $"fiscalNumber={first.FiscalIssuance.FiscalDocumentNumber} " +
                $"semanticHash={failedReference.SemanticRequestHashValue} " +
                $"first={first.RecoveryStatus} second={second.RecoveryStatus} exitAuthorizations={countsAfterSecond.ExitAuthorizations}");
        }
        finally
        {
            if (File.Exists(keyFile))
            {
                File.Delete(keyFile);
            }
        }
    }

    private static TerminalCashFiscalIssuanceService CreateService(
        ITerminalCashPaymentService terminalPayments,
        IFiscalIssuanceReferenceRepository references,
        IFiscalIssuanceOrchestrationService orchestration,
        IFiscalIssuancePosServerLiveIntegrationService posIntegration,
        IIssueExitAuthorizationUseCase exitAuthorization,
        FiscalIssuancePosServerIntegrationOptions options,
        IPosServerFiscalDocumentRequestMapper mapper,
        IFiscalSemanticRequestHashCalculator hashCalculator,
        IFiscalExceptionControlledRetryExecutionAuditRepository audit,
        ITerminalCashFiscalConflictRecoveryGuardRepository guard,
        ITerminalCashFiscalConflictRecoveryLock recoveryLock,
        IVendorPaymentAcknowledgmentWorkflow acknowledgment) =>
        new(
            terminalPayments,
            references,
            orchestration,
            posIntegration,
            NotApplicableStatutoryReader.Instance,
            new ConfiguredSitePosServerBindingResolver(options),
            exitAuthorization,
            NullLogger<TerminalCashFiscalIssuanceService>.Instance,
            options,
            mapper,
            hashCalculator,
            audit,
            guard,
            recoveryLock,
            acknowledgment);

    private static IIssueExitAuthorizationUseCase CreateExitAuthorizationUseCase(
        string connectionString,
        IFiscalIssuanceReferenceRepository references,
        SystemClock clock) =>
        new IssueExitAuthorizationHandler(
            new IssueExitAuthorizationGateway(connectionString, NullLogger<IssueExitAuthorizationGateway>.Instance),
            NoopIntegrationEventPublisher.Instance,
            clock,
            new CentralPmsMetrics(),
            NullLogger<IssueExitAuthorizationHandler>.Instance,
            new ExitAuthorizationFiscalGatingShadowEvaluator(references),
            new ExitAuthorizationPaymentFinalityReadRepository(connectionString),
            new FiscalIssuanceExitAuthorizationGatingOptions(),
            new PaymentFinalityCompletionAuthorityReader(connectionString));

    private static FiscalIssuancePosServerIntegrationOptions CreateOptions(
        Guid siteId,
        string baseUrl,
        string keyFile) =>
        new()
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableLiveFiscalIssuanceFromPaymentFlow = true,
            RuntimeEnvironment = "IntegrationTest",
            TimeoutSeconds = 30,
            Endpoints =
            [
                new SitePosServerEndpointOptions
                {
                    SiteId = siteId,
                    SitePosServerId = SitePosServerId,
                    SitePosServerRef = SitePosServerRef,
                    BaseUrl = new Uri(baseUrl).GetLeftPart(UriPartial.Authority) + "/",
                    ApiKeyFile = keyFile,
                    Environment = "IntegrationTest",
                    Enabled = true,
                    FiscalDocumentTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000101"),
                    FiscalDocumentStatusCodeId = Guid.Parse("10000000-0000-0000-0000-000000000102"),
                    FiscalLineTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000201"),
                    FiscalTenderTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000301"),
                    FiscalTaxTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000401"),
                    FiscalTaxClassificationCodeId = Guid.Parse("10000000-0000-0000-0000-000000000402"),
                    FiscalDiscountPrivilegeTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000501"),
                    FiscalTotalTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000601")
                }
            ]
        };

    private static async Task SetTariffAmountAsync(string connectionString, Guid tariffSnapshotId)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET gross_amount = 45.00,
                net_amount = 45.00,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task EnsureTerminalCashPatchAppliedAsync(string connectionString)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? patchPath = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "infra",
                "db",
                "patches",
                "ExitPass_TerminalCashPaymentCommandReadback_v1.3.sql");
            if (File.Exists(candidate))
            {
                patchPath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        if (patchPath is null)
        {
            throw new FileNotFoundException("Could not locate the terminal cash persistence patch.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(patchPath), connection)
        {
            CommandTimeout = 30
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedSalesInvoiceHeaderProfileAsync(string connectionString, Guid siteId)
    {
        const string sql = """
            UPDATE pos.fiscal_identities
            SET registered_business_name = 'SYNTHETIC RECOVERY MERCHANT',
                registered_business_address = 'SYNTHETIC RECOVERY ADDRESS',
                tin = 'SYNTHETIC-MERCHANT-TIN',
                fiscal_identity_status = 'APPROVED',
                updated_at = NOW(),
                updated_by_ref = 'service-recovery-proof'
            WHERE fiscal_identity_id = '10000000-0000-0000-0000-000000000701';

            DELETE FROM pos.sales_invoice_header_profiles
            WHERE profile_version = 'service-recovery-proof-v1';

            INSERT INTO pos.sales_invoice_header_profiles(
                sales_invoice_header_profile_id, fiscal_identity_id, site_id, site_pos_server_id,
                profile_version, template_version, presentation_version, pos_serial_number,
                machine_identification_number, parking_location_display,
                supplier_developer_registered_name, supplier_developer_address, supplier_developer_tin,
                bir_accreditation_number, bir_accreditation_issued_date, bir_accreditation_valid_until,
                ptu_number, ptu_issued_date, sales_invoice_legal_statement, customer_service_footer,
                effective_from, lifecycle_status, approved_at, approved_by_ref, created_by_ref, updated_by_ref)
            VALUES(
                @profile_id, '10000000-0000-0000-0000-000000000701', @site_id,
                '10000000-0000-0000-0000-000000000001', 'service-recovery-proof-v1',
                'digital-sales-invoice-json-v1', 'digital-sales-invoice-presentation-json-v1',
                'SYN-SERIAL-SERVICE-RECOVERY', 'SYN-MIN-SERVICE-RECOVERY', 'SYNTHETIC RECOVERY SITE',
                'SYNTHETIC SOFTWARE SUPPLIER', 'SYNTHETIC SOFTWARE ADDRESS', 'SYNTHETIC-TIN',
                'SYNTHETIC-ACCREDITATION', CURRENT_DATE - 1, CURRENT_DATE + 365,
                'SYNTHETIC-PTU', CURRENT_DATE - 1, 'SYNTHETIC SALES INVOICE', 'SYNTHETIC SUPPORT',
                NOW() - INTERVAL '1 day', 'APPROVED', NOW() - INTERVAL '1 day',
                'service-recovery-proof', 'service-recovery-proof', 'service-recovery-proof');
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("profile_id", Guid.NewGuid());
        command.Parameters.AddWithValue("site_id", siteId);
        Assert.True(await command.ExecuteNonQueryAsync() >= 1);
    }

    private static async Task<RecoveryCounts> ReadCountsAsync(
        string connectionString,
        Guid tenderId,
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid fiscalReferenceId,
        Guid parkingSessionId)
    {
        const string sql = """
            SELECT
                (SELECT count(*) FROM core.terminal_cash_payment_commands WHERE terminal_cash_tender_id = @tender_id),
                (SELECT count(*) FROM core.payment_attempts WHERE payment_attempt_id = @payment_attempt_id),
                (SELECT count(*) FROM core.payment_confirmations WHERE payment_confirmation_id = @payment_confirmation_id),
                (SELECT count(*) FROM core.fiscal_issuance_references WHERE fiscal_issuance_reference_id = @fiscal_reference_id),
                (SELECT count(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id),
                (SELECT count(*) FROM core.fiscal_issuance_retry_execution_attempts WHERE fiscal_issuance_reference_id = @fiscal_reference_id),
                (SELECT count(*) FROM core.fiscal_issuance_retry_execution_attempts WHERE fiscal_issuance_reference_id = @fiscal_reference_id AND execution_status = 'EXECUTED'),
                (SELECT count(*) FROM core.fiscal_issuance_retry_execution_attempts WHERE fiscal_issuance_reference_id = @fiscal_reference_id AND execution_status = 'REPLAY_MATCHED');
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tender_id", tenderId);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        command.Parameters.AddWithValue("fiscal_reference_id", fiscalReferenceId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RecoveryCounts(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7));
    }

    private sealed record RecoveryCounts(
        long TerminalCashCommands,
        long PaymentAttempts,
        long PaymentConfirmations,
        long FiscalReferences,
        long ExitAuthorizations,
        long RecoveryAudits,
        long ExecutedAudits,
        long ReplayMatchedAudits);

    private sealed class PersistenceFailurePosClient : IPosServerFiscalDocumentClient
    {
        public Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
            PosServerFiscalDocumentCreateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PosServerFiscalDocumentCreateResult(
                PosServerFiscalDocumentOutcome.FailedService,
                false,
                503,
                "persistence_write_failed",
                "Synthetic corrected-service precondition.",
                null, null, null, FiscalNumberAssignmentState.NotAssigned,
                null, null, null, null, null, null, null, null, null, null,
                FiscalIssuanceErrorPosture.RetryAfterServiceRecovery));

        public Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
            Guid fiscalDocumentId, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
            Guid fiscalDocumentId, Guid? correlationId, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
            Guid fiscalDocumentId, PosServerFiscalDocumentVoidRequest request, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPosClient(IPosServerFiscalDocumentClient inner) : IPosServerFiscalDocumentClient
    {
        public PosServerFiscalDocumentCreateResult? LastCreateResult { get; private set; }

        public async Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
            PosServerFiscalDocumentCreateRequest request,
            CancellationToken cancellationToken)
        {
            LastCreateResult = await inner.CreateFiscalDocumentAsync(request, cancellationToken);
            return LastCreateResult;
        }

        public Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
            Guid fiscalDocumentId, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            inner.GetFiscalDocumentAsync(fiscalDocumentId, routingContext, cancellationToken);

        public Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
            Guid fiscalDocumentId, Guid? correlationId, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            inner.GetFiscalDocumentPresentationAsync(fiscalDocumentId, correlationId, routingContext, cancellationToken);

        public Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
            Guid fiscalDocumentId, PosServerFiscalDocumentVoidRequest request, PosServerRoutingContext routingContext, CancellationToken cancellationToken) =>
            inner.VoidFiscalDocumentAsync(fiscalDocumentId, request, routingContext, cancellationToken);
    }

    private sealed class NotApplicableStatutoryReader : ITerminalCashStatutoryFiscalLinkageReader
    {
        public static readonly NotApplicableStatutoryReader Instance = new();

        public Task<TerminalCashStatutoryFiscalLinkageResult> ReadByAppliedTariffSnapshotAsync(
            TerminalCashPaymentReadback cashPayment,
            CancellationToken cancellationToken) =>
            Task.FromResult(TerminalCashStatutoryFiscalLinkageResult.NotApplicable());
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public static readonly NoopIntegrationEventPublisher Instance = new();

        public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingVendorAcknowledgmentWorkflow : IVendorPaymentAcknowledgmentWorkflow
    {
        public int CallCount { get; private set; }

        public Task ProcessAsync(
            VendorPaymentAcknowledgmentWorkflowCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
