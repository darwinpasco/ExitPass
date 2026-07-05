using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionRetryEligibilityEvaluatorTests
{
    [Fact]
    public async Task GetAsync_WhenNoReadbackAttemptHistory_BlocksRetry()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var sut = CreateService(reference);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback);
        detail.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Blocked);
        detail.Summary.RetryBlockReasonCode.Should().Be("readback_attempt_history_missing");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(FiscalExceptionReadbackClassification.Matched, FiscalExceptionRetryEligibilityStatus.BlockedReadbackMatched, "readback_matched")]
    [InlineData(FiscalExceptionReadbackClassification.Failed, FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed, "readback_failed")]
    [InlineData(FiscalExceptionReadbackClassification.Unavailable, FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed, "readback_unavailable")]
    [InlineData(FiscalExceptionReadbackClassification.Unknown, FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed, "readback_unknown")]
    [InlineData(FiscalExceptionReadbackClassification.IdentifierMissing, FiscalExceptionRetryEligibilityStatus.BlockedIdentifierMissing, "readback_identifier_missing")]
    public async Task GetAsync_WhenLatestReadbackClassificationIsUnsafe_BlocksRetry(
        FiscalExceptionReadbackClassification classification,
        FiscalExceptionRetryEligibilityStatus expectedStatus,
        string expectedReason)
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var sut = CreateService(reference, classification);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(expectedStatus);
        detail.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Blocked);
        detail.Summary.RetryBlockReasonCode.Should().Be(expectedReason);
        detail.Summary.RetryEligibilityBasedOnReadbackClassification.Should().Be(classification);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReadbackMismatchAndManualReview_PostureRemainsManualReviewAndRetryBlocked()
    {
        var reference = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
            FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
            "fiscal_reference_mismatch");
        var sut = CreateService(reference, FiscalExceptionReadbackClassification.Mismatch);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.QueueState.Should().Be(FiscalExceptionQueueState.ManualReviewRequired);
        detail.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedManualReview);
        detail.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Blocked);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReadbackNotSupported_ReturnsUnavailableWithoutRetryExecution()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var sut = CreateService(reference, FiscalExceptionReadbackClassification.NotSupportedYet);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedReadbackUnsupported);
        detail.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Unavailable);
        detail.Summary.RetryBlockReasonCode.Should().Be("readback_not_supported_yet");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReadbackNotFoundButRequestContextMissing_BlocksRetry()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PaymentAttemptId = Guid.Empty
        };
        var sut = CreateService(reference, FiscalExceptionReadbackClassification.NotFound);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedMissingRequestContext);
        detail.Summary.RetryBlockReasonCode.Should().Be("original_request_context_missing");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReadbackNotFoundButUpstreamFinalityMissing_BlocksRetry()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            UpstreamFinalityReference = " "
        };
        var sut = CreateService(reference, FiscalExceptionReadbackClassification.NotFound);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedMissingUpstreamFinalityReference);
        detail.Summary.RetryBlockReasonCode.Should().Be("upstream_finality_reference_missing");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReadbackNotFoundAndModeledPrerequisitesAreSafe_ReturnsEligibleWithoutExecution()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var sut = CreateService(reference, FiscalExceptionReadbackClassification.NotFound);

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning);
        detail.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Eligible);
        detail.Summary.RetryBlockReasonCode.Should().BeNull();
        detail.Summary.SafeRetryEligibilitySummary.Should().Be("retry_eligible_for_controlled_retry_planning_no_execution");
        detail.Summary.RetryEligibilityBasedOnReadbackClassification.Should().Be(FiscalExceptionReadbackClassification.NotFound);
        detail.Summary.RetryEligibilityEvaluatedAt.Should().NotBeNull();
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public void FiscalExceptionRetryEligibilityEvaluator_DoesNotDependOnPosServerPaymentExitGateOrRetryExecution()
    {
        var constructorParameters = typeof(FiscalExceptionRetryEligibilityEvaluator)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().BeEmpty();

        var fiscalIssuanceTypes = typeof(FiscalExceptionRetryEligibilityEvaluator).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionRetryEligibilityEvaluator).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryScheduler", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalExceptionQueueService CreateService(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionReadbackClassification? readbackClassification = null)
    {
        var attempts = new FakeReadbackAttemptRepository();
        if (readbackClassification is not null)
        {
            attempts.Seed(
                new FiscalExceptionReadbackAttemptSummary(
                    Classification: readbackClassification.Value,
                    AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                    AttemptCount: 1,
                    SafeErrorSummary: readbackClassification.Value.ToString()));
        }

        return new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            attempts,
            new FiscalExceptionRetryEligibilityEvaluator());
    }

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceExceptionReason? reason = null,
        string? errorCode = null)
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
            LatestErrorCode: errorCode ?? "post_timeout",
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
        private FiscalExceptionReadbackAttemptSummary? _summary;

        public Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
            FiscalExceptionReadbackAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("retry eligibility tests are read-only");

        public Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_summary);

        public void Seed(FiscalExceptionReadbackAttemptSummary summary)
        {
            _summary = summary;
        }
    }
}
