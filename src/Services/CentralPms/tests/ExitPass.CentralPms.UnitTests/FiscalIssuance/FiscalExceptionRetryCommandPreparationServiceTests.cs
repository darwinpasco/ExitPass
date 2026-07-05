using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionRetryCommandPreparationServiceTests
{
    [Fact]
    public async Task Prepare_WhenRetryEligibilityIsNotEligible_BlocksCommand()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("readback_attempt_history_missing");
        result.Command.Should().BeNull();
        result.RetryScheduled.Should().BeFalse();
        result.PosServerPostCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(FiscalExceptionReadbackClassification.Matched, "readback_matched")]
    [InlineData(FiscalExceptionReadbackClassification.Mismatch, "readback_mismatch")]
    [InlineData(FiscalExceptionReadbackClassification.Failed, "readback_failed")]
    [InlineData(FiscalExceptionReadbackClassification.Unavailable, "readback_unavailable")]
    [InlineData(FiscalExceptionReadbackClassification.Unknown, "readback_unknown")]
    [InlineData(FiscalExceptionReadbackClassification.IdentifierMissing, "readback_identifier_missing")]
    [InlineData(FiscalExceptionReadbackClassification.NotSupportedYet, "readback_not_supported_yet")]
    public async Task Prepare_WhenLatestReadbackIsNotNotFound_BlocksCommand(
        FiscalExceptionReadbackClassification classification,
        string expectedReason)
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference, classification);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().NotBe(FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable);
        result.BlockReasonCode.Should().Be(expectedReason);
        result.Command.Should().BeNull();
        result.RetryScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_WhenNoDurableReadbackAttemptExists_BlocksCommand()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("readback_attempt_history_missing");
    }

    [Fact]
    public async Task Prepare_WhenUpstreamFinalityContextIsMissing_BlocksCommand()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            UpstreamFinalityReference = " "
        };
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("upstream_finality_reference_missing");
        result.IdempotencyContextAvailabilityStatus
            .Should().Be(FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference);
    }

    [Fact]
    public async Task Prepare_WhenNewUpstreamFinalityReferenceIsRequested_BlocksBypass()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(
            new FiscalExceptionRetryCommandPreparationRequest(
                detail!,
                RequestedUpstreamFinalityReference: $"CPS-POS-UAT:{Guid.NewGuid():N}"));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("new_upstream_finality_reference_rejected");
        result.IdempotencyContextAvailabilityStatus
            .Should().Be(FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected);
    }

    [Fact]
    public async Task Prepare_WhenSemanticRequestHashIsMissing_ReturnsUnavailableWithoutInventingHash()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Unavailable);
        result.BlockReasonCode.Should().Be("semantic_request_hash_required_but_missing");
        result.SemanticRequestHashAvailabilityStatus
            .Should().Be(FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing);
        result.Command.Should().BeNull();
        result.PosServerPostCalled.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_WhenSemanticRequestHashIsConfirmed_PreparesNonExecutableEnvelope()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        detail = detail! with
        {
            Summary = detail.Summary with
            {
                SemanticRequestHashAvailabilityStatus =
                    FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed
            }
        };
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable);
        result.BlockReasonCode.Should().BeNull();
        result.Command.Should().NotBeNull();
        result.Command!.Executable.Should().BeFalse();
        result.Command.UpstreamFinalityReference.Should().Be(reference.UpstreamFinalityReference);
        result.Command.SemanticRequestHashAvailabilityStatus
            .Should().Be(FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed);
        result.PosServerPostCalled.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview, FiscalIssuanceExceptionReason.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict, FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled, null)]
    public async Task Prepare_WhenFiscalExceptionStateIsUnsafe_BlocksCommand(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceExceptionReason? reason)
    {
        var reference = Reference(state, reason);
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(new FiscalExceptionRetryCommandPreparationRequest(detail!));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.Command.Should().BeNull();
        result.RetryScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_WhenTreatedAsExecutableButExecutionUnavailable_BlocksCommand()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);
        var sut = new FiscalExceptionRetryCommandPreparationService();

        var result = sut.Prepare(
            new FiscalExceptionRetryCommandPreparationRequest(
                detail!,
                TreatAsExecutableCommand: true));

        result.Status.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_execution_not_available");
        result.Command.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenCommandPreparationPostureIsEvaluated_ReturnsSafeReadOnlyPosture()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);

        var detail = await DetailAsync(reference, FiscalExceptionReadbackClassification.NotFound);

        detail.Should().NotBeNull();
        detail!.Summary.RetryEligibilityDecision.Should().Be(FiscalExceptionRetryEligibilityDecision.Eligible);
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.Summary.RetryCommandPreparationStatus.Should().Be(FiscalExceptionRetryCommandPreparationStatus.Unavailable);
        detail.Summary.RetryCommandBlockReasonCode.Should().Be("semantic_request_hash_required_but_missing");
        detail.Summary.SemanticRequestHashAvailabilityStatus
            .Should().Be(FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing);
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
    }

    [Fact]
    public void RetryCommandPreparation_DoesNotDependOnPosServerSchedulerPaymentExitOrGate()
    {
        var constructorParameters = typeof(FiscalExceptionRetryCommandPreparationService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().BeEmpty();

        var resultProperties = typeof(FiscalExceptionRetryCommandPreparationResult)
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.PropertyType);

        resultProperties.Should().ContainKey(nameof(FiscalExceptionRetryCommandPreparationResult.PosServerPostCalled));
        resultProperties.Should().ContainKey(nameof(FiscalExceptionRetryCommandPreparationResult.RetryScheduled));
        resultProperties.Should().ContainKey(nameof(FiscalExceptionRetryCommandPreparationResult.PaymentFinalityChanged));
        resultProperties.Should().ContainKey(nameof(FiscalExceptionRetryCommandPreparationResult.ExitAuthorizationIssued));
        resultProperties.Should().ContainKey(nameof(FiscalExceptionRetryCommandPreparationResult.GateBehaviorTriggered));

        var fiscalIssuanceTypes = typeof(FiscalExceptionRetryCommandPreparationService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionRetryCommandPreparationService).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryScheduler", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<FiscalExceptionQueueCaseDetail?> DetailAsync(
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionReadbackClassification? classification = null)
    {
        var attempts = new FakeReadbackAttemptRepository();
        if (classification is not null)
        {
            attempts.Seed(
                new FiscalExceptionReadbackAttemptSummary(
                    Classification: classification.Value,
                    AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                    AttemptCount: 1,
                    SafeErrorSummary: classification.Value.ToString()));
        }

        var service = new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            attempts);

        return await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);
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
            throw new NotSupportedException("retry command preparation tests are read-only");

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
