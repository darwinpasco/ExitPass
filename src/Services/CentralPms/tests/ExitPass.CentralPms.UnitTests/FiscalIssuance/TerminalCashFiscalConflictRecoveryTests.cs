using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class TerminalCashFiscalConflictRecoveryTests
{
    private static readonly Guid TenderId = Guid.Parse("31000000-0000-4000-8000-000000000001");
    private static readonly Guid AttemptId = Guid.Parse("31000000-0000-4000-8000-000000000002");
    private static readonly Guid ConfirmationId = Guid.Parse("31000000-0000-4000-8000-000000000003");
    private static readonly Guid SessionId = Guid.Parse("31000000-0000-4000-8000-000000000004");
    private static readonly Guid TariffId = Guid.Parse("31000000-0000-4000-8000-000000000005");
    private static readonly Guid SiteId = Guid.Parse("31000000-0000-4000-8000-000000000006");
    private static readonly Guid SiteGroupId = Guid.Parse("31000000-0000-4000-8000-000000000007");
    private static readonly Guid PosId = Guid.Parse("31000000-0000-4000-8000-000000000008");
    private static readonly Guid ReferenceId = Guid.Parse("31000000-0000-4000-8000-000000000009");
    private static readonly Guid TransactionCorrelation = Guid.Parse("31000000-0000-4000-8000-000000000010");
    private static readonly Guid FiscalCorrelation = Guid.Parse("31000000-0000-4000-8000-000000000011");
    private static readonly Guid ActorId = Guid.Parse("31000000-0000-4000-8000-000000000012");
    private static readonly Guid DocumentId = Guid.Parse("31000000-0000-4000-8000-000000000013");
    private const string PosRef = "site-pos-server-recovery";
    private const string SemanticHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const long AmountMinorUnits = 4_500;

    [Fact]
    public async Task Recover_WhenOnlyReportingPeriodConflictRemains_ReusesReferenceAndExecutesOnce()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default);

        Assert.True(result.RecoveryExecuted);
        Assert.False(result.IdempotentReadback);
        Assert.Equal(ReferenceId, result.FiscalIssuance.FiscalIssuanceReferenceId);
        Assert.Equal(DocumentId, result.FiscalIssuance.PosFiscalDocumentId);
        Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, result.FiscalIssuance.FiscalIssuanceState);
        await fixture.PosIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            ReferenceId,
            Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
            Arg.Is<PosServerCreateResultRecordingContext>(value =>
                value.CorrelationId == FiscalCorrelation && value.ServiceIdentityId == ActorId),
            Arg.Any<CancellationToken>());
        await fixture.AuditRepository.Received(2).RecordAsync(
            Arg.Is<FiscalExceptionControlledRetryExecutionAttemptWrite>(value =>
                value.FiscalIssuanceReferenceId == ReferenceId &&
                value.SemanticRequestHashValue == SemanticHash &&
                value.UpstreamFinalityReference == UpstreamReference() &&
                value.SafeSummary.Contains("approval=authorized-test-approval", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recover_WhenAssignmentMismatchRemains_ReusesSameGovernedObligation()
    {
        const string code = "fiscal_reporting_period_assignment_mismatch";
        var reference = Reference() with
        {
            LatestErrorCode = code,
            LatestExceptionReason = FiscalIssuanceExceptionReason.FiscalReportingPeriodAssignmentMismatch,
            LatestErrorPosture = FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
        };
        var fixture = CreateFixture(reference);

        var result = await fixture.Service.RecoverReportingPeriodConflictAsync(
            Command() with { ReasonCode = code },
            default);

        Assert.True(result.RecoveryExecuted);
        Assert.Equal(ReferenceId, result.FiscalIssuance.FiscalIssuanceReferenceId);
        await fixture.PosIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            ReferenceId,
            Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
            Arg.Any<PosServerCreateResultRecordingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("fiscal_document_idempotency_conflict")]
    [InlineData("semantic_request_hash_mismatch")]
    [InlineData("amount_mismatch")]
    [InlineData("currency_mismatch")]
    [InlineData("fiscal_reference_mismatch")]
    public async Task Recover_WhenConflictIsSemantic_RemainsTerminal(string errorCode)
    {
        var fixture = CreateFixture(Reference() with { LatestErrorCode = errorCode });

        var error = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(() =>
            fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default));

        Assert.Equal("TERMINAL_CASH_FISCAL_RECOVERY_REASON_NOT_APPROVED", error.ErrorCode);
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task Recover_WhenReportingPeriodStillUnavailable_RemainsRecoverableWithoutFiscalEvidence()
    {
        var stillBlocked = Reference() with
        {
            FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
            LatestErrorPosture = FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
        };
        var posResult = FailedReportingPeriodResult();
        var fixture = CreateFixture(posResultReference: stillBlocked, posResult: posResult);

        var result = await fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default);

        Assert.True(result.RecoveryExecuted);
        Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, result.FiscalIssuance.FiscalIssuanceState);
        Assert.Equal("fiscal_reporting_period_unavailable", result.FiscalIssuance.SafeErrorCode);
        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs().ExecuteAsync(null!, default);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("confirmation")]
    [InlineData("delivery-hash")]
    [InlineData("delivery-key")]
    [InlineData("correlation")]
    [InlineData("reason")]
    public async Task Recover_WhenExpectedDurableFactDiffers_FailsClosedBeforePos(string mismatch)
    {
        var fixture = CreateFixture();
        var command = Command();
        command = mismatch switch
        {
            "hash" => command with { ExpectedFiscalSemanticRequestHash = new string('b', 64) },
            "amount" => command with { ExpectedAmountMinorUnits = AmountMinorUnits + 1 },
            "currency" => command with { ExpectedCurrency = "USD" },
            "confirmation" => command with { ExpectedPaymentConfirmationId = Guid.NewGuid() },
            "delivery-hash" => command with { ExpectedDeliveryRequestHash = new string('c', 64) },
            "delivery-key" => command with { DeliveryIdempotencyKey = "different-key" },
            "correlation" => command with { RecoveryCorrelationId = Guid.NewGuid() },
            "reason" => command with { ReasonCode = "different_conflict" },
            _ => command
        };

        await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(() =>
            fixture.Service.RecoverReportingPeriodConflictAsync(command, default));
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task Recover_WhenExitAuthorizationAlreadyExists_DeniesPosSubmission()
    {
        var fixture = CreateFixture(exitAuthorizationCount: 1);

        var error = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(() =>
            fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default));

        Assert.Equal("TERMINAL_CASH_FISCAL_RECOVERY_EXIT_ALREADY_AUTHORIZED", error.ErrorCode);
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task Recover_WhenReferenceContainsUnexpectedFiscalEvidence_DeniesPosSubmission()
    {
        var fixture = CreateFixture(Reference() with { PosServerFiscalDocumentId = Guid.NewGuid() });

        var error = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(() =>
            fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default));

        Assert.Equal("TERMINAL_CASH_FISCAL_RECOVERY_EXISTING_EVIDENCE_AMBIGUOUS", error.ErrorCode);
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task Recover_WhenAlreadyCompleted_PerformsIdempotentReadbackWithoutPosSubmission()
    {
        var completed = RecordedReference(FiscalIssuanceResultClassification.IdempotentReplay);
        var fixture = CreateFixture(completed);

        var result = await fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default);

        Assert.False(result.RecoveryExecuted);
        Assert.True(result.IdempotentReadback);
        Assert.Equal(DocumentId, result.FiscalIssuance.PosFiscalDocumentId);
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    [Fact]
    public async Task Recover_WhenConcurrentLeaseIsHeld_DeniesSecondEffectiveRetry()
    {
        var fixture = CreateFixture(lockAvailable: false);

        var error = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(() =>
            fixture.Service.RecoverReportingPeriodConflictAsync(Command(), default));

        Assert.Equal("TERMINAL_CASH_FISCAL_RECOVERY_IN_PROGRESS", error.ErrorCode);
        await fixture.PosIntegration.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default, null!, null!, default);
    }

    private static Fixture CreateFixture(
        FiscalIssuanceReferenceRecord? reference = null,
        int exitAuthorizationCount = 0,
        bool lockAvailable = true,
        FiscalIssuanceReferenceRecord? posResultReference = null,
        PosServerFiscalDocumentCreateResult? posResult = null)
    {
        reference ??= Reference();
        posResult ??= SuccessfulPosResult();
        posResultReference ??= RecordedReference(posResult.ResultClassification ?? FiscalIssuanceResultClassification.NewlyCreated);
        var cashPayment = CashPayment();

        var terminalPayments = Substitute.For<ITerminalCashPaymentService>();
        terminalPayments.GetByTerminalCashTenderIdAsync(TenderId, Arg.Any<CancellationToken>())
            .Returns(cashPayment);
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        references.FindByFiscalIssuanceReferenceIdAsync(ReferenceId, Arg.Any<CancellationToken>())
            .Returns(reference);
        var posIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        posIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                ReferenceId,
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.Applied(
                MappedRequest(), posResult, posResultReference));
        var statutory = Substitute.For<ITerminalCashStatutoryFiscalLinkageReader>();
        statutory.ReadByAppliedTariffSnapshotAsync(cashPayment, Arg.Any<CancellationToken>())
            .Returns(TerminalCashStatutoryFiscalLinkageResult.NotApplicable());
        var binding = Substitute.For<ISitePosServerBindingResolver>();
        var options = Options();
        binding.Resolve(Arg.Any<SitePosServerBindingRequest>())
            .Returns(SitePosServerBindingResolution.Success(options.Endpoints.Single()));
        var exitAuthorization = Substitute.For<IIssueExitAuthorizationUseCase>();
        exitAuthorization.ExecuteAsync(Arg.Any<IssueExitAuthorizationCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<IssueExitAuthorizationCommand>();
                var now = DateTimeOffset.UtcNow;
                return new IssueExitAuthorizationResult(
                    Guid.NewGuid(), request.ParkingSessionId, request.PaymentAttemptId,
                    "token", "ISSUED", now, now.AddMinutes(15));
            });
        var hashCalculator = Substitute.For<IFiscalSemanticRequestHashCalculator>();
        hashCalculator.Calculate(Arg.Any<PosServerFiscalDocumentCreateRequest>())
            .Returns(HashResult());
        var auditRepository = Substitute.For<IFiscalExceptionControlledRetryExecutionAuditRepository>();
        auditRepository.RecordAsync(
                Arg.Any<FiscalExceptionControlledRetryExecutionAttemptWrite>(),
                Arg.Any<CancellationToken>())
            .Returns(call => AuditRecord(call.Arg<FiscalExceptionControlledRetryExecutionAttemptWrite>()));
        var guard = Substitute.For<ITerminalCashFiscalConflictRecoveryGuardRepository>();
        guard.ReadAsync(AttemptId, ConfirmationId, Arg.Any<CancellationToken>())
            .Returns(new TerminalCashFiscalConflictRecoveryFacts(
                AttemptId, ConfirmationId, SessionId, TariffId, "CONFIRMED", "RECORDED",
                "PHP", AmountMinorUnits, "PHP", AmountMinorUnits, exitAuthorizationCount));
        var recoveryLock = Substitute.For<ITerminalCashFiscalConflictRecoveryLock>();
        recoveryLock.TryAcquireAsync(ReferenceId, Arg.Any<CancellationToken>())
            .Returns(lockAvailable ? new NoopLease() : null);

        var service = new TerminalCashFiscalIssuanceService(
            terminalPayments,
            references,
            Substitute.For<IFiscalIssuanceOrchestrationService>(),
            posIntegration,
            statutory,
            binding,
            exitAuthorization,
            NullLogger<TerminalCashFiscalIssuanceService>.Instance,
            options,
            new PosServerFiscalDocumentRequestMapper(),
            hashCalculator,
            auditRepository,
            guard,
            recoveryLock,
            Substitute.For<IVendorPaymentAcknowledgmentWorkflow>());
        return new Fixture(service, posIntegration, auditRepository, exitAuthorization);
    }

    private static TerminalCashFiscalConflictRecoveryCommand Command() =>
        new(
            TenderId,
            ReferenceId,
            AttemptId,
            ConfirmationId,
            SessionId,
            TariffId,
            AmountMinorUnits,
            "PHP",
            "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            SemanticHash,
            UpstreamReference(),
            $"terminal-cash-fiscal-{TenderId:N}",
            TransactionCorrelation,
            FiscalCorrelation,
            FiscalCorrelation,
            ActorId,
            "authorized-test-approval",
            "fiscal_reporting_period_unavailable",
            "The governed reporting period is now ready.");

    private static TerminalCashPaymentReadback CashPayment() =>
        new(
            Guid.NewGuid(), TenderId, AttemptId, Guid.NewGuid(), SessionId, TariffId, "terminal-01",
            SiteId, SiteGroupId, PosId.ToString("D"), Guid.NewGuid().ToString("D"), "shift-01", "PHP",
            AmountMinorUnits, AmountMinorUnits, 0, "CONFIRMED", ConfirmationId, "CREATED",
            "terminal-cash-payment:test", "terminal-cash-payment:sha256:v1",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4),
            DateTimeOffset.UtcNow.AddMinutes(-3), TransactionCorrelation, "NOT_STARTED_IN_THIS_SLICE");

    private static FiscalIssuanceReferenceRecord Reference() =>
        new(
            FiscalIssuanceReferenceId: ReferenceId,
            PaymentConfirmationId: ConfirmationId,
            PaymentAttemptId: AttemptId,
            ParkingSessionId: SessionId,
            TariffSnapshotId: TariffId,
            SiteId: SiteId,
            SitePosServerId: PosId,
            SitePosServerRef: PosRef,
            PayableBasisRef: TariffId.ToString("D"),
            UpstreamFinalityReference: UpstreamReference(),
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
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
            LatestExceptionReason: FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
            LatestErrorCode: "fiscal_reporting_period_unavailable",
            LatestErrorPosture: FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
            CorrelationId: FiscalCorrelation,
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            RecordedByServiceIdentityId: ActorId,
            FiscalDocumentTypeCodeId: Options().Endpoints.Single().FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: "sales_invoice",
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: SemanticHash,
            SemanticRequestHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount: 12,
            SemanticRequestHashSafeSummary: "semantic_request_hash_source_available",
            SemanticRequestHashRecordedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletionBasis: FiscalCompletionBasisCodes.PaymentFinality,
            CompletionAuthorityReferenceId: ConfirmationId);

    private static FiscalIssuanceReferenceRecord RecordedReference(FiscalIssuanceResultClassification classification) =>
        Reference() with
        {
            PosServerFiscalDocumentId = DocumentId,
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 5,
            FiscalDocumentNumber = "SI-000005",
            FiscalSeries = "SI",
            FiscalNumberPrefixText = "SI-",
            FiscalNumberAssignedAt = DateTimeOffset.UtcNow,
            FiscalNumberAssignedByRef = "pos-server",
            FiscalDocumentStatusCodeId = Options().Endpoints.Single().FiscalDocumentStatusCodeId,
            ResultClassification = classification,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceState = classification == FiscalIssuanceResultClassification.IdempotentReplay
                ? FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                : FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            LatestExceptionReason = null,
            LatestErrorCode = null,
            LatestErrorPosture = null
        };

    private static PosServerFiscalDocumentCreateResult SuccessfulPosResult() =>
        new(
            PosServerFiscalDocumentOutcome.Accepted, true, 202, "accepted", "created", DocumentId,
            FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState.Assigned, Guid.NewGuid(),
            Options().Endpoints.Single().FiscalDocumentStatusCodeId, Guid.NewGuid(), 5, "SI-000005", "SI", "SI-", null,
            DateTimeOffset.UtcNow, "pos-server", null);

    private static PosServerFiscalDocumentCreateResult FailedReportingPeriodResult() =>
        new(
            PosServerFiscalDocumentOutcome.FailedConfiguration, false, 409,
            "fiscal_reporting_period_unavailable", "not ready", null, null, null,
            FiscalNumberAssignmentState.NotAssigned, null, null, null, null, null, null, null, null, null, null,
            FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection);

    private static PosServerFiscalDocumentCreateRequest MappedRequest() =>
        new PosServerFiscalDocumentRequestMapper().Map(new CentralPmsFiscalDocumentMappingContext(
            PosId, PosRef, Options().Endpoints.Single().FiscalDocumentTypeCodeId, "sales_invoice",
            Options().Endpoints.Single().FiscalDocumentStatusCodeId, DateOnly.FromDateTime(DateTime.UtcNow),
            SessionId.ToString("D"), AttemptId.ToString("D"), ConfirmationId.ToString("D"),
            new CentralPmsPayableBasisContext(TariffId.ToString("D"), UpstreamReference(), "PHP", AmountMinorUnits, [], new Dictionary<string, string>()),
            [new CentralPmsFiscalDocumentLineContext(1, Options().Endpoints.Single().FiscalLineTypeCodeId, "Parking", 1, AmountMinorUnits, AmountMinorUnits, 0, 0, AmountMinorUnits, "PHP", null, TariffId.ToString("D"), new Dictionary<string, string>())],
            [new CentralPmsFiscalTenderContext(Options().Endpoints.Single().FiscalTenderTypeCodeId, AmountMinorUnits, "PHP", AttemptId.ToString("D"), ConfirmationId.ToString("D"), UpstreamReference(), "CASH", new Dictionary<string, string>())],
            [], [],
            [new CentralPmsFiscalTotalContext(Options().Endpoints.Single().FiscalTotalTypeCodeId, AmountMinorUnits, "PHP", new Dictionary<string, string>())],
            new Dictionary<string, string>(), UpstreamReference(), null));

    private static FiscalSemanticRequestHashResult HashResult() =>
        new(
            FiscalSemanticRequestHashSourceStatus.Available,
            SemanticHash,
            FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            12,
            "semantic_request_hash_source_available",
            null);

    private static FiscalIssuancePosServerIntegrationOptions Options()
    {
        var values = Enumerable.Range(20, 8)
            .Select(value => Guid.Parse($"31000000-0000-4000-8000-{value:D12}"))
            .ToArray();
        return new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableLiveFiscalIssuanceFromPaymentFlow = true,
            RuntimeEnvironment = "Test",
            Endpoints =
            [
                new SitePosServerEndpointOptions
                {
                    SiteId = SiteId,
                    SitePosServerId = PosId,
                    SitePosServerRef = PosRef,
                    BaseUrl = "http://pos-server.test/",
                    ApiKeyFile = "test-pos-key",
                    Environment = "Test",
                    Enabled = true,
                    FiscalDocumentTypeCodeId = values[0],
                    FiscalDocumentStatusCodeId = values[1],
                    FiscalLineTypeCodeId = values[2],
                    FiscalTenderTypeCodeId = values[3],
                    FiscalTaxTypeCodeId = values[4],
                    FiscalTaxClassificationCodeId = values[5],
                    FiscalDiscountPrivilegeTypeCodeId = values[6],
                    FiscalTotalTypeCodeId = values[7]
                }
            ]
        };
    }

    private static FiscalExceptionControlledRetryExecutionAttemptRecord AuditRecord(
        FiscalExceptionControlledRetryExecutionAttemptWrite value) =>
        new(
            Guid.NewGuid(), value.FiscalIssuanceReferenceId, value.RetryCommandPreparationAttemptId,
            value.RetrySchedulePreparationAttemptId, value.ReadbackClassificationBasis,
            value.SemanticRequestHashValue, value.SemanticRequestHashAlgorithm,
            value.SemanticRequestHashSourceVersion, value.UpstreamFinalityReference,
            value.ExecutionStatus, value.BlockReasonCode, value.PosServerOutcome,
            value.PosServerResultClassification, value.PosServerFiscalDocumentId,
            value.FiscalDocumentNumber, value.FiscalIdentityId, value.FiscalSequencePolicyId,
            value.FiscalSequenceValue, value.FiscalSeries, value.FiscalNumberPrefixText,
            value.FiscalNumberSuffixText, value.FiscalNumberAssignedAt, value.FiscalNumberAssignedByRef,
            value.AttemptedAt, value.CompletedAt, value.ServiceIdentityId, value.CorrelationId,
            value.SafeSummary, DateTimeOffset.UtcNow);

    private static string UpstreamReference() =>
        $"terminal-cash-payment-confirmation:{ConfirmationId:D}:sales_invoice";

    private sealed record Fixture(
        TerminalCashFiscalIssuanceService Service,
        IFiscalIssuancePosServerLiveIntegrationService PosIntegration,
        IFiscalExceptionControlledRetryExecutionAuditRepository AuditRepository,
        IIssueExitAuthorizationUseCase ExitAuthorization);

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
