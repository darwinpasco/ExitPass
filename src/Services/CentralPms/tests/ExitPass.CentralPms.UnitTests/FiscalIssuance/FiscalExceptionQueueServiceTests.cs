using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionQueueServiceTests
{
    [Fact]
    public async Task ListAsync_WhenUnknownOutcomeExists_ReturnsReadbackRequiredCaseWithoutRetry()
    {
        var reference = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            FiscalIssuanceExceptionReason.PostTimeout,
            "post_timeout");
        var sut = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var cases = await sut.ListAsync(new FiscalExceptionQueueQuery(), CancellationToken.None);

        cases.Should().ContainSingle();
        var feqCase = cases.Single();
        feqCase.CaseId.Should().Be(reference.FiscalIssuanceReferenceId);
        feqCase.Category.Should().Be(FiscalExceptionQueueCategory.PosServerTimeout);
        feqCase.QueueState.Should().Be(FiscalExceptionQueueState.ReadbackRequired);
        feqCase.ReadbackStatus.Should().Be(FiscalExceptionReadbackStatus.RequiredNotStarted);
        feqCase.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback);
        feqCase.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CreateOrUpdateFromFiscalReferenceAsync_WhenRepeatedForSameReference_CollapsesToSameCase()
    {
        var reference = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
            FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            "get_readback_service_failed");
        var sut = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var first = await sut.CreateOrUpdateFromFiscalReferenceAsync(reference, CancellationToken.None);
        var second = await sut.CreateOrUpdateFromFiscalReferenceAsync(
            reference with { LastUpdatedAt = reference.LastUpdatedAt.AddMinutes(5) },
            CancellationToken.None);

        second.Summary.CaseId.Should().Be(first.Summary.CaseId);
        second.Summary.DuplicateCollapseKey.Should().Be(first.Summary.DuplicateCollapseKey);
        second.Summary.DuplicateCollapseStrategy.Should().Be("source_fiscal_issuance_reference_identity");
    }

    [Fact]
    public async Task GetAsync_WhenCaseExists_ReturnsSafeReadOnlyDetail()
    {
        var reference = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
            FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
            "fiscal_reference_mismatch") with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalDocumentNumber = "DEV-SI-00000001"
        };
        var sut = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var detail = await sut.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.Category.Should().Be(FiscalExceptionQueueCategory.FiscalMismatch);
        detail.Summary.QueueState.Should().Be(FiscalExceptionQueueState.ManualReviewRequired);
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
        detail.FiscalDocumentNumber.Should().Be("DEV-SI-00000001");
    }

    [Fact]
    public async Task PrepareReadbackAsync_WhenUnknownOutcome_PreparesContractWithoutCallingPosServerOrRetry()
    {
        var reference = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit,
            "network_disconnect_after_possible_commit");
        var sut = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var preparation = await sut.PrepareReadbackAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        preparation.Should().NotBeNull();
        preparation!.ReadbackRequired.Should().BeTrue();
        preparation.ReadbackStatus.Should().Be(FiscalExceptionReadbackStatus.PendingFutureSlice);
        preparation.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback);
        preparation.RetryExecutionAvailable.Should().BeFalse();
        preparation.PosServerReadbackCallPerformed.Should().BeFalse();
        preparation.PreparationStatus.Should().Be("readback_contract_prepared_no_pos_server_call");
    }

    [Fact]
    public async Task ListAsync_WhenRecordedReferenceExists_DoesNotExposeItAsFeqCase()
    {
        var recorded = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        var failed = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
            FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound,
            "fiscal_sequence_policy_not_found");
        var sut = new FiscalExceptionQueueService(new FakeReferenceReader([recorded, failed]));

        var cases = await sut.ListAsync(new FiscalExceptionQueueQuery(), CancellationToken.None);

        cases.Should().ContainSingle();
        cases.Single().CaseId.Should().Be(failed.FiscalIssuanceReferenceId);
        cases.Single().Category.Should().Be(FiscalExceptionQueueCategory.FiscalConfigurationMissing);
        cases.Single().RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedConfiguration);
    }

    [Fact]
    public void FiscalExceptionQueueSlice_DoesNotIntroduceRetrySchedulerOrExecutionEndpoint()
    {
        var fiscalIssuanceTypes = typeof(FiscalExceptionQueueService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionQueueService).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryScheduler", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FiscalExceptionQueueService_DoesNotDependOnPaymentExitOrGateServices()
    {
        var constructorParameters = typeof(FiscalExceptionQueueService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().ContainSingle(nameof(IFiscalExceptionQueueReferenceReader));
        constructorParameters.Should().NotContain(parameter =>
            parameter.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Gate", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceExceptionReason? reason = null,
        string? errorCode = null)
    {
        var now = DateTimeOffset.Parse("2026-07-03T10:00:00+08:00");
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
            FiscalDocumentStatusCodeId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: reason,
            LatestErrorCode: errorCode,
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
}

