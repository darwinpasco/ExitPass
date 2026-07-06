using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashBackfillInternalApiHandlerTests
{
    [Fact]
    public async Task RequestAsync_WhenApiIsDisabled_FailsClosed()
    {
        var context = await ReadyContextAsync(apiEnabled: false);

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.HttpStatusCode.Should().Be(403);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_internal_api_disabled");
        context.WorkflowAudit.Records.Should().BeEmpty();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenActorServiceIdentityIsMissing_BlocksThroughWorkflow()
    {
        var context = await ReadyContextAsync();
        var request = context.Request with { ActorServiceIdentityId = Guid.Empty };

        var result = await context.Sut.RequestAsync(request, CancellationToken.None);

        result.HttpStatusCode.Should().Be(409);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_actor_authorization_missing");
        context.WorkflowAudit.Records.Should().ContainSingle();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenApprovalReferenceIsMissing_BlocksThroughWorkflow()
    {
        var context = await ReadyContextAsync();
        var request = context.Request with { ApprovalReference = "" };

        var result = await context.Sut.RequestAsync(request, CancellationToken.None);

        result.BlockReasonCode.Should().Be("semantic_hash_backfill_explicit_approval_missing");
        context.WorkflowAudit.Records.Should().ContainSingle();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenRequiredDualControlReferenceIsMissing_BlocksThroughWorkflow()
    {
        var context = await ReadyContextAsync();
        var request = context.Request with { DualControlReference = null };

        var result = await context.Sut.RequestAsync(request, CancellationToken.None);

        result.BlockReasonCode.Should().Be("semantic_hash_backfill_dual_control_required");
        context.WorkflowAudit.Records.Should().ContainSingle();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenBatchPayloadIsAttempted_BlocksBeforeWorkflow()
    {
        var context = await ReadyContextAsync();
        var request = context.Request with
        {
            FiscalIssuanceReferenceIds = [context.Request.FiscalIssuanceReferenceId, Guid.NewGuid()]
        };

        var result = await context.Sut.RequestAsync(request, CancellationToken.None);

        result.HttpStatusCode.Should().Be(400);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_internal_api_batch_not_allowed");
        context.WorkflowAudit.Records.Should().BeEmpty();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenPreviewAuditIsMissing_BlocksBeforeWorkflow()
    {
        var context = await ReadyContextAsync(includePreview: false);

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.BlockReasonCode.Should().Be("semantic_hash_recalculation_preview_audit_missing");
        context.WorkflowAudit.Records.Should().BeEmpty();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenMutationPreparationAuditIsMissing_BlocksBeforeWorkflow()
    {
        var context = await ReadyContextAsync(includeMutationAudit: false);

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.BlockReasonCode.Should().Be("semantic_hash_backfill_mutation_preparation_audit_missing");
        context.WorkflowAudit.Records.Should().BeEmpty();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenDryRun_DoesNotMutateHashMetadata()
    {
        var context = await ReadyContextAsync();

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.WorkflowStatus.Should()
            .Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval);
        result.MutationInvocationPosture.Should()
            .Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.DryRunOnly);
        context.WorkflowAudit.Records.Should().ContainSingle();
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenExecuteIntentAndWorkflowInvocationDisabled_DoesNotInvokeMutation()
    {
        var context = await ReadyContextAsync(executeControlledMutation: true, dryRunOnly: false);

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.WorkflowStatus.Should()
            .Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.PreparedButMutationInvocationDisabled);
        result.BlockReasonCode.Should()
            .Be("semantic_hash_backfill_operator_workflow_mutation_invocation_disabled");
        context.GuardedMutation.Requests.Should().BeEmpty();
        AssertNoRetryOrExternalMutation(result);
    }

    [Fact]
    public async Task RequestAsync_WhenExecuteIntentAndWorkflowInvocationEnabled_CallsGuardedMutation()
    {
        var context = await ReadyContextAsync(
            executeControlledMutation: true,
            dryRunOnly: false,
            workflowMutationInvocationEnabled: true);

        var result = await context.Sut.RequestAsync(context.Request, CancellationToken.None);

        result.WorkflowStatus.Should().Be(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked);
        result.GuardedMutationStatus.Should()
            .Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated);
        context.GuardedMutation.Requests.Should().ContainSingle();
        result.RetryExecutionAvailable.Should().BeFalse();
    }

    private static async Task<TestContext> ReadyContextAsync(
        bool apiEnabled = true,
        bool includePreview = true,
        bool includeMutationAudit = true,
        bool executeControlledMutation = false,
        bool dryRunOnly = true,
        bool workflowMutationInvocationEnabled = false)
    {
        var reference = LegacyReference();
        var detail = await DetailAsync(reference);
        var preview = SuccessfulPreviewAudit(reference.FiscalIssuanceReferenceId);
        var actor = Guid.NewGuid();
        var approval = "APPROVAL-2026-07-06-001";
        var dualControl = "DUAL-2026-07-06-001";
        var mutationAudit = PreparedMutationAudit(
            reference,
            preview,
            actor,
            approval,
            dualControl);
        var workflowAudit = new FakeWorkflowAuditRepository();
        var guardedMutation = new FakeGuardedMutationService();
        var sut = new FiscalExceptionSemanticHashBackfillInternalApiHandler(
            new FiscalExceptionSemanticHashBackfillInternalApiOptions(apiEnabled),
            new FakeQueueService(detail),
            new FakePreviewAuditRepository(includePreview ? preview : null),
            new FakeMutationAuditRepository(includeMutationAudit ? mutationAudit : null),
            new FiscalExceptionSemanticHashBackfillOperatorWorkflowService(
                new FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions(
                    workflowMutationInvocationEnabled),
                new FiscalExceptionSemanticHashControlledBackfillApprovalService(
                    new FiscalExceptionSemanticHashControlledBackfillApprovalOptions(
                        approvalPolicyConfigured: true,
                        dualControlRequired: true,
                        dualControlSatisfied: true,
                        actorOrServiceAuthorized: true,
                        explicitApprovalPresent: true)),
                guardedMutation,
                workflowAudit));

        var request = new FiscalExceptionSemanticHashBackfillInternalApiRequest(
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: preview.LastRecalculationPreviewAuditId,
            MutationPreparationAuditId: mutationAudit.MutationAuditId,
            ApprovalReference: approval,
            DualControlReference: dualControl,
            ActorServiceIdentityId: actor,
            ReasonCode: "semantic_hash_legacy_backfill_request",
            SafeJustification: "legacy semantic hash requires governed sha256:v1 metadata alignment",
            CorrelationId: reference.CorrelationId,
            DryRunOnly: dryRunOnly,
            ExecuteControlledMutation: executeControlledMutation);

        return new TestContext(sut, request, workflowAudit, guardedMutation);
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord PreparedMutationAudit(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionSemanticHashRecalculationPreviewAuditSummary preview,
        Guid actor,
        string approval,
        string dualControl) =>
        new(
            MutationAuditId: Guid.NewGuid(),
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: preview.LastRecalculationPreviewAuditId,
            MutationPreparationAuditId: null,
            ApprovalBasisStatus:
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            OldSourceVersion: FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            RequiredSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            OldHashValue: reference.SemanticRequestHashValue,
            NewHashValue: preview.RecalculatedHashValue,
            NewHashAlgorithm: preview.RecalculatedHashAlgorithm,
            NewHashSourceVersion: preview.RecalculatedHashSourceVersion,
            NewHashSourceFactCount: preview.RecalculatedSourceFactCount,
            SafeSourceSummary: preview.RecalculatedSafeSourceSummary,
            MutationStatus:
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation,
            BlockReasonCode: null,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            MutationEnabled: true,
            FiscalIssuanceReferenceMutated: false,
            AttemptedAt: DateTimeOffset.Parse("2026-07-06T10:00:00+08:00"),
            SafeSummary: "semantic_hash_backfill_mutation_prepared_single_record_guarded_write_enabled",
            CorrelationId: reference.CorrelationId,
            ActorServiceIdentityId: actor,
            ApprovalReference: approval,
            DualControlReference: dualControl,
            CreatedAt: DateTimeOffset.Parse("2026-07-06T10:00:00+08:00"));

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

    private static void AssertNoRetryOrExternalMutation(
        FiscalExceptionSemanticHashBackfillInternalApiResponse result)
    {
        result.RetryExecutionAvailable.Should().BeFalse();
        result.GuardedMutationAuditId.Should().BeNull();
    }

    private sealed record TestContext(
        FiscalExceptionSemanticHashBackfillInternalApiHandler Sut,
        FiscalExceptionSemanticHashBackfillInternalApiRequest Request,
        FakeWorkflowAuditRepository WorkflowAudit,
        FakeGuardedMutationService GuardedMutation);

    private sealed class FakeQueueService : IFiscalExceptionQueueService
    {
        private readonly FiscalExceptionQueueCaseDetail _detail;

        public FakeQueueService(FiscalExceptionQueueCaseDetail detail)
        {
            _detail = detail;
        }

        public Task<IReadOnlyList<FiscalExceptionQueueCaseSummary>> ListAsync(
            FiscalExceptionQueueQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalExceptionQueueCaseSummary>>([_detail.Summary]);

        public Task<FiscalExceptionQueueCaseDetail?> GetAsync(
            Guid caseId,
            CancellationToken cancellationToken) =>
            Task.FromResult(caseId == _detail.Summary.FiscalIssuanceReferenceId ? _detail : null);

        public Task<FiscalExceptionQueueCaseDetail> CreateOrUpdateFromFiscalReferenceAsync(
            FiscalIssuanceReferenceRecord source,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("internal API handler tests are read-only");

        public Task<FiscalExceptionReadbackPreparation?> PrepareReadbackAsync(
            Guid caseId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("internal API handler tests do not prepare readback");
    }

    private sealed class FakePreviewAuditRepository :
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
    {
        private readonly FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? _summary;

        public FakePreviewAuditRepository(FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? summary)
        {
            _summary = summary;
        }

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("internal API handler tests do not write preview audit");

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_summary);
    }

    private sealed class FakeMutationAuditRepository :
        IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository
    {
        private readonly FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord? _record;

        public FakeMutationAuditRepository(
            FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord? record)
        {
            _record = record;
        }

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("internal API handler tests do not write mutation audit");

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord?> GetRecordAsync(
            Guid mutationAuditId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.MutationAuditId == mutationAuditId ? _record : null);

        public Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?>(null);
    }

    private sealed class FakeWorkflowAuditRepository :
        IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository
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
