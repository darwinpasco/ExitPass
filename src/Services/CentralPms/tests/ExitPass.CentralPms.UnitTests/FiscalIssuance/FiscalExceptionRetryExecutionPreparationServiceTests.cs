using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionRetryExecutionPreparationServiceTests
{
    [Fact]
    public async Task EvaluateAsync_WhenDefaultOptions_ReturnsDisabledWithoutExecution()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        var sut = new FiscalExceptionRetryExecutionPreparationService();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Disabled);
        result.BlockReasonCode.Should().Be("retry_execution_preparation_disabled");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSchedulerPreparationIsMissing_BlocksExecutionPrep()
    {
        var (detail, commandPreparation, _) = await SafeExecutionPrerequisitesAsync();
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                MissingSchedulingPreparation()),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_scheduling_preparation_missing");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCommandPreparationAuditIsMissing_BlocksExecutionPrep()
    {
        var (detail, _, _) = await SafeExecutionPrerequisitesAsync();
        var commandPreparation = PreparedCommand(detail, retryCommandPreparationAttemptId: null);
        var schedulingPreparation = PreparedSchedule(detail, retryCommandPreparationAttemptId: null);
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_command_preparation_audit_missing");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSemanticHashIsMissing_BlocksExecutionPrep()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        detail = detail with
        {
            Summary = detail.Summary with
            {
                SemanticRequestHashAvailabilityStatus =
                    FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing,
                SemanticRequestHashValue = null,
                SemanticRequestHashAlgorithm = null,
                SemanticRequestHashSourceVersion = null
            }
        };
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_request_hash_required_but_missing");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSemanticHashUsesLegacySourceVersion_BlocksExecutionPrep()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        detail = detail with
        {
            Summary = detail.Summary with
            {
                SemanticRequestHashSourceVersion =
                    FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion
            }
        };
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_legacy_version_requires_recalculation");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReadbackIsNotNotFound_BlocksExecutionPrep()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        detail = detail with
        {
            Summary = detail.Summary with
            {
                ReadbackClassification = FiscalExceptionReadbackClassification.Matched,
                ReadbackAttemptCount = 1
            }
        };
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("readback_matched");
        AssertNoExecutionSideEffects(result);
    }

    [Theory]
    [InlineData("numbering", "pos_server_numbering_not_ready",
        FiscalExceptionRetryExecutionPosServerReadinessStatus.NumberingNotReady)]
    [InlineData("idempotency", "pos_server_idempotency_contract_not_confirmed",
        FiscalExceptionRetryExecutionPosServerReadinessStatus.IdempotencyContractNotConfirmed)]
    [InlineData("sequence", "pos_server_sequence_policy_not_confirmed",
        FiscalExceptionRetryExecutionPosServerReadinessStatus.SequencePolicyNotConfirmed)]
    [InlineData("identity", "pos_server_fiscal_identity_not_confirmed",
        FiscalExceptionRetryExecutionPosServerReadinessStatus.FiscalIdentityNotConfirmed)]
    [InlineData("bir", "production_bir_readiness_not_confirmed",
        FiscalExceptionRetryExecutionPosServerReadinessStatus.ProductionBirReadinessNotConfirmed)]
    public async Task EvaluateAsync_WhenPosServerReadinessGateIsNotConfirmed_ReturnsReadinessRequirement(
        string missingGate,
        string expectedBlockReason,
        FiscalExceptionRetryExecutionPosServerReadinessStatus expectedReadinessStatus)
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        var sut = new FiscalExceptionRetryExecutionPreparationService(
            ReadyOptions(
                posServerNumberingReady: missingGate != "numbering",
                posServerIdempotencyContractConfirmed: missingGate != "idempotency",
                posServerSequencePolicyConfirmed: missingGate != "sequence",
                posServerFiscalIdentityConfirmed: missingGate != "identity",
                productionBirReadinessConfirmed: missingGate != "bir"));

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.RequiresPosServerReadiness);
        result.BlockReasonCode.Should().Be(expectedBlockReason);
        result.PosServerReadinessStatus.Should().Be(expectedReadinessStatus);
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenProductionImpactingAndDualControlMissing_RequiresDualControl()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        var sut = new FiscalExceptionRetryExecutionPreparationService(
            ReadyOptions(dualControlSatisfied: false));

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.RequiresDualControl);
        result.BlockReasonCode.Should().Be("dual_control_required");
        result.DualControlRequired.Should().BeTrue();
        result.AuthorizationStatus.Should().Be(FiscalExceptionRetryExecutionAuthorizationStatus.DualControlRequired);
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenAllPreconditionsPass_ReturnsReadyPostureWithoutExecutingRetry()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.ReadyForExecutionWhenEnabled);
        result.BlockReasonCode.Should().BeNull();
        result.AuthorizationStatus.Should().Be(FiscalExceptionRetryExecutionAuthorizationStatus.DualControlSatisfied);
        result.PosServerReadinessStatus.Should().Be(FiscalExceptionRetryExecutionPosServerReadinessStatus.Confirmed);
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenPosServerContractReadinessIsUnconfirmed_BlocksExecutionPrep()
    {
        var (detail, commandPreparation, schedulingPreparation) = await SafeExecutionPrerequisitesAsync();
        var sut = ReadySut();

        var result = await sut.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation,
                new FiscalExceptionPosServerRetryContractReadinessResult(
                    Status: FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed,
                    SemanticHashCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed,
                    IdempotencyMappingStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
                    ReadbackFieldCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
                    FiscalNumberingReadinessStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
                    ConflictReplayBehaviorStatus: FiscalExceptionPosServerRetryContractReadinessStatus.Ready,
                    BlockReasonCode: "pos_server_semantic_hash_compatibility_unconfirmed",
                    SafeSummary: "pos_server_semantic_hash_compatibility_unconfirmed",
                    RetryExecutionAvailable: false)),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_compatibility_unconfirmed");
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task GetAsync_WhenExecutionPrepIsConfigured_ReturnsSafeExecutionPosture()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue = ValidSemanticRequestHashValue(),
            SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount = 42,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=42"
        };
        var readback = new FakeReadbackAttemptRepository();
        readback.Seed(new FiscalExceptionReadbackAttemptSummary(
            Classification: FiscalExceptionReadbackClassification.NotFound,
            AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
            AttemptCount: 1,
            SafeErrorSummary: "not_found"));
        var commandAudit = new FakeRetryCommandPreparationAuditRepository();
        var scheduleAudit = new FakeRetrySchedulingPreparationAuditRepository();
        var service = new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            readback,
            new FiscalExceptionRetryEligibilityEvaluator(),
            new FiscalExceptionRetryCommandPreparationService(commandAudit),
            commandAudit,
            new FiscalExceptionRetrySchedulingPreparationService(
                SchedulingOptions(),
                scheduleAudit),
            scheduleAudit,
            ReadySut(),
            new ReadyPosServerRetryContractReadinessService());

        var detail = await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryExecutionPreparationStatus
            .Should().Be(FiscalExceptionRetryExecutionPreparationStatus.ReadyForExecutionWhenEnabled);
        detail.Summary.RetryExecutionBlockReasonCode.Should().BeNull();
        detail.Summary.SafeRetryExecutionPreparationSummary
            .Should().Be("retry_execution_preconditions_ready_no_execution");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
    }

    [Fact]
    public void RetryExecutionPreparation_DoesNotIntroduceEndpointWorkerQueueOrPosServerDependency()
    {
        var constructorParameters = typeof(FiscalExceptionRetryExecutionPreparationService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().NotContain(parameter =>
            parameter.Contains("PosServer", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Queue", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Gate", StringComparison.OrdinalIgnoreCase));

        var fiscalIssuanceTypes = typeof(FiscalExceptionRetryExecutionPreparationService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionRetryExecutionPreparationService).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryExecutionJob", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalExceptionRetryExecutionPreparationService ReadySut() =>
        new(ReadyOptions());

    private static FiscalExceptionRetryExecutionPreparationOptions ReadyOptions(
        bool dualControlSatisfied = true,
        bool posServerNumberingReady = true,
        bool posServerIdempotencyContractConfirmed = true,
        bool posServerSequencePolicyConfirmed = true,
        bool posServerFiscalIdentityConfirmed = true,
        bool productionBirReadinessConfirmed = true) =>
        new(
            EnableExecutionPreparation: true,
            ServiceIdentityAllowed: true,
            ProductionImpacting: true,
            DualControlSatisfied: dualControlSatisfied,
            PosServerNumberingReady: posServerNumberingReady,
            PosServerIdempotencyContractConfirmed: posServerIdempotencyContractConfirmed,
            PosServerSequencePolicyConfirmed: posServerSequencePolicyConfirmed,
            PosServerFiscalIdentityConfirmed: posServerFiscalIdentityConfirmed,
            ProductionBirReadinessConfirmed: productionBirReadinessConfirmed);

    private static FiscalExceptionRetrySchedulingPreparationOptions SchedulingOptions() =>
        new(
            EnableSchedulePreparation: true,
            RetrySchedulePolicyConfigured: true,
            RetryBackoffPolicyConfigured: true);

    private static async Task<(
        FiscalExceptionQueueCaseDetail Detail,
        FiscalExceptionRetryCommandPreparationResult CommandPreparation,
        FiscalExceptionRetrySchedulingPreparationResult SchedulingPreparation)> SafeExecutionPrerequisitesAsync()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue = ValidSemanticRequestHashValue(),
            SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount = 42,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=42"
        };
        var readback = new FakeReadbackAttemptRepository();
        readback.Seed(new FiscalExceptionReadbackAttemptSummary(
            Classification: FiscalExceptionReadbackClassification.NotFound,
            AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
            AttemptCount: 1,
            SafeErrorSummary: "not_found"));
        var detail = await new FiscalExceptionQueueService(
                new FakeReferenceReader([reference]),
                readback)
            .GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        var commandAudit = new FakeRetryCommandPreparationAuditRepository();
        var commandPreparation = await new FiscalExceptionRetryCommandPreparationService(commandAudit)
            .PrepareAsync(new FiscalExceptionRetryCommandPreparationRequest(detail!), CancellationToken.None);
        var scheduleAudit = new FakeRetrySchedulingPreparationAuditRepository();
        var schedulingPreparation = await new FiscalExceptionRetrySchedulingPreparationService(
                SchedulingOptions(),
                scheduleAudit)
            .PrepareAsync(
                new FiscalExceptionRetrySchedulingPreparationRequest(detail!, commandPreparation),
                CancellationToken.None);

        return (detail!, commandPreparation, schedulingPreparation);
    }

    private static FiscalExceptionRetrySchedulingPreparationResult MissingSchedulingPreparation() =>
        new(
            Status: FiscalExceptionRetrySchedulingPreparationStatus.Blocked,
            BlockReasonCode: "retry_scheduling_preparation_missing",
            SafeSummary: "retry_scheduling_missing_for_execution_prep_test",
            Schedule: null,
            PosServerPostCalled: false,
            ExecutableJobEnqueued: false,
            RetryEndpointExposed: false,
            PaymentFinalityChanged: false,
            FiscalReferenceSuccessRecorded: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);

    private static FiscalExceptionRetryCommandPreparationResult PreparedCommand(
        FiscalExceptionQueueCaseDetail detail,
        Guid? retryCommandPreparationAttemptId) =>
        new(
            Status: FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable,
            BlockReasonCode: null,
            SafeSummary: "retry_command_prepared_non_executable",
            Command: new FiscalExceptionRetryCommandEnvelope(
                FiscalIssuanceReferenceId: detail.Summary.FiscalIssuanceReferenceId,
                PaymentConfirmationId: detail.Summary.PaymentConfirmationId,
                PaymentAttemptId: detail.Summary.PaymentAttemptId,
                ParkingSessionId: detail.Summary.ParkingSessionId,
                SiteId: detail.Summary.SiteId,
                SitePosServerId: detail.Summary.SitePosServerId,
                SitePosServerRef: detail.Summary.SitePosServerRef,
                FiscalDocumentTypeContextStatus: "not_available_in_current_fiscal_reference_model",
                UpstreamFinalityReference: detail.Summary.UpstreamFinalityReference,
                SemanticRequestHashAvailabilityStatus: detail.Summary.SemanticRequestHashAvailabilityStatus,
                SemanticRequestHashValue: detail.Summary.SemanticRequestHashValue,
                SemanticRequestHashAlgorithm: detail.Summary.SemanticRequestHashAlgorithm,
                SemanticRequestHashSourceVersion: detail.Summary.SemanticRequestHashSourceVersion,
                LatestReadbackClassificationBasis:
                    detail.Summary.ReadbackClassification ?? FiscalExceptionReadbackClassification.NotFound,
                RetryEligibilityDecisionBasis: detail.Summary.RetryEligibilityDecision,
                SafeBlockReasonCode: null,
                CorrelationId: detail.CorrelationId,
                Executable: false),
            SemanticRequestHashAvailabilityStatus: detail.Summary.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus: detail.Summary.IdempotencyContextAvailabilityStatus,
            PosServerPostCalled: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            RetryCommandPreparationAttemptId: retryCommandPreparationAttemptId,
            RetryCommandPreparationAttemptedAt: DateTimeOffset.UtcNow);

    private static FiscalExceptionRetrySchedulingPreparationResult PreparedSchedule(
        FiscalExceptionQueueCaseDetail detail,
        Guid? retryCommandPreparationAttemptId) =>
        new(
            Status: FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared,
            BlockReasonCode: null,
            SafeSummary: "retry_scheduling_prepared_non_executable",
            Schedule: new FiscalExceptionRetrySchedulePreparationEnvelope(
                RetrySchedulePreparationAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: detail.Summary.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: retryCommandPreparationAttemptId,
                RetryEligibilityDecisionBasis: detail.Summary.RetryEligibilityDecision,
                LatestReadbackClassificationBasis: detail.Summary.ReadbackClassification,
                SemanticRequestHashAvailabilityStatus: detail.Summary.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: detail.Summary.IdempotencyContextAvailabilityStatus,
                UpstreamFinalityReference: detail.Summary.UpstreamFinalityReference,
                RequestedAt: DateTimeOffset.UtcNow,
                EarliestEligibleAt: DateTimeOffset.UtcNow,
                SchedulePolicySummary: "retry_schedule_policy_configured_backoff_configured_no_execution",
                CorrelationId: detail.CorrelationId,
                Executable: false),
            PosServerPostCalled: false,
            ExecutableJobEnqueued: false,
            RetryEndpointExposed: false,
            PaymentFinalityChanged: false,
            FiscalReferenceSuccessRecorded: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            RetrySchedulePreparationAttemptId: Guid.NewGuid(),
            RetrySchedulePreparationAttemptedAt: DateTimeOffset.UtcNow);

    private static void AssertNoExecutionSideEffects(FiscalExceptionRetryExecutionPreparationResult result)
    {
        result.PosServerPostCalled.Should().BeFalse();
        result.ExecutableJobEnqueued.Should().BeFalse();
        result.RetryEndpointExposed.Should().BeFalse();
        result.RetryExecuted.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.FiscalReferenceSuccessRecorded.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        result.FiscalNumberEdited.Should().BeFalse();
        result.ManualFiscalDocumentCreated.Should().BeFalse();
    }

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceExceptionReason? reason = null)
    {
        var now = DateTimeOffset.Parse("2026-07-05T09:00:00+08:00");
        return new FiscalIssuanceReferenceRecord(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: $"CPS-POS-UAT:{Guid.NewGuid():N}",
            PosServerFiscalDocumentId: Guid.NewGuid(),
            FiscalIdentityId: Guid.NewGuid(),
            FiscalSequencePolicyId: Guid.NewGuid(),
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: reason ?? FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: reason?.ToString() ?? "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid());
    }

    private static string ValidSemanticRequestHashValue() =>
        new('a', 64);

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

    private sealed class ReadyPosServerRetryContractReadinessService :
        IFiscalExceptionPosServerRetryContractReadinessService
    {
        public FiscalExceptionPosServerRetryContractReadinessResult Evaluate(
            FiscalExceptionPosServerRetryContractReadinessRequest request) =>
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
    }

    private sealed class FakeReadbackAttemptRepository : IFiscalExceptionReadbackAttemptRepository
    {
        private FiscalExceptionReadbackAttemptSummary? _summary;

        public Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
            FiscalExceptionReadbackAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("retry execution preparation tests are read-only");

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
        private readonly List<FiscalExceptionRetryCommandPreparationAttemptRecord> _records = [];

        public Task<FiscalExceptionRetryCommandPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetryCommandPreparationAttemptWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionRetryCommandPreparationAttemptRecord(
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
                CreatedAt: attempt.AttemptedAt);

            _records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionRetryCommandPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            var latest = _records
                .Where(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId)
                .OrderByDescending(record => record.AttemptedAt)
                .FirstOrDefault();

            if (latest is null)
            {
                return Task.FromResult<FiscalExceptionRetryCommandPreparationAttemptSummary?>(null);
            }

            return Task.FromResult<FiscalExceptionRetryCommandPreparationAttemptSummary?>(
                new FiscalExceptionRetryCommandPreparationAttemptSummary(
                    LastRetryCommandPreparationAttemptId: latest.RetryCommandPreparationAttemptId,
                    LastCommandPreparationStatus: latest.CommandPreparationStatus,
                    LastAttemptedAt: latest.AttemptedAt,
                    AttemptCount: _records.Count(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId),
                    LastCommandBlockReasonCode: latest.CommandBlockReasonCode,
                    SemanticRequestHashAvailabilityStatus: latest.SemanticRequestHashAvailabilityStatus,
                    IdempotencyContextAvailabilityStatus: latest.IdempotencyContextAvailabilityStatus,
                    SafeSummary: latest.SafeSummary));
        }
    }

    private sealed class FakeRetrySchedulingPreparationAuditRepository :
        IFiscalExceptionRetrySchedulingPreparationAuditRepository
    {
        private readonly List<FiscalExceptionRetrySchedulingPreparationAttemptRecord> _records = [];

        public Task<FiscalExceptionRetrySchedulingPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetrySchedulingPreparationAttemptWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionRetrySchedulingPreparationAttemptRecord(
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
                CreatedAt: attempt.RequestedAt);

            _records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionRetrySchedulingPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            var latest = _records
                .Where(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId)
                .OrderByDescending(record => record.RequestedAt)
                .FirstOrDefault();

            if (latest is null)
            {
                return Task.FromResult<FiscalExceptionRetrySchedulingPreparationAttemptSummary?>(null);
            }

            return Task.FromResult<FiscalExceptionRetrySchedulingPreparationAttemptSummary?>(
                new FiscalExceptionRetrySchedulingPreparationAttemptSummary(
                    LastSchedulingPreparationStatus: latest.SchedulingPreparationStatus,
                    LastRequestedAt: latest.RequestedAt,
                    AttemptCount: _records.Count(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId),
                    LastSchedulingBlockReasonCode: latest.SchedulingBlockReasonCode,
                    SafeSummary: latest.SafeSummary));
        }
    }
}
