using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionControlledRetryExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabledByDefault_DoesNotCallPosServerPath()
    {
        var scenario = await ReadyScenarioAsync();
        var liveIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var audit = new FakeExecutionAuditRepository();
        var sut = CreateSut(liveIntegration: liveIntegration, auditRepository: audit);

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Disabled);
        result.BlockReasonCode.Should().Be("controlled_retry_execution_disabled");
        result.PosServerPostCalled.Should().BeFalse();
        result.RetryExecutionAvailable.Should().BeFalse();
        audit.Records.Should().BeEmpty();
        await liveIntegration.DidNotReceiveWithAnyArgs()
            .TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenReadbackAttemptMissing_BlocksWithoutCallingPosServer()
    {
        var scenario = await ReadyScenarioAsync(readbackAttemptCount: null);
        var liveIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var audit = new FakeExecutionAuditRepository();
        var sut = CreateSut(EnabledOptions(), liveIntegration, audit);

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("readback_attempt_history_missing");
        result.PosServerPostCalled.Should().BeFalse();
        audit.Records.Should().ContainSingle();
        await liveIntegration.DidNotReceiveWithAnyArgs()
            .TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData(FiscalExceptionReadbackClassification.Matched, "readback_matched")]
    [InlineData(FiscalExceptionReadbackClassification.Mismatch, "readback_mismatch")]
    [InlineData(FiscalExceptionReadbackClassification.Failed, "readback_failed")]
    [InlineData(FiscalExceptionReadbackClassification.Unavailable, "readback_unavailable")]
    [InlineData(FiscalExceptionReadbackClassification.Unknown, "readback_unknown")]
    [InlineData(FiscalExceptionReadbackClassification.IdentifierMissing, "readback_identifier_missing")]
    [InlineData(FiscalExceptionReadbackClassification.NotSupportedYet, "readback_not_supported_yet")]
    public async Task ExecuteAsync_WhenReadbackIsNotNotFound_Blocks(
        FiscalExceptionReadbackClassification classification,
        string expectedReason)
    {
        var scenario = await ReadyScenarioAsync(readbackClassification: classification);
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be(expectedReason);
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetryEligibilityIsNotEligible_Blocks()
    {
        var scenario = await ReadyScenarioAsync(referenceState: FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest);
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().NotBeNullOrWhiteSpace();
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandPreparationMissing_Blocks()
    {
        var scenario = await ReadyScenarioAsync();
        var request = scenario.Request with
        {
            CommandPreparation = scenario.Request.CommandPreparation with
            {
                Status = FiscalExceptionRetryCommandPreparationStatus.Blocked,
                BlockReasonCode = "retry_command_preparation_not_safe",
                Command = null
            }
        };
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_command_preparation_not_safe");
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSchedulingPreparationMissing_Blocks()
    {
        var scenario = await ReadyScenarioAsync();
        var request = scenario.Request with
        {
            SchedulingPreparation = scenario.Request.SchedulingPreparation with
            {
                Status = FiscalExceptionRetrySchedulingPreparationStatus.Blocked,
                BlockReasonCode = "retry_scheduling_preparation_missing",
                Schedule = null
            }
        };
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_scheduling_preparation_missing");
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionPreparationNotReady_Blocks()
    {
        var scenario = await ReadyScenarioAsync();
        var request = scenario.Request with
        {
            ExecutionPreparation = scenario.Request.ExecutionPreparation with
            {
                Status = FiscalExceptionRetryExecutionPreparationStatus.Blocked,
                BlockReasonCode = "retry_execution_preparation_not_ready"
            }
        };
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_execution_preparation_not_ready");
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSemanticHashDoesNotMatchOriginalFacts_Blocks()
    {
        var scenario = await ReadyScenarioAsync();
        var request = scenario.Request with
        {
            FiscalContext = scenario.FiscalContext with
            {
                PayableBasis = scenario.FiscalContext.PayableBasis with
                {
                    PayableAmountMinorUnits = scenario.FiscalContext.PayableBasis.PayableAmountMinorUnits + 1
                }
            }
        };
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_request_hash_mismatch");
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNewUpstreamFinalityReferenceRequested_Blocks()
    {
        var scenario = await ReadyScenarioAsync();
        var request = scenario.Request with { RequestedUpstreamFinalityReference = "new-finality-ref" };
        var sut = CreateSut(EnabledOptions(), auditRepository: new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Blocked);
        result.BlockReasonCode.Should().Be("new_upstream_finality_reference_rejected");
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabledAndAllGatesPass_CallsPosServerPathOnceAndPersistsAudit()
    {
        var scenario = await ReadyScenarioAsync();
        var posResult = CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated);
        var liveIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                scenario.Request.Detail.Summary.FiscalIssuanceReferenceId,
                scenario.FiscalContext,
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.Applied(
                scenario.MappedRequest,
                posResult,
                scenario.Reference with
                {
                    FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                }));
        var audit = new FakeExecutionAuditRepository();
        var sut = CreateSut(EnabledOptions(), liveIntegration, audit);

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Executed);
        result.PosServerPostCalled.Should().BeTrue();
        result.RetryExecuted.Should().BeTrue();
        result.FiscalReferenceSuccessRecorded.Should().BeTrue();
        result.RetryExecutionAvailable.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        audit.Records.Should().ContainSingle(record =>
            record.ExecutionStatus == FiscalExceptionControlledRetryExecutionStatus.Executed &&
            record.PosServerFiscalDocumentId == posResult.FiscalDocumentId);
        await liveIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            scenario.Request.Detail.Summary.FiscalIssuanceReferenceId,
            scenario.FiscalContext,
            Arg.Is<PosServerCreateResultRecordingContext>(context =>
                context.UpstreamFinalityReference == scenario.Request.Detail.Summary.UpstreamFinalityReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenReplayResponseReturned_RecordsReplayMatched()
    {
        var scenario = await ReadyScenarioAsync();
        var posResult = CompletePosServerCreateResult(FiscalIssuanceResultClassification.IdempotentReplay);
        var liveIntegration = LiveIntegrationReturning(
            scenario,
            posResult,
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed);
        var sut = CreateSut(EnabledOptions(), liveIntegration, new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.ReplayMatched);
        result.FiscalReferenceSuccessRecorded.Should().BeTrue();
        await liveIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            Arg.Any<Guid>(),
            Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
            Arg.Any<PosServerCreateResultRecordingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdempotencyConflictReturned_DoesNotLoop()
    {
        var scenario = await ReadyScenarioAsync();
        var posResult = FailurePosServerCreateResult(
            PosServerFiscalDocumentOutcome.Conflict,
            "fiscal_document_idempotency_conflict",
            FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange);
        var liveIntegration = LiveIntegrationReturning(
            scenario,
            posResult,
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict);
        var audit = new FakeExecutionAuditRepository();
        var sut = CreateSut(EnabledOptions(), liveIntegration, audit);

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Conflict);
        result.BlockReasonCode.Should().Be("pos_server_idempotency_conflict");
        result.FiscalReferenceSuccessRecorded.Should().BeFalse();
        audit.Records.Should().ContainSingle(record =>
            record.ExecutionStatus == FiscalExceptionControlledRetryExecutionStatus.Conflict);
        await liveIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            Arg.Any<Guid>(),
            Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
            Arg.Any<PosServerCreateResultRecordingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPosServerOutcomeUnknown_DoesNotRetryAgain()
    {
        var scenario = await ReadyScenarioAsync();
        var posResult = FailurePosServerCreateResult(
            PosServerFiscalDocumentOutcome.FailedService,
            "pos_server_timeout",
            FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
        var liveIntegration = LiveIntegrationReturning(
            scenario,
            posResult,
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var sut = CreateSut(EnabledOptions(), liveIntegration, new FakeExecutionAuditRepository());

        var result = await sut.ExecuteAsync(scenario.Request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.Unknown);
        result.SafeSummary.Should().Contain("requires_readback");
        await liveIntegration.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            Arg.Any<Guid>(),
            Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
            Arg.Any<PosServerCreateResultRecordingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenDryRun_DoesNotMutateOrCallPosServer()
    {
        var scenario = await ReadyScenarioAsync();
        var liveIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var request = scenario.Request with { DryRunOnly = true };
        var audit = new FakeExecutionAuditRepository();
        var sut = CreateSut(EnabledOptions(), liveIntegration, audit);

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionControlledRetryExecutionStatus.DryRunReady);
        result.PosServerPostCalled.Should().BeFalse();
        result.FiscalReferenceSuccessRecorded.Should().BeFalse();
        audit.Records.Should().ContainSingle(record =>
            record.ExecutionStatus == FiscalExceptionControlledRetryExecutionStatus.DryRunReady);
        await liveIntegration.DidNotReceiveWithAnyArgs()
            .TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public void ControlledRetryExecution_DoesNotIntroduceBatchEndpointSchedulerOrGateDependency()
    {
        var fiscalIssuanceTypes = typeof(FiscalExceptionControlledRetryExecutionService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionControlledRetryExecutionService).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryExecutionJob", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryBatch", StringComparison.OrdinalIgnoreCase));

        typeof(FiscalExceptionControlledRetryExecutionRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(name => name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Gate", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalExceptionControlledRetryExecutionService CreateSut(
        FiscalExceptionControlledRetryExecutionOptions? options = null,
        IFiscalIssuancePosServerLiveIntegrationService? liveIntegration = null,
        IFiscalExceptionControlledRetryExecutionAuditRepository? auditRepository = null) =>
        new(
            options ?? new FiscalExceptionControlledRetryExecutionOptions(),
            new PosServerFiscalDocumentRequestMapper(),
            new FiscalSemanticRequestHashCalculator(),
            liveIntegration ?? Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>(),
            auditRepository);

    private static FiscalExceptionControlledRetryExecutionOptions EnabledOptions() =>
        new() { EnableControlledRetryExecution = true };

    private static async Task<ReadyScenario> ReadyScenarioAsync(
        FiscalExceptionReadbackClassification readbackClassification = FiscalExceptionReadbackClassification.NotFound,
        int? readbackAttemptCount = 1,
        FiscalIssuanceIntegrationState referenceState = FiscalIssuanceIntegrationState.FiscalIssuanceUnknown)
    {
        var mapper = new PosServerFiscalDocumentRequestMapper();
        var hashCalculator = new FiscalSemanticRequestHashCalculator();
        var fiscalContext = PosServerFiscalDocumentRequestMapperTests.ValidContext();
        var mappedRequest = mapper.Map(fiscalContext);
        var hash = hashCalculator.Calculate(mappedRequest);
        var reference = Reference(referenceState, fiscalContext, hash);
        var readback = new FakeReadbackAttemptRepository();
        if (readbackAttemptCount is not null)
        {
            readback.Seed(new FiscalExceptionReadbackAttemptSummary(
                Classification: readbackClassification,
                AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                AttemptCount: readbackAttemptCount.Value,
                SafeErrorSummary: "readback_basis"));
        }

        var detail = await new FiscalExceptionQueueService(
                new FakeReferenceReader([reference]),
                readback)
            .GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        var commandPreparation = await new FiscalExceptionRetryCommandPreparationService(
                new FakeRetryCommandPreparationAuditRepository())
            .PrepareAsync(new FiscalExceptionRetryCommandPreparationRequest(detail!), CancellationToken.None);
        var schedulingPreparation = await new FiscalExceptionRetrySchedulingPreparationService(
                new FiscalExceptionRetrySchedulingPreparationOptions(
                    EnableSchedulePreparation: true,
                    RetrySchedulePolicyConfigured: true,
                    RetryBackoffPolicyConfigured: true),
                new FakeRetrySchedulingPreparationAuditRepository())
            .PrepareAsync(
                new FiscalExceptionRetrySchedulingPreparationRequest(detail!, commandPreparation),
                CancellationToken.None);
        var executionPreparation = await new FiscalExceptionRetryExecutionPreparationService(
                ReadyExecutionPreparationOptions())
            .EvaluateAsync(
                new FiscalExceptionRetryExecutionPreparationRequest(
                    detail!,
                    commandPreparation,
                    schedulingPreparation,
                    ReadyPosServerRetryContractReadiness()),
                CancellationToken.None);

        return new ReadyScenario(
            reference,
            fiscalContext,
            mappedRequest,
            new FiscalExceptionControlledRetryExecutionRequest(
                detail!,
                commandPreparation,
                schedulingPreparation,
                executionPreparation,
                fiscalContext,
                ServiceIdentityId: Guid.NewGuid(),
                ApprovalReference: "approval-ref",
                DualControlReference: "dual-control-ref",
                CorrelationId: detail!.CorrelationId));
    }

    private static FiscalExceptionRetryExecutionPreparationOptions ReadyExecutionPreparationOptions() =>
        new(
            EnableExecutionPreparation: true,
            ServiceIdentityAllowed: true,
            ProductionImpacting: true,
            DualControlSatisfied: true,
            PosServerNumberingReady: true,
            PosServerIdempotencyContractConfirmed: true,
            PosServerSequencePolicyConfirmed: true,
            PosServerFiscalIdentityConfirmed: true,
            ProductionBirReadinessConfirmed: true);

    private static FiscalExceptionPosServerRetryContractReadinessResult ReadyPosServerRetryContractReadiness() =>
        new(
            Status: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            SemanticHashCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            IdempotencyMappingStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            ReadbackFieldCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            FiscalNumberingReadinessStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            ConflictReplayBehaviorStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
            BlockReasonCode: null,
            SafeSummary: "pos_server_retry_contract_readiness_ready_no_execution",
            RetryExecutionAvailable: false);

    private static IFiscalIssuancePosServerLiveIntegrationService LiveIntegrationReturning(
        ReadyScenario scenario,
        PosServerFiscalDocumentCreateResult posResult,
        FiscalIssuanceIntegrationState appliedState)
    {
        var liveIntegration = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                Arg.Any<Guid>(),
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.Applied(
                scenario.MappedRequest,
                posResult,
                scenario.Reference with { FiscalIssuanceState = appliedState }));
        return liveIntegration;
    }

    private static PosServerFiscalDocumentCreateResult CompletePosServerCreateResult(
        FiscalIssuanceResultClassification classification) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.Accepted,
            Succeeded: true,
            HttpStatusCode: 200,
            Code: "ok",
            Message: "ok",
            FiscalDocumentId: Guid.NewGuid(),
            ResultClassification: classification,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIdentityId: Guid.NewGuid(),
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            FiscalSequencePolicyId: Guid.NewGuid(),
            FiscalSequenceValue: 1001,
            FiscalDocumentNumber: "SI-000001001",
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-05T10:05:00+08:00"),
            FiscalNumberAssignedByRef: "pos-server-sequence",
            ErrorPosture: null);

    private static PosServerFiscalDocumentCreateResult FailurePosServerCreateResult(
        PosServerFiscalDocumentOutcome outcome,
        string code,
        FiscalIssuanceErrorPosture posture) =>
        new(
            Outcome: outcome,
            Succeeded: false,
            HttpStatusCode: outcome == PosServerFiscalDocumentOutcome.Conflict ? 409 : 503,
            Code: code,
            Message: code,
            FiscalDocumentId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalIdentityId: null,
            FiscalDocumentStatusCodeId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            ErrorPosture: posture);

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        CentralPmsFiscalDocumentMappingContext context,
        FiscalSemanticRequestHashResult hash)
    {
        var now = DateTimeOffset.Parse("2026-07-05T09:00:00+08:00");
        return new FiscalIssuanceReferenceRecord(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: context.SitePosServerId,
            SitePosServerRef: context.SitePosServerRef,
            PayableBasisRef: context.PayableBasis.PayableBasisRef,
            UpstreamFinalityReference: context.PayableBasis.UpstreamFinalityRef,
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
            FiscalDocumentStatusCodeId: context.FiscalDocumentStatusCodeId,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid(),
            FiscalDocumentTypeCodeId: context.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: context.FiscalDocumentTypeCodeKey,
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: hash.HashValue,
            SemanticRequestHashAlgorithm: hash.HashAlgorithm,
            SemanticRequestHashSourceVersion: hash.HashSourceVersion,
            SemanticRequestHashSourceFactCount: hash.SourceFactCount,
            SemanticRequestHashSafeSummary: hash.SafeSourceSummary,
            SemanticRequestHashRecordedAt: now);
    }

    private sealed record ReadyScenario(
        FiscalIssuanceReferenceRecord Reference,
        CentralPmsFiscalDocumentMappingContext FiscalContext,
        PosServerFiscalDocumentCreateRequest MappedRequest,
        FiscalExceptionControlledRetryExecutionRequest Request);

    private sealed class FakeReferenceReader : IFiscalExceptionQueueReferenceReader
    {
        private readonly IReadOnlyList<FiscalIssuanceReferenceRecord> _records;

        public FakeReferenceReader(IReadOnlyList<FiscalIssuanceReferenceRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<FiscalIssuanceReferenceRecord>> ListFiscalExceptionReferencesAsync(
            FiscalExceptionQueueQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records);

        public Task<FiscalIssuanceReferenceRecord?> FindFiscalExceptionReferenceAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record =>
                record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId));
    }

    private sealed class FakeReadbackAttemptRepository : IFiscalExceptionReadbackAttemptRepository
    {
        private FiscalExceptionReadbackAttemptSummary? _summary;

        public Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
            FiscalExceptionReadbackAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("controlled retry execution tests do not write readback attempts");

        public Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_summary);

        public void Seed(FiscalExceptionReadbackAttemptSummary summary)
        {
            _summary = summary;
        }
    }

    private sealed class FakeRetryCommandPreparationAuditRepository :
        IFiscalExceptionRetryCommandPreparationAuditRepository
    {
        public Task<FiscalExceptionRetryCommandPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetryCommandPreparationAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FiscalExceptionRetryCommandPreparationAttemptRecord(
                RetryCommandPreparationAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                PaymentConfirmationId: attempt.PaymentConfirmationId,
                PaymentAttemptId: attempt.PaymentAttemptId,
                ParkingSessionId: attempt.ParkingSessionId,
                SiteId: attempt.SiteId,
                SitePosServerId: attempt.SitePosServerId,
                SitePosServerRef: attempt.SitePosServerRef,
                LatestReadbackClassificationBasis: attempt.LatestReadbackClassificationBasis,
                RetryEligibilityDecisionBasis: attempt.RetryEligibilityDecisionBasis,
                CommandPreparationStatus: attempt.CommandPreparationStatus,
                CommandBlockReasonCode: attempt.CommandBlockReasonCode,
                SemanticRequestHashAvailabilityStatus: attempt.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: attempt.IdempotencyContextAvailabilityStatus,
                AttemptedAt: attempt.AttemptedAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CreatedAt: attempt.AttemptedAt));

        public Task<FiscalExceptionRetryCommandPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionRetryCommandPreparationAttemptSummary?>(null);
    }

    private sealed class FakeRetrySchedulingPreparationAuditRepository :
        IFiscalExceptionRetrySchedulingPreparationAuditRepository
    {
        public Task<FiscalExceptionRetrySchedulingPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetrySchedulingPreparationAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FiscalExceptionRetrySchedulingPreparationAttemptRecord(
                RetrySchedulePreparationAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: attempt.RetryCommandPreparationAttemptId,
                PaymentConfirmationId: attempt.PaymentConfirmationId,
                PaymentAttemptId: attempt.PaymentAttemptId,
                ParkingSessionId: attempt.ParkingSessionId,
                SiteId: attempt.SiteId,
                SitePosServerId: attempt.SitePosServerId,
                SitePosServerRef: attempt.SitePosServerRef,
                LatestReadbackClassificationBasis: attempt.LatestReadbackClassificationBasis,
                RetryEligibilityDecisionBasis: attempt.RetryEligibilityDecisionBasis,
                SemanticRequestHashAvailabilityStatus: attempt.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: attempt.IdempotencyContextAvailabilityStatus,
                SchedulingPreparationStatus: attempt.SchedulingPreparationStatus,
                SchedulingBlockReasonCode: attempt.SchedulingBlockReasonCode,
                RequestedAt: attempt.RequestedAt,
                EarliestEligibleAt: attempt.EarliestEligibleAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CreatedAt: attempt.RequestedAt));

        public Task<FiscalExceptionRetrySchedulingPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionRetrySchedulingPreparationAttemptSummary?>(null);
    }

    private sealed class FakeExecutionAuditRepository :
        IFiscalExceptionControlledRetryExecutionAuditRepository
    {
        public List<FiscalExceptionControlledRetryExecutionAttemptRecord> Records { get; } = [];

        public Task<FiscalExceptionControlledRetryExecutionAttemptRecord> RecordAsync(
            FiscalExceptionControlledRetryExecutionAttemptWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionControlledRetryExecutionAttemptRecord(
                RetryExecutionAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: attempt.RetryCommandPreparationAttemptId,
                RetrySchedulePreparationAttemptId: attempt.RetrySchedulePreparationAttemptId,
                ReadbackClassificationBasis: attempt.ReadbackClassificationBasis,
                SemanticRequestHashValue: attempt.SemanticRequestHashValue,
                SemanticRequestHashAlgorithm: attempt.SemanticRequestHashAlgorithm,
                SemanticRequestHashSourceVersion: attempt.SemanticRequestHashSourceVersion,
                UpstreamFinalityReference: attempt.UpstreamFinalityReference,
                ExecutionStatus: attempt.ExecutionStatus,
                BlockReasonCode: attempt.BlockReasonCode,
                PosServerOutcome: attempt.PosServerOutcome,
                PosServerResultClassification: attempt.PosServerResultClassification,
                PosServerFiscalDocumentId: attempt.PosServerFiscalDocumentId,
                FiscalDocumentNumber: attempt.FiscalDocumentNumber,
                FiscalIdentityId: attempt.FiscalIdentityId,
                FiscalSequencePolicyId: attempt.FiscalSequencePolicyId,
                FiscalSequenceValue: attempt.FiscalSequenceValue,
                FiscalSeries: attempt.FiscalSeries,
                FiscalNumberPrefixText: attempt.FiscalNumberPrefixText,
                FiscalNumberSuffixText: attempt.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: attempt.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: attempt.FiscalNumberAssignedByRef,
                AttemptedAt: attempt.AttemptedAt,
                CompletedAt: attempt.CompletedAt,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CorrelationId: attempt.CorrelationId,
                SafeSummary: attempt.SafeSummary,
                CreatedAt: attempt.AttemptedAt);

            Records.Add(record);
            return Task.FromResult(record);
        }
    }
}
