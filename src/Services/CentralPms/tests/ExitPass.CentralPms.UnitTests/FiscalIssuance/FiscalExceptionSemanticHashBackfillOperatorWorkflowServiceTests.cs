using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashBackfillOperatorWorkflowServiceTests
{
    [Fact]
    public async Task RequestAsync_WhenActorServiceIdentityIsMissing_BlocksAndPersistsAudit()
    {
        var request = await ReadyRequestAsync();
        request = request with { ActorServiceIdentityId = Guid.Empty };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_actor_authorization_missing");
        result.WorkflowAuditPersisted.Should().BeTrue();
        audit.Records.Should().ContainSingle();
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenApprovalReferenceIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync();
        request = request with { ApprovalReference = "" };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_explicit_approval_missing");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenRequiredDualControlReferenceIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync();
        request = request with { DualControlReference = null };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_dual_control_required");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenRecalculationPreviewAuditIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync();
        request = request with
        {
            RecalculationPreviewAuditId = Guid.NewGuid(),
            LatestRecalculationPreviewAuditSummary = null
        };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_recalculation_preview_audit_missing");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenMutationPreparationAuditIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync();
        request = request with
        {
            MutationPreparationAuditId = Guid.NewGuid(),
            MutationPreparationBasis = null
        };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_mutation_preparation_audit_missing");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenRequestModeIsBatch_Blocks()
    {
        var request = await ReadyRequestAsync();
        request = request with
        {
            RequestMode = FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.Batch
        };
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_operator_workflow_batch_not_allowed");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenDryRun_DoesNotInvokeGuardedMutation()
    {
        var request = await ReadyRequestAsync(executeControlledMutation: false, dryRunOnly: true);
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval);
        result.MutationInvocationPosture.Should()
            .Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.DryRunOnly);
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
        audit.Records.Should().ContainSingle();
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenInvocationDisabled_DoesNotInvokeGuardedMutation()
    {
        var request = await ReadyRequestAsync(executeControlledMutation: true, dryRunOnly: false);
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should()
            .Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.PreparedButMutationInvocationDisabled);
        result.BlockReasonCode.Should()
            .Be("semantic_hash_backfill_operator_workflow_mutation_invocation_disabled");
        guarded.Requests.Should().BeEmpty();
        AssertNoExternalSideEffects(result);
    }

    [Fact]
    public async Task RequestAsync_WhenInvocationEnabledAndAllBasisMatches_CallsGuardedMutation()
    {
        var request = await ReadyRequestAsync(executeControlledMutation: true, dryRunOnly: false);
        var audit = new FakeWorkflowAuditRepository();
        var guarded = new FakeGuardedMutationService();
        var sut = Sut(audit, guarded, enableInvocation: true);

        var result = await sut.RequestAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked);
        result.GuardedMutationStatus.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated);
        result.FiscalIssuanceReferenceMutated.Should().BeTrue();
        guarded.Requests.Should().ContainSingle();
        audit.Records.Should().HaveCount(2);
        AssertNoExternalSideEffects(result, expectHashMutation: true);
    }

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowService Sut(
        FakeWorkflowAuditRepository audit,
        FakeGuardedMutationService guarded,
        bool enableInvocation = false) =>
        new(
            new FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions(enableInvocation),
            new FiscalExceptionSemanticHashControlledBackfillApprovalService(
                new FiscalExceptionSemanticHashControlledBackfillApprovalOptions(
                    approvalPolicyConfigured: true,
                    dualControlRequired: true,
                    dualControlSatisfied: true,
                    actorOrServiceAuthorized: true,
                    explicitApprovalPresent: true)),
            guarded,
            audit);

    private static async Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest> ReadyRequestAsync(
        bool executeControlledMutation = false,
        bool dryRunOnly = true)
    {
        var reference = LegacyReference();
        var detail = await DetailAsync(reference);
        var preview = SuccessfulPreviewAudit(reference.FiscalIssuanceReferenceId);
        var actor = Guid.NewGuid();
        var approvalReference = "APPROVAL-2026-07-06-001";
        var dualControlReference = "DUAL-2026-07-06-001";
        var command = new FiscalExceptionSemanticHashControlledBackfillMutationCommand(
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            LatestRecalculationPreviewAuditId: preview.LastRecalculationPreviewAuditId,
            ApprovalBasisStatus: FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            StoredSourceVersion: FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            RequiredSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            RecalculatedHashValue: preview.RecalculatedHashValue!,
            RecalculatedHashAlgorithm: preview.RecalculatedHashAlgorithm!,
            RecalculatedHashSourceVersion: preview.RecalculatedHashSourceVersion!,
            RecalculatedSourceFactCount: preview.RecalculatedSourceFactCount!.Value,
            RecalculatedSafeSourceSummary: preview.RecalculatedSafeSourceSummary!,
            ActorServiceIdentityId: actor,
            ApprovalReference: approvalReference,
            DualControlReference: dualControlReference,
            CorrelationId: reference.CorrelationId,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            DryRunOnly: dryRunOnly,
            MutationStatus: FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus
                .PreparedForControlledMutation);
        var preparation = new FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult(
            Status: FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation,
            BlockReasonCode: null,
            SafeSummary: "semantic_hash_backfill_mutation_prepared_single_record_guarded_write_enabled",
            Command: command,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            MutationEnabled: true,
            DryRunOnly: dryRunOnly,
            AuditPersisted: true,
            FiscalIssuanceReferenceMutated: false,
            RetryExecutionAvailable: false,
            PosServerPostCalled: false,
            RetryExecuted: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            MutationAuditId: Guid.NewGuid(),
            MutationAttemptedAt: DateTimeOffset.Parse("2026-07-06T10:00:00+08:00"));

        return new FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest(
            Detail: detail,
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: preview.LastRecalculationPreviewAuditId,
            MutationPreparationAuditId: preparation.MutationAuditId,
            ActorServiceIdentityId: actor,
            ApprovalReference: approvalReference,
            DualControlReference: dualControlReference,
            ReasonCode: "semantic_hash_legacy_backfill_request",
            SafeJustification: "legacy semantic hash requires governed sha256:v1 metadata alignment",
            CorrelationId: reference.CorrelationId,
            ExecuteControlledMutation: executeControlledMutation,
            DryRunOnly: dryRunOnly,
            RequestedAt: DateTimeOffset.Parse("2026-07-06T10:05:00+08:00"),
            LatestRecalculationPreviewAuditSummary: preview,
            MutationPreparationBasis: preparation);
    }

    private static FiscalExceptionSemanticHashRecalculationPreviewAuditSummary SuccessfulPreviewAudit(
        Guid fiscalIssuanceReferenceId) =>
        new(
            LastRecalculationPreviewAuditId: Guid.NewGuid(),
            LastPreviewStatus: FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
            LastAttemptedAt: DateTimeOffset.Parse("2026-07-06T09:00:00+08:00"),
            AttemptCount: 1,
            LastBlockReasonCode: null,
            CompleteOriginalRequestFactsAvailable: true,
            RecalculatedHashValue: new string('d', 64),
            RecalculatedHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            RecalculatedHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            RecalculatedSourceFactCount: 20,
            RecalculatedSafeSourceSummary: $"semantic_request_hash_source_available:{fiscalIssuanceReferenceId:N}",
            RecalculatedHashMatchesStoredHash: false,
            MutationStatus: FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
            SafeSummary: "semantic_hash_recalculation_preview_calculated_not_mutated");

    private static async Task<FiscalExceptionQueueCaseDetail> DetailAsync(FiscalIssuanceReferenceRecord reference)
    {
        var detail = await new FiscalExceptionQueueService(new FakeReferenceReader([reference]))
            .GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        return detail!;
    }

    private static FiscalIssuanceReferenceRecord LegacyReference()
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
            RecordedByServiceIdentityId: Guid.NewGuid(),
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: new string('c', 64),
            SemanticRequestHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion:
                FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            SemanticRequestHashSourceFactCount: 42,
            SemanticRequestHashSafeSummary: "semantic_request_hash_source_available:facts=42");
    }

    private static void AssertNoExternalSideEffects(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowResult result,
        bool expectHashMutation = false)
    {
        result.FiscalIssuanceReferenceMutated.Should().Be(expectHashMutation);
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

    private sealed class FakeWorkflowAuditRepository
        : IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository
    {
        public List<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite> Records { get; } = [];

        public Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite attempt,
            CancellationToken cancellationToken)
        {
            Records.Add(attempt);
            return Task.FromResult(new FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord(
                WorkflowRequestId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                RecalculationPreviewAuditId: attempt.RecalculationPreviewAuditId,
                MutationPreparationAuditId: attempt.MutationPreparationAuditId,
                ApprovalReference: attempt.ApprovalReference,
                DualControlReference: attempt.DualControlReference,
                ActorServiceIdentityId: attempt.ActorServiceIdentityId,
                ReasonCode: attempt.ReasonCode,
                SafeJustification: attempt.SafeJustification,
                RequestMode: attempt.RequestMode,
                WorkflowStatus: attempt.WorkflowStatus,
                BlockReasonCode: attempt.BlockReasonCode,
                MutationInvocationPosture: attempt.MutationInvocationPosture,
                GuardedMutationAuditId: attempt.GuardedMutationAuditId,
                GuardedMutationStatus: attempt.GuardedMutationStatus,
                ExecuteControlledMutationRequested: attempt.ExecuteControlledMutationRequested,
                MutationInvocationEnabled: attempt.MutationInvocationEnabled,
                DryRunOnly: attempt.DryRunOnly,
                RequestedAt: attempt.RequestedAt,
                CorrelationId: attempt.CorrelationId,
                SafeSummary: attempt.SafeSummary,
                CreatedAt: attempt.RequestedAt));
        }

        public Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary?>(null);
    }

    private sealed class FakeGuardedMutationService : IFiscalExceptionSemanticHashGuardedBackfillMutationService
    {
        public List<FiscalExceptionSemanticHashGuardedBackfillMutationRequest> Requests { get; } = [];

        public Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
            FiscalExceptionSemanticHashGuardedBackfillMutationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(FiscalExceptionSemanticHashGuardedBackfillMutationService.Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated,
                blockReasonCode: null,
                "semantic_hash_guarded_backfill_mutated_single_record_semantic_metadata_only",
                mutationAuditId: Guid.NewGuid(),
                oldSourceVersion: FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
                newSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                oldHashValue: request.Detail.Summary.SemanticRequestHashValue,
                newHashValue: request.MutationPreparationBasis.Command!.RecalculatedHashValue,
                mutationTimestamp: request.RequestedAt,
                mutated: true));
        }
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
}
