using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashControlledBackfillApprovalServiceTests
{
    [Fact]
    public async Task Evaluate_WhenNoPreviewAuditExists_BlocksControlledBackfillApprovalReadiness()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(detail));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_recalculation_preview_audit_missing");
        result.LatestRecalculationPreviewAuditExists.Should().BeFalse();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenPreviewAuditWasBlocked_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit() with
            {
                LastPreviewStatus = FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked,
                LastBlockReasonCode = FiscalExceptionSemanticHashRecalculationPreviewService
                    .OriginalFiscalRequestFactsUnavailableReason,
                CompleteOriginalRequestFactsAvailable = false,
                RecalculatedHashValue = null
            }));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be(
            FiscalExceptionSemanticHashRecalculationPreviewService.OriginalFiscalRequestFactsUnavailableReason);
        result.PreviewSuccessful.Should().BeFalse();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenOriginalFactsWereIncomplete_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit() with
            {
                CompleteOriginalRequestFactsAvailable = false
            }));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("original_fiscal_request_facts_unavailable");
        result.CompleteOriginalRequestFactsAvailable.Should().BeFalse();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenRecalculatedHashIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit() with
            {
                RecalculatedHashValue = null
            }));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("recalculated_semantic_hash_missing");
        result.RecalculatedHashMetadataComplete.Should().BeFalse();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenRecalculatedHashIsNotSha256V1_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit() with
            {
                RecalculatedHashAlgorithm = "SHA-512",
                RecalculatedHashSourceVersion = "sha512:v1"
            }));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("recalculated_semantic_hash_not_sha256_v1");
        result.RecalculatedHashIsSha256V1.Should().BeFalse();
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenApprovalPolicyIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit()));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_approval_policy_missing");
        result.ApprovalPosture.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillApprovalPosture.PolicyMissing);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenActorAuthorizationIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService(
            new FiscalExceptionSemanticHashControlledBackfillApprovalOptions(
                approvalPolicyConfigured: true,
                dualControlRequired: true,
                dualControlSatisfied: true,
                actorOrServiceAuthorized: false,
                explicitApprovalPresent: true));

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit()));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_actor_authorization_missing");
        result.ActorAuthorizationPosture.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture.Missing);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenExplicitApprovalIsMissing_Blocks()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService(
            new FiscalExceptionSemanticHashControlledBackfillApprovalOptions(
                approvalPolicyConfigured: true,
                dualControlRequired: true,
                dualControlSatisfied: true,
                actorOrServiceAuthorized: true,
                explicitApprovalPresent: false));

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit()));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_explicit_approval_missing");
        result.ApprovalPosture.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillApprovalPosture.ApprovalMissing);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenDualControlRequiredButNotSatisfied_ReturnsPendingDualControl()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService(
            ReadyOptions(dualControlSatisfied: false));

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit()));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_dual_control_required");
        result.DualControlPosture.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillDualControlPosture.RequiredPending);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenSuccessfulPreviewAndApprovalPostureExists_ReturnsReadyForControlledBackfillOnly()
    {
        var detail = await DetailAsync(LegacyReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService(ReadyOptions());

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
            detail,
            SuccessfulPreviewAudit()));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill);
        result.BlockReasonCode.Should().BeNull();
        result.PreviewSuccessful.Should().BeTrue();
        result.CompleteOriginalRequestFactsAvailable.Should().BeTrue();
        result.RecalculatedHashIsSha256V1.Should().BeTrue();
        result.RecalculatedHashMetadataComplete.Should().BeTrue();
        result.MutationStatus.Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenRecordIsAlreadyCurrentSha256V1_ReturnsNotRequiredCurrent()
    {
        var detail = await DetailAsync(CurrentReference());
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(detail));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent);
        result.BlockReasonCode.Should().Be("semantic_hash_already_current_sha256_v1");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task Evaluate_WhenSourceVersionIsIncompatible_Blocks()
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
        var sut = new FiscalExceptionSemanticHashControlledBackfillApprovalService();

        var result = sut.Evaluate(new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(detail));

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_source_version_incompatible");
        AssertNoMutationOrRetry(result);
    }

    [Fact]
    public async Task GetAsync_WhenPreviewAuditExists_ReturnsSafeControlledBackfillApprovalPosture()
    {
        var reference = LegacyReference();
        var audit = new FakeRecalculationPreviewAuditRepository(SuccessfulPreviewAudit(reference.FiscalIssuanceReferenceId));
        var sut = QueueService(
            reference,
            FiscalExceptionReadbackClassification.NotFound,
            audit,
            new FiscalExceptionSemanticHashControlledBackfillApprovalService(ReadyOptions()));

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.SemanticHashControlledBackfillApprovalStatus
            .Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill);
        detail.Summary.SemanticHashControlledBackfillLatestPreviewAuditId
            .Should().Be(audit.Summary.LastRecalculationPreviewAuditId);
        detail.Summary.SemanticHashControlledBackfillDualControlPosture
            .Should().Be(FiscalExceptionSemanticHashControlledBackfillDualControlPosture.Satisfied);
        detail.Summary.SemanticHashControlledBackfillApprovalPosture
            .Should().Be(FiscalExceptionSemanticHashControlledBackfillApprovalPosture.ApprovalPresent);
        detail.Summary.SemanticHashControlledBackfillMutationStatus
            .Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
        detail.Summary.RetryEligibilityStatus
            .Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady);
        detail.Summary.RetryCommandPreparationStatus
            .Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        detail.Summary.RetrySchedulingPreparationStatus
            .Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        detail.Summary.RetryExecutionPreparationStatus
            .Should().Be(FiscalExceptionRetryExecutionPreparationStatus.Disabled);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
    }

    private static FiscalExceptionSemanticHashControlledBackfillApprovalOptions ReadyOptions(
        bool dualControlSatisfied = true) =>
        new(
            approvalPolicyConfigured: true,
            dualControlRequired: true,
            dualControlSatisfied: dualControlSatisfied,
            actorOrServiceAuthorized: true,
            explicitApprovalPresent: true);

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
        FiscalExceptionSemanticHashControlledBackfillApprovalResult result)
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
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository auditRepository,
        IFiscalExceptionSemanticHashControlledBackfillApprovalService approvalService)
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
            auditRepository,
            approvalService);
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
            throw new NotSupportedException("controlled backfill approval tests are read-only");

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
            throw new NotSupportedException("controlled backfill approval tests are read-only");

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?>(Summary);
    }
}
