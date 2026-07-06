using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashGuardedBackfillMutationServiceTests
{
    [Fact]
    public async Task MutateAsync_WhenMutationDisabledByDefault_DoesNotUpdateReference()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        var repository = new FakeGuardedBackfillRepository();
        var sut = new FiscalExceptionSemanticHashGuardedBackfillMutationService(
            new FiscalExceptionSemanticHashControlledBackfillMutationOptions(),
            repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled);
        result.BlockReasonCode.Should().Be("semantic_hash_controlled_backfill_mutation_disabled");
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenApprovalIsNotReady_Blocks()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        request = request with
        {
            ApprovalBasis = request.ApprovalBasis with
            {
                Status = FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                BlockReasonCode = "semantic_hash_backfill_explicit_approval_missing",
                ExplicitApprovalPresent = false
            }
        };
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_explicit_approval_missing");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenMutationPrepAuditIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        request = request with
        {
            MutationPreparationBasis = request.MutationPreparationBasis with
            {
                MutationAuditId = null,
                AuditPersisted = false
            }
        };
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_mutation_preparation_audit_missing");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenActorApprovalOrDualControlIsMissing_Blocks()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        request = request with
        {
            ActorServiceIdentityId = Guid.Empty,
            ApprovalReference = "",
            DualControlReference = null
        };
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_actor_authorization_missing");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenDryRunOnly_DoesNotUpdateReference()
    {
        var request = await ReadyRequestAsync(dryRunOnly: true);
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled);
        result.BlockReasonCode.Should().Be("semantic_hash_controlled_backfill_mutation_dry_run_only");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenPreviewAuditBasisDiffers_FailsClosedAsStale()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        request = request with
        {
            ApprovalBasis = request.ApprovalBasis with
            {
                LatestRecalculationPreviewAuditId = Guid.NewGuid()
            }
        };
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale);
        result.BlockReasonCode.Should().Be("semantic_hash_recalculation_preview_audit_basis_mismatch");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenMutationPreparationBasisDiffers_FailsClosedAsStale()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        request = request with
        {
            MutationPreparationBasis = request.MutationPreparationBasis with
            {
                Command = request.MutationPreparationBasis.Command! with
                {
                    RecalculatedHashValue = new string('e', 64)
                }
            }
        };
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale);
        result.BlockReasonCode.Should().Be("semantic_hash_backfill_mutation_preparation_basis_mismatch");
        repository.Commands.Should().BeEmpty();
        AssertNoRetryOrExternalSideEffects(result);
    }

    [Fact]
    public async Task MutateAsync_WhenAllGatesPass_DelegatesSingleRecordMutation()
    {
        var request = await ReadyRequestAsync(dryRunOnly: false);
        var repository = new FakeGuardedBackfillRepository();
        var sut = EnabledSut(repository);

        var result = await sut.MutateAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated);
        result.FiscalIssuanceReferenceMutated.Should().BeTrue();
        result.RetryExecutionAvailable.Should().BeFalse();
        repository.Commands.Should().ContainSingle();
        repository.Commands[0].FiscalIssuanceReferenceId.Should()
            .Be(request.Detail.Summary.FiscalIssuanceReferenceId);
        repository.Commands[0].MutationPreparationAuditId.Should()
            .Be(request.MutationPreparationBasis.MutationAuditId!.Value);
        repository.Commands[0].NewHashSourceVersion.Should()
            .Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        AssertNoRetryOrExternalSideEffects(result, expectMutation: true);
    }

    private static FiscalExceptionSemanticHashGuardedBackfillMutationService EnabledSut(
        FakeGuardedBackfillRepository repository) =>
        new(
            new FiscalExceptionSemanticHashControlledBackfillMutationOptions(enableControlledMutation: true),
            repository);

    private static async Task<FiscalExceptionSemanticHashGuardedBackfillMutationRequest> ReadyRequestAsync(
        bool dryRunOnly)
    {
        var reference = LegacyReference();
        var detail = await DetailAsync(reference);
        var preview = SuccessfulPreviewAudit();
        var actorServiceIdentityId = Guid.NewGuid();
        var approvalReference = "APPROVAL-2026-07-06-001";
        var dualControlReference = "DUAL-2026-07-06-001";
        var approval = ReadyApproval(preview);
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
            ActorServiceIdentityId: actorServiceIdentityId,
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

        return new FiscalExceptionSemanticHashGuardedBackfillMutationRequest(
            detail,
            approval,
            preview,
            preparation,
            actorServiceIdentityId,
            approvalReference,
            dualControlReference,
            DateTimeOffset.Parse("2026-07-06T10:05:00+08:00"));
    }

    private static FiscalExceptionSemanticHashControlledBackfillApprovalResult ReadyApproval(
        FiscalExceptionSemanticHashRecalculationPreviewAuditSummary preview) =>
        new(
            Status: FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            BlockReasonCode: null,
            SafeSummary: "semantic_hash_controlled_backfill_preconditions_ready_not_mutated",
            LegacySourceVersion: FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
            RequiredSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            LatestRecalculationPreviewAuditId: preview.LastRecalculationPreviewAuditId,
            LatestRecalculationPreviewAttemptedAt: preview.LastAttemptedAt,
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

    private static FiscalExceptionSemanticHashRecalculationPreviewAuditSummary SuccessfulPreviewAudit() =>
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
            RecalculatedSafeSourceSummary: "semantic_request_hash_source_available:facts=20",
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

    private static void AssertNoRetryOrExternalSideEffects(
        FiscalExceptionSemanticHashGuardedBackfillMutationResult result,
        bool expectMutation = false)
    {
        result.FiscalIssuanceReferenceMutated.Should().Be(expectMutation);
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

    private sealed class FakeGuardedBackfillRepository : IFiscalExceptionSemanticHashGuardedBackfillMutationRepository
    {
        public List<FiscalExceptionSemanticHashGuardedBackfillMutationCommand> Commands { get; } = [];

        public Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
            FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(FiscalExceptionSemanticHashGuardedBackfillMutationService.Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated,
                blockReasonCode: null,
                "semantic_hash_guarded_backfill_mutated_single_record_semantic_metadata_only",
                mutationAuditId: Guid.NewGuid(),
                oldSourceVersion: command.ExpectedOldSourceVersion,
                newSourceVersion: command.NewHashSourceVersion,
                oldHashValue: command.OldHashValue,
                newHashValue: command.NewHashValue,
                mutationTimestamp: command.AttemptedAt,
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
