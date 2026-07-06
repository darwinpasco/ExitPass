using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashRecalculationPreviewServiceTests
{
    private readonly PosServerFiscalDocumentRequestMapper _mapper = new();
    private readonly FiscalExceptionSemanticHashRecalculationPreviewService _sut = new();

    [Fact]
    public void Preview_WhenLegacyHashHasNoOriginalRequestFacts_BlocksWithRecalculationRequiredPosture()
    {
        var reference = LegacyReference();

        var result = _sut.Preview(new FiscalExceptionSemanticHashRecalculationPreviewRequest(reference));

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        result.BlockReasonCode.Should().Be(
            FiscalExceptionSemanticHashRecalculationPreviewService.OriginalFiscalRequestFactsUnavailableReason);
        result.StoredSourceVersion.Should().Be(FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion);
        result.RequiredSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.CompleteOriginalFiscalRequestFactsAvailable.Should().BeFalse();
        result.RecalculatedHashValue.Should().BeNull();
        result.MutationStatus.Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
    }

    [Fact]
    public void Preview_WhenOriginalRequestFactsAreIncomplete_DoesNotProduceFakeHash()
    {
        var reference = LegacyReference();
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()) with
        {
            DocumentLines = Array.Empty<PosServerFiscalDocumentLineRequest>(),
            Lines = Array.Empty<PosServerFiscalDocumentLineRequest>()
        };

        var result = _sut.Preview(new FiscalExceptionSemanticHashRecalculationPreviewRequest(
            reference,
            request));

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        result.BlockReasonCode.Should().Be("document_line_required");
        result.CompleteOriginalFiscalRequestFactsAvailable.Should().BeFalse();
        result.RecalculatedHashValue.Should().BeNull();
        result.RecalculatedHashAlgorithm.Should().BeNull();
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
    }

    [Fact]
    public void Preview_WhenCompleteOriginalRequestFactsAreAvailable_CalculatesNonMutatingPreviewHash()
    {
        var reference = LegacyReference();
        var originalHashValue = reference.SemanticRequestHashValue;
        var originalSourceVersion = reference.SemanticRequestHashSourceVersion;
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var result = _sut.Preview(new FiscalExceptionSemanticHashRecalculationPreviewRequest(
            reference,
            request,
            RequestedAt: DateTimeOffset.Parse("2026-07-06T08:00:00+08:00")));

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated);
        result.BlockReasonCode.Should().BeNull();
        result.CompleteOriginalFiscalRequestFactsAvailable.Should().BeTrue();
        result.RecalculatedHashValue.Should().MatchRegex("^[0-9a-f]{64}$");
        result.RecalculatedHashAlgorithm.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm);
        result.RecalculatedHashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.RecalculatedSourceFactCount.Should().BeGreaterThan(0);
        result.RecalculatedSafeSourceSummary.Should().Be("semantic_request_hash_source_available:facts=20");
        result.RecalculatedHashMatchesStoredHash.Should().BeFalse();
        result.PreviewAttemptedAt.Should().Be(DateTimeOffset.Parse("2026-07-06T08:00:00+08:00"));
        reference.SemanticRequestHashValue.Should().Be(originalHashValue);
        reference.SemanticRequestHashSourceVersion.Should().Be(originalSourceVersion);
    }

    [Fact]
    public void Preview_WhenHashIsAlreadyCurrent_DoesNotRequireRecalculation()
    {
        var reference = CurrentReference();

        var result = _sut.Preview(new FiscalExceptionSemanticHashRecalculationPreviewRequest(reference));

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.NotRequired);
        result.BlockReasonCode.Should().BeNull();
        result.SafeSummary.Should().Be("semantic_hash_recalculation_preview_not_required_current_hash");
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
    }

    [Fact]
    public void Preview_DoesNotPerformRetryExecutionOrFiscalSideEffects()
    {
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var result = _sut.Preview(new FiscalExceptionSemanticHashRecalculationPreviewRequest(
            LegacyReference(),
            request));

        result.PosServerPostCalled.Should().BeFalse();
        result.RetryExecuted.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        result.FiscalNumberEdited.Should().BeFalse();
        result.ManualFiscalDocumentCreated.Should().BeFalse();
        result.FiscalIssuanceReferenceMutated.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_WhenLegacyHashHasNoOriginalRequestFacts_PersistsBlockedAuditRecord()
    {
        var audit = new FakeRecalculationPreviewAuditRepository();
        var sut = new FiscalExceptionSemanticHashRecalculationPreviewService(audit);
        var reference = LegacyReference();

        var result = await sut.PreviewAsync(
            new FiscalExceptionSemanticHashRecalculationPreviewRequest(reference),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        result.RecalculationPreviewAuditId.Should().NotBeNull();
        audit.Records.Should().ContainSingle();
        audit.Records[0].PreviewStatus.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        audit.Records[0].BlockReasonCode.Should().Be(
            FiscalExceptionSemanticHashRecalculationPreviewService.OriginalFiscalRequestFactsUnavailableReason);
        audit.Records[0].StoredSemanticHashValue.Should().Be(reference.SemanticRequestHashValue);
        audit.Records[0].MutationStatus.Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
        audit.Records[0].CompleteOriginalRequestFactsAvailable.Should().BeFalse();
        audit.Records[0].RecalculatedHashValue.Should().BeNull();
    }

    [Fact]
    public async Task PreviewAsync_WhenOriginalRequestFactsAreIncomplete_PersistsBlockedAuditWithoutFakeHash()
    {
        var audit = new FakeRecalculationPreviewAuditRepository();
        var sut = new FiscalExceptionSemanticHashRecalculationPreviewService(audit);
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()) with
        {
            DocumentLines = Array.Empty<PosServerFiscalDocumentLineRequest>(),
            Lines = Array.Empty<PosServerFiscalDocumentLineRequest>()
        };

        var result = await sut.PreviewAsync(
            new FiscalExceptionSemanticHashRecalculationPreviewRequest(LegacyReference(), request),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        result.BlockReasonCode.Should().Be("document_line_required");
        audit.Records.Should().ContainSingle();
        audit.Records[0].RecalculatedHashValue.Should().BeNull();
        audit.Records[0].CompleteOriginalRequestFactsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_WhenCompleteOriginalRequestFactsAreAvailable_PersistsPreviewHashAuditRecord()
    {
        var audit = new FakeRecalculationPreviewAuditRepository();
        var sut = new FiscalExceptionSemanticHashRecalculationPreviewService(audit);
        var reference = LegacyReference();
        var originalHashValue = reference.SemanticRequestHashValue;
        var originalSourceVersion = reference.SemanticRequestHashSourceVersion;
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var result = await sut.PreviewAsync(
            new FiscalExceptionSemanticHashRecalculationPreviewRequest(reference, request),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated);
        result.RecalculationPreviewAuditId.Should().NotBeNull();
        audit.Records.Should().ContainSingle();
        audit.Records[0].PreviewStatus.Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated);
        audit.Records[0].RecalculatedHashValue.Should().Be(result.RecalculatedHashValue);
        audit.Records[0].RecalculatedHashAlgorithm.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm);
        audit.Records[0].RecalculatedHashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        audit.Records[0].RecalculatedHashMatchesStoredHash.Should().BeFalse();
        reference.SemanticRequestHashValue.Should().Be(originalHashValue);
        reference.SemanticRequestHashSourceVersion.Should().Be(originalSourceVersion);
    }

    [Fact]
    public async Task PreviewAsync_WhenAuditPersistenceFails_DoesNotPretendPreviewIsDurablyAuditable()
    {
        var audit = new FakeRecalculationPreviewAuditRepository { ThrowOnRecord = true };
        var sut = new FiscalExceptionSemanticHashRecalculationPreviewService(audit);

        var act = async () => await sut.PreviewAsync(
            new FiscalExceptionSemanticHashRecalculationPreviewRequest(LegacyReference()),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("semantic_hash_recalculation_preview_audit_persistence_failed");
    }

    [Fact]
    public async Task GetAsync_WhenLegacyHashIsPersisted_ReturnsSafeRecalculationPreviewPosture()
    {
        var reference = LegacyReference();
        var sut = QueueService(reference, FiscalExceptionReadbackClassification.NotFound);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.SemanticHashReadinessStatus
            .Should().Be(FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired);
        detail.Summary.SemanticHashRecalculationPreviewStatus
            .Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked);
        detail.Summary.SemanticHashRecalculationPreviewBlockReasonCode
            .Should().Be(FiscalExceptionSemanticHashRecalculationPreviewService.OriginalFiscalRequestFactsUnavailableReason);
        detail.Summary.SemanticHashRecalculationPreviewStoredSourceVersion
            .Should().Be(FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion);
        detail.Summary.SemanticHashRecalculationPreviewRequiredSourceVersion
            .Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        detail.Summary.SemanticHashRecalculationMutationStatus
            .Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
        detail.Summary.RetryEligibilityStatus
            .Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady);
        detail.Summary.RetryBlockReasonCode
            .Should().Be("semantic_hash_legacy_version_requires_recalculation");
        detail.Summary.RetryCommandPreparationStatus
            .Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenRecalculationPreviewAuditExists_ReturnsLastAuditSummary()
    {
        var reference = LegacyReference();
        var audit = new FakeRecalculationPreviewAuditRepository();
        await audit.RecordAsync(
            new FiscalExceptionSemanticHashRecalculationPreviewAuditWrite(
                FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
                StoredSemanticHashSourceVersion: reference.SemanticRequestHashSourceVersion,
                RequiredSemanticHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                StoredSemanticHashValue: reference.SemanticRequestHashValue,
                PreviewStatus: FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
                BlockReasonCode: null,
                CompleteOriginalRequestFactsAvailable: true,
                RecalculatedHashValue: new string('d', 64),
                RecalculatedHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
                RecalculatedHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                RecalculatedSourceFactCount: 20,
                RecalculatedSafeSourceSummary: "semantic_request_hash_source_available:facts=20",
                RecalculatedHashMatchesStoredHash: false,
                MutationStatus: FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
                AttemptedAt: DateTimeOffset.Parse("2026-07-06T09:00:00+08:00"),
                SafeSummary: "semantic_hash_recalculation_preview_calculated_not_mutated",
                CorrelationId: reference.CorrelationId,
                ServiceIdentityId: reference.RecordedByServiceIdentityId),
            CancellationToken.None);
        var sut = QueueService(reference, FiscalExceptionReadbackClassification.NotFound, audit);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.SemanticHashRecalculationPreviewStatus
            .Should().Be(FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated);
        detail.Summary.SemanticHashRecalculationPreviewAttemptedAt
            .Should().Be(DateTimeOffset.Parse("2026-07-06T09:00:00+08:00"));
        detail.Summary.SemanticHashRecalculationPreviewAttemptCount.Should().Be(1);
        detail.Summary.SafeSemanticHashRecalculationPreviewSummary
            .Should().Be("semantic_hash_recalculation_preview_calculated_not_mutated");
        detail.Summary.SemanticHashRecalculationMutationStatus
            .Should().Be(FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated);
        detail.Summary.RetryEligibilityStatus
            .Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    private static FiscalExceptionQueueService QueueService(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionReadbackClassification readbackClassification,
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository? auditRepository = null)
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
            new FiscalExceptionRetrySchedulingPreparationService(),
            retrySchedulingPreparationAuditRepository: null,
            new FiscalExceptionRetryExecutionPreparationService(),
            new FiscalExceptionPosServerRetryContractReadinessService(),
            auditRepository);
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
            throw new NotSupportedException("semantic hash recalculation preview tests are read-only");

        public Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FiscalExceptionReadbackAttemptSummary?>(_summary);
    }

    private sealed class FakeRecalculationPreviewAuditRepository :
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
    {
        public List<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> Records { get; } = [];

        public bool ThrowOnRecord { get; init; }

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> RecordAsync(
            FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRecord)
            {
                throw new InvalidOperationException("audit_write_failed");
            }

            var record = new FiscalExceptionSemanticHashRecalculationPreviewAuditRecord(
                RecalculationPreviewAuditId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                StoredSemanticHashSourceVersion: attempt.StoredSemanticHashSourceVersion,
                RequiredSemanticHashSourceVersion: attempt.RequiredSemanticHashSourceVersion,
                StoredSemanticHashValue: attempt.StoredSemanticHashValue,
                PreviewStatus: attempt.PreviewStatus,
                BlockReasonCode: attempt.BlockReasonCode,
                CompleteOriginalRequestFactsAvailable: attempt.CompleteOriginalRequestFactsAvailable,
                RecalculatedHashValue: attempt.RecalculatedHashValue,
                RecalculatedHashAlgorithm: attempt.RecalculatedHashAlgorithm,
                RecalculatedHashSourceVersion: attempt.RecalculatedHashSourceVersion,
                RecalculatedSourceFactCount: attempt.RecalculatedSourceFactCount,
                RecalculatedSafeSourceSummary: attempt.RecalculatedSafeSourceSummary,
                RecalculatedHashMatchesStoredHash: attempt.RecalculatedHashMatchesStoredHash,
                MutationStatus: attempt.MutationStatus,
                AttemptedAt: attempt.AttemptedAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CreatedAt: attempt.AttemptedAt);

            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
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
                return Task.FromResult<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?>(null);
            }

            var latest = matching[0];
            return Task.FromResult<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?>(
                new FiscalExceptionSemanticHashRecalculationPreviewAuditSummary(
                    LastRecalculationPreviewAuditId: latest.RecalculationPreviewAuditId,
                    LastPreviewStatus: latest.PreviewStatus,
                    LastAttemptedAt: latest.AttemptedAt,
                    AttemptCount: matching.Length,
                    LastBlockReasonCode: latest.BlockReasonCode,
                    CompleteOriginalRequestFactsAvailable: latest.CompleteOriginalRequestFactsAvailable,
                    RecalculatedHashValue: latest.RecalculatedHashValue,
                    RecalculatedHashAlgorithm: latest.RecalculatedHashAlgorithm,
                    RecalculatedHashSourceVersion: latest.RecalculatedHashSourceVersion,
                    RecalculatedSourceFactCount: latest.RecalculatedSourceFactCount,
                    RecalculatedSafeSourceSummary: latest.RecalculatedSafeSourceSummary,
                    RecalculatedHashMatchesStoredHash: latest.RecalculatedHashMatchesStoredHash,
                    MutationStatus: latest.MutationStatus,
                    SafeSummary: latest.SafeSummary));
        }
    }
}
