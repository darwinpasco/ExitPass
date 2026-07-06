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

    private static FiscalExceptionQueueService QueueService(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionReadbackClassification readbackClassification)
    {
        var attempts = new FakeReadbackAttemptRepository(
            new FiscalExceptionReadbackAttemptSummary(
                Classification: readbackClassification,
                AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                AttemptCount: 1,
                SafeErrorSummary: readbackClassification.ToString()));

        return new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            attempts);
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
}
