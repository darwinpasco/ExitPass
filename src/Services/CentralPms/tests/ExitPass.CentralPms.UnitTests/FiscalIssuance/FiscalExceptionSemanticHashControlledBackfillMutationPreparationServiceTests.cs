using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashControlledBackfillMutationPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenApprovalIsNotReady_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var preview = SuccessfulPreviewAudit();
        var approval = new FiscalExceptionSemanticHashControlledBackfillApprovalService()
            .Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(detail, preview));
        var audit = new FakeMutationAuditRepository();
        var sut = new FiscalExceptionSemanticHashControlledBackfillMutationPreparationService(
            new FiscalExceptionSemanticHashControlledBackfillMutationOptions(),
            audit);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                approval,
                preview),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_approval_policy_missing");
        result.Command.Should().BeNull();
        audit.Records.Should().ContainSingle();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenPreviewAuditIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = Sut(out _);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                ReadyApproval(),
                LatestRecalculationPreviewAuditSummary: null),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_recalculation_preview_audit_missing");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenApprovalActorOrDualControlIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var preview = SuccessfulPreviewAudit();
        var blockedApproval = ReadyApproval() with
        {
            Status = FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl,
            BlockReasonCode = "semantic_hash_backfill_dual_control_required",
            DualControlSatisfied = false
        };
        var sut = Sut(out _);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                blockedApproval,
                preview),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_dual_control_required");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenReferenceAlreadyCurrent_BlocksFailClosed()
    {
        var detail = await DetailAsync(CurrentReference());
        var sut = Sut(out _);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                ReadyApproval(),
                SuccessfulPreviewAudit()),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_already_current_sha256_v1");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenReferenceSourceVersionIsIncompatible_Blocks()
    {
        var detail = await DetailAsync(CurrentReference());
        detail = detail with
        {
            Summary = detail.Summary with
            {
                SemanticRequestHashAvailabilityStatus =
                    FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed,
                SemanticRequestHashSourceVersion = "sha256:v2",
                StoredSemanticHashSourceVersion = "sha256:v2"
            }
        };
        var sut = Sut(out _);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                ReadyApproval(),
                SuccessfulPreviewAudit()),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_source_version_incompatible");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenFiscalPostureIsUnsafe_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        detail = detail with
        {
            Summary = detail.Summary with
            {
                Category = FiscalExceptionQueueCategory.FiscalMismatch,
                QueueState = FiscalExceptionQueueState.MismatchReview
            }
        };
        var sut = Sut(out _);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                ReadyApproval(),
                SuccessfulPreviewAudit()),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("fiscal_exception_state_not_safe_for_semantic_hash_backfill_mutation");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenAllGatesPassAndDefaultDisabled_ReturnsPreparedButDoesNotMutate()
    {
        var detail = await DetailAsync(LegacyReference());
        var preview = SuccessfulPreviewAudit();
        var approval = ReadyApproval();
        var sut = Sut(out var audit);

        var result = await sut.PrepareAsync(
            new FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
                detail,
                approval,
                preview,
                ActorServiceIdentityId: Guid.NewGuid(),
                ApprovalReference: "APPROVAL-2026-07-06-001",
                DualControlReference: "DUAL-2026-07-06-001"),
            CancellationToken.None);

        result.Status.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled);
        result.BlockReasonCode.Should().Be("semantic_hash_controlled_backfill_mutation_disabled");
        result.Command.Should().NotBeNull();
        result.Command!.MutationMode.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly);
        result.Command.RecalculatedHashValue.Should().Be(preview.RecalculatedHashValue);
        result.MutationEnabled.Should().BeFalse();
        result.AuditPersisted.Should().BeTrue();
        audit.Records.Should().ContainSingle(record =>
            record.MutationStatus ==
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled);
        detail.Summary.SemanticRequestHashSourceVersion
            .Should().Be(FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion);
        detail.Summary.RetryEligibilityStatus
            .Should().NotBe(FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task GetAsync_WhenMutationPrepAuditExists_ReturnsSafeMutationPosture()
    {
        var reference = LegacyReference();
        var preview = SuccessfulPreviewAudit(reference.FiscalIssuanceReferenceId);
        var previewAudit = new FakeRecalculationPreviewAuditRepository(preview);
        var mutationAudit = new FakeMutationAuditRepository();
        var mutationService = new FiscalExceptionSemanticHashControlledBackfillMutationPreparationService(
            new FiscalExceptionSemanticHashControlledBackfillMutationOptions(),
            mutationAudit);
        var sut = QueueService(
            reference,
            FiscalExceptionReadbackClassification.NotFound,
            previewAudit,
            new FiscalExceptionSemanticHashControlledBackfillApprovalService(ReadyApprovalOptions()),
            mutationService,
            mutationAudit);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.SemanticHashControlledBackfillMutationPreparationStatus
            .Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled);
        detail.Summary.SemanticHashControlledBackfillLatestMutationAuditId.Should().NotBeNull();
        detail.Summary.SemanticHashControlledBackfillMutationAttemptCount.Should().Be(1);
        detail.Summary.SemanticHashControlledBackfillMutationMode
            .Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly);
        detail.Summary.SemanticHashControlledBackfillMutationEnabled.Should().BeFalse();
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationService Sut(
        out FakeMutationAuditRepository audit)
    {
        audit = new FakeMutationAuditRepository();
        return new FiscalExceptionSemanticHashControlledBackfillMutationPreparationService(
            new FiscalExceptionSemanticHashControlledBackfillMutationOptions(),
            audit);
    }

    private static FiscalExceptionSemanticHashControlledBackfillApprovalOptions ReadyApprovalOptions() =>
        new(
            approvalPolicyConfigured: true,
            dualControlRequired: true,
            dualControlSatisfied: true,
            actorOrServiceAuthorized: true,
            explicitApprovalPresent: true);

    private static FiscalExceptionSemanticHashControlledBackfillApprovalResult ReadyApproval() =>
        new(
            Status: FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            BlockReasonCode: null,
            SafeSummary: "semantic_hash_controlled_backfill_preconditions_ready_not_mutated",
            LegacySourceVersion: FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            RequiredSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            LatestRecalculationPreviewAuditId: Guid.NewGuid(),
            LatestRecalculationPreviewAttemptedAt: DateTimeOffset.Parse("2026-07-06T09:00:00+08:00"),
            LatestRecalculationPreviewAuditExists: true,
            PreviewSuccessful: true,
            CompleteOriginalRequestFactsAvailable: true,
            RecalculatedHashIsSha256V1: true,
            RecalculatedHashMetadataComplete: true,
            DualControlRequired: true,
            DualControlSatisfied: true,
            ExplicitApprovalPresent: true,
            ActorOrServiceAuthorizationPresent: true,
            DualControlPosture: FiscalExceptionSemanticHashControlledBackfillDualControlPosture.Satisfied,
            ApprovalPosture: FiscalExceptionSemanticHashControlledBackfillApprovalPosture.ApprovalPresent,
            ActorAuthorizationPosture: FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture.Present,
            MutationStatus: FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
            FiscalIssuanceReferenceMutated: false,
            RetryExecutionAvailable: false,
            PosServerPostCalled: false,
            RetryExecuted: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);

    private static FiscalExceptionSemanticHashRecalculationPreviewAuditSummary SuccessfulPreviewAudit(
        Guid? auditId = null) =>
        new(
            LastRecalculationPreviewAuditId: auditId ?? Guid.NewGuid(),
            LastPreviewStatus: FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
            LastAttemptedAt: DateTimeOffset.Parse("2026-07-06T09:00:00+08:00"),
            AttemptCount: 1,
            LastBlockReasonCode: null,
            CompleteOriginalRequestFactsAvailable: true,
            RecalculatedHashValue: new string('d', 64),
            RecalculatedHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            RecalculatedHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            RecalculatedSourceFactCount: 20,
            RecalculatedSafeSourceSummary: "semantic_request_hash_source_available:facts=20",
            RecalculatedHashMatchesStoredHash: false,
            MutationStatus: FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
            SafeSummary: "semantic_hash_recalculation_preview_calculated_not_mutated");

    private static void AssertNoMutationOrRetry(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult result)
    {
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
        result.RetryExecutionAvailable.Should().BeFalse();
        result.PosServerPostCalled.Should().BeFalse();
        result.RetryExecuted.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        result.FiscalNumberEdited.Should().BeFalse();
        result.ManualFiscalDocumentCreated.Should().BeFalse();
    }

    private static async Task<FiscalExceptionQueueCaseDetail> DetailAsync(FiscalIssuanceReferenceRecord reference)
    {
        var detail = await new FiscalExceptionQueueService(new FakeReferenceReader([reference]))
            .GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        return detail!;
    }

    private static FiscalExceptionQueueService QueueService(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionReadbackClassification readbackClassification,
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository previewAuditRepository,
        IFiscalExceptionSemanticHashControlledBackfillApprovalService approvalService,
        IFiscalExceptionSemanticHashControlledBackfillMutationPreparationService mutationService,
        IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository mutationAuditRepository)
    {
        var attempts = new FakeReadbackAttemptRepository(
            new FiscalExceptionReadbackAttemptSummary(
                Classification: readbackClassification,
                AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                AttemptCount: 1,
                SafeErrorSummary: readbackClassification.ToString()));

        return new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            attempts,
            new FiscalExceptionRetryEligibilityEvaluator(),
            new FiscalExceptionRetryCommandPreparationService(),
            retryCommandPreparationAuditRepository: null,
            new FiscalExceptionRetrySchedulingPreparationService(
                new FiscalExceptionRetrySchedulingPreparationOptions(
                    EnableSchedulePreparation: true,
                    RetrySchedulePolicyConfigured: true,
                    RetryBackoffPolicyConfigured: true)),
            retrySchedulingPreparationAuditRepository: null,
            new FiscalExceptionRetryExecutionPreparationService(),
            new FiscalExceptionPosServerRetryContractReadinessService(),
            previewAuditRepository,
            approvalService,
            mutationService,
            mutationAuditRepository);
    }

    private static FiscalIssuanceReferenceRecord LegacyReference() =>
        BaseReference() with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue = new string('c', 64),
            SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion =
                FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            SemanticRequestHashSourceFactCount = 42,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=42"
        };

    private static FiscalIssuanceReferenceRecord CurrentReference() =>
        BaseReference() with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue = new string('d', 64),
            SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount = 18,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=18"
        };

    private static FiscalIssuanceReferenceRecord BaseReference()
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
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            LatestExceptionReason: FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid());
    }

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
        private readonly FiscalExceptionReadbackAttemptSummary _summary;

        public FakeReadbackAttemptRepository(FiscalExceptionReadbackAttemptSummary summary)
        {
            _summary = summary;
        }

        public Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
            FiscalExceptionReadbackAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("controlled backfill mutation tests are read-only");

        public Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionReadbackAttemptSummary?>(_summary);
    }

    private sealed class FakeRecalculationPreviewAuditRepository :
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
    {
        public FakeRecalculationPreviewAuditRepository(
            FiscalExceptionSemanticHashRecalculationPreviewAuditSummary summary)
        {
            Summary = summary;
        }

        public FiscalExceptionSemanticHashRecalculationPreviewAuditSummary Summary { get; }

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("controlled backfill mutation tests are read-only");

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?>(Summary);
    }

    private sealed class FakeMutationAuditRepository :
        IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository
    {
        public List<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> Records { get; } = [];

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord(
                MutationAuditId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                RecalculationPreviewAuditId: attempt.RecalculationPreviewAuditId,
                MutationPreparationAuditId: attempt.MutationPreparationAuditId,
                ApprovalBasisStatus: attempt.ApprovalBasisStatus,
                OldSourceVersion: attempt.OldSourceVersion,
                RequiredSourceVersion: attempt.RequiredSourceVersion,
                OldHashValue: attempt.OldHashValue,
                NewHashValue: attempt.NewHashValue,
                NewHashAlgorithm: attempt.NewHashAlgorithm,
                NewHashSourceVersion: attempt.NewHashSourceVersion,
                NewHashSourceFactCount: attempt.NewHashSourceFactCount,
                SafeSourceSummary: attempt.SafeSourceSummary,
                MutationStatus: attempt.MutationStatus,
                BlockReasonCode: attempt.BlockReasonCode,
                MutationMode: attempt.MutationMode,
                MutationEnabled: attempt.MutationEnabled,
                FiscalIssuanceReferenceMutated: attempt.FiscalIssuanceReferenceMutated,
                AttemptedAt: attempt.AttemptedAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ActorServiceIdentityId: attempt.ActorServiceIdentityId,
                ApprovalReference: attempt.ApprovalReference,
                DualControlReference: attempt.DualControlReference,
                CreatedAt: attempt.AttemptedAt);

            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord?> GetRecordAsync(
            Guid mutationAuditId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Records.SingleOrDefault(record => record.MutationAuditId == mutationAuditId));

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            var matching = Records
                .Where(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId)
                .OrderByDescending(record => record.AttemptedAt)
                .ThenByDescending(record => record.CreatedAt)
                .ToArray();

            if (matching.Length == 0)
            {
                return Task.FromResult<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?>(null);
            }

            var latest = matching[0];
            return Task.FromResult<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?>(
                new FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary(
                    LastMutationAuditId: latest.MutationAuditId,
                    LastMutationStatus: latest.MutationStatus,
                    LastAttemptedAt: latest.AttemptedAt,
                    AttemptCount: matching.Length,
                    LastBlockReasonCode: latest.BlockReasonCode,
                    MutationMode: latest.MutationMode,
                    MutationEnabled: latest.MutationEnabled,
                    FiscalIssuanceReferenceMutated: latest.FiscalIssuanceReferenceMutated,
                    OldSourceVersion: latest.OldSourceVersion,
                    NewSourceVersion: latest.NewHashSourceVersion,
                    NewHashValue: latest.NewHashValue,
                    SafeSummary: latest.SafeSummary));
        }
    }
}
