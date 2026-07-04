using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionReadbackWorkerTests
{
    [Fact]
    public async Task RunReadbackAsync_WhenIdentifierMissing_ReturnsIdentifierMissingWithoutPosServerCall()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = null
        };
        var client = new FakeReadbackClient(supportsReadback: true);
        var sut = CreateWorker([reference], client);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.IdentifierMissing);
        result.PosServerReadbackCallAttempted.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReadbackAsync_WhenSafeGetReadbackUnsupported_ReturnsNotSupportedYet()
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var client = new FakeReadbackClient(supportsReadback: false);
        var sut = CreateWorker([reference], client);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.NotSupportedYet);
        result.PosServerReadbackCallAttempted.Should().BeFalse();
        result.RetryScheduled.Should().BeFalse();
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReadbackAsync_WhenReadbackMatches_ClassifiesMatchedWithoutStateMutationOrRetry()
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var client = new FakeReadbackClient(
            supportsReadback: true,
            ReadResult(fiscalDocumentId));
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateWorker([reference], client, orchestration);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.Matched);
        result.PosServerReadbackCallAttempted.Should().BeTrue();
        result.RetryScheduled.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        result.UpdatedCase.Should().NotBeNull();
        result.UpdatedCase!.FiscalNumberEditingAllowed.Should().BeFalse();
        result.UpdatedCase.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
        _ = orchestration.DidNotReceiveWithAnyArgs().ApplyReadbackPlanningResultAsync(default, default!, default);
    }

    [Fact]
    public async Task RunReadbackAsync_WhenReadbackNotFound_UpdatesUnknownPostureAndDoesNotRetry()
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var updated = reference with
        {
            LatestExceptionReason = FiscalIssuanceExceptionReason.GetReadbackNotFound,
            LatestErrorCode = "get_readback_not_found",
            LastUpdatedAt = reference.LastUpdatedAt.AddMinutes(1)
        };
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        orchestration.ApplyReadbackPlanningResultAsync(
                reference.FiscalIssuanceReferenceId,
                Arg.Is<FiscalIssuanceReadbackPlanningResult>(result =>
                    result.Outcome == FiscalIssuanceReadbackPlanningOutcome.NotFound),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updated));
        var client = new FakeReadbackClient(
            supportsReadback: true,
            ReadResult(null, succeeded: false, httpStatusCode: 404, code: "fiscal_document_not_found"));
        var sut = CreateWorker([reference], client, orchestration);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.NotFound);
        result.RetryScheduled.Should().BeFalse();
        result.UpdatedCase!.Summary.ReadbackClassification.Should().Be(FiscalExceptionReadbackClassification.NotFound);
    }

    [Fact]
    public async Task RunReadbackAsync_WhenReadbackMismatches_RoutesToManualReviewAndDoesNotRetry()
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var updated = reference with
        {
            FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
            LatestExceptionReason = FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
            LatestErrorCode = "fiscal_reference_mismatch",
            LatestErrorPosture = FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
            LastUpdatedAt = reference.LastUpdatedAt.AddMinutes(1)
        };
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        orchestration.ApplyReadbackPlanningResultAsync(
                reference.FiscalIssuanceReferenceId,
                Arg.Is<FiscalIssuanceReadbackPlanningResult>(result =>
                    result.Outcome == FiscalIssuanceReadbackPlanningOutcome.Mismatch),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updated));
        var client = new FakeReadbackClient(
            supportsReadback: true,
            ReadResult(Guid.NewGuid()));
        var sut = CreateWorker([reference], client, orchestration);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.Mismatch);
        result.RetryScheduled.Should().BeFalse();
        result.UpdatedCase!.Summary.QueueState.Should().Be(FiscalExceptionQueueState.ManualReviewRequired);
        result.UpdatedCase.Summary.RetryEligibilityStatus.Should().Be(FiscalExceptionRetryEligibilityStatus.BlockedManualReview);
    }

    [Theory]
    [InlineData(503, "pos_server_unavailable", FiscalExceptionReadbackClassification.Unavailable)]
    [InlineData(500, "invalid_json_response", FiscalExceptionReadbackClassification.Unknown)]
    [InlineData(400, "readback_failed", FiscalExceptionReadbackClassification.Unknown)]
    public async Task RunReadbackAsync_WhenReadbackIsNotConclusive_DoesNotRetry(
        int httpStatusCode,
        string code,
        FiscalExceptionReadbackClassification expectedClassification)
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var updated = reference with
        {
            LatestExceptionReason = expectedClassification == FiscalExceptionReadbackClassification.Unavailable
                ? FiscalIssuanceExceptionReason.GetReadbackServiceFailed
                : FiscalIssuanceExceptionReason.GetReadbackInconclusive,
            LatestErrorCode = expectedClassification == FiscalExceptionReadbackClassification.Unavailable
                ? "get_readback_service_failed"
                : "get_readback_inconclusive",
            LastUpdatedAt = reference.LastUpdatedAt.AddMinutes(1)
        };
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        orchestration.ApplyReadbackPlanningResultAsync(
                reference.FiscalIssuanceReferenceId,
                Arg.Any<FiscalIssuanceReadbackPlanningResult>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updated));
        var client = new FakeReadbackClient(
            supportsReadback: true,
            ReadResult(null, succeeded: false, httpStatusCode: httpStatusCode, code: code));
        var sut = CreateWorker([reference], client, orchestration);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(expectedClassification);
        result.RetryScheduled.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task RunReadbackAsync_WhenReadbackFails_DoesNotRetry()
    {
        var fiscalDocumentId = Guid.NewGuid();
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            PosServerFiscalDocumentId = fiscalDocumentId
        };
        var updated = reference with
        {
            LatestExceptionReason = FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            LatestErrorCode = "get_readback_service_failed",
            LastUpdatedAt = reference.LastUpdatedAt.AddMinutes(1)
        };
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        orchestration.ApplyReadbackPlanningResultAsync(
                reference.FiscalIssuanceReferenceId,
                Arg.Is<FiscalIssuanceReadbackPlanningResult>(result =>
                    result.Outcome == FiscalIssuanceReadbackPlanningOutcome.ServiceFailed),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updated));
        var client = new FakeReadbackClient(
            supportsReadback: true,
            new PosServerFiscalDocumentReadResult(
                Outcome: PosServerFiscalDocumentOutcome.FailedService,
                Succeeded: false,
                HttpStatusCode: 500,
                Code: "readback_failed",
                Message: "readback_failed",
                FiscalDocumentId: null,
                FiscalIssuanceEvidenceStatus: null,
                FiscalNumberAssignmentState: null,
                FiscalDocumentStatusCodeId: null));
        var sut = CreateWorker([reference], client, orchestration);

        var result = await sut.RunReadbackAsync(reference.FiscalIssuanceReferenceId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Classification.Should().Be(FiscalExceptionReadbackClassification.Failed);
        result.RetryScheduled.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task FeqReadOnlyDetail_WhenReferenceHasReadbackReason_IncludesReadbackStatus()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            LatestExceptionReason = FiscalIssuanceExceptionReason.GetReadbackNotFound,
            LatestErrorCode = "get_readback_not_found"
        };
        var service = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var detail = await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.ReadbackStatus.Should().Be(FiscalExceptionReadbackStatus.Attempted);
        detail.Summary.ReadbackClassification.Should().Be(FiscalExceptionReadbackClassification.NotFound);
        detail.Summary.LastReadbackAttemptAt.Should().Be(reference.LastUpdatedAt);
    }

    [Fact]
    public void FiscalExceptionReadbackWorkerSlice_DoesNotIntroduceRetrySchedulerOrExecutionEndpoint()
    {
        var fiscalIssuanceTypes = typeof(FiscalExceptionReadbackWorker).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionReadbackWorker).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryScheduler", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalExceptionReadbackWorker CreateWorker(
        IReadOnlyList<FiscalIssuanceReferenceRecord> references,
        IFiscalExceptionReadbackClient client,
        IFiscalIssuanceOrchestrationService? orchestration = null)
    {
        var queueService = new FiscalExceptionQueueService(new FakeReferenceReader(references));
        return new FiscalExceptionReadbackWorker(
            queueService,
            client,
            orchestration ?? Substitute.For<IFiscalIssuanceOrchestrationService>(),
            Substitute.For<ILogger<FiscalExceptionReadbackWorker>>());
    }

    private static PosServerFiscalDocumentReadResult ReadResult(
        Guid? fiscalDocumentId,
        bool succeeded = true,
        int httpStatusCode = 200,
        string code = "ok") =>
        new(
            Outcome: succeeded ? PosServerFiscalDocumentOutcome.Accepted : PosServerFiscalDocumentOutcome.InvalidResponse,
            Succeeded: succeeded,
            HttpStatusCode: httpStatusCode,
            Code: code,
            Message: code,
            FiscalDocumentId: fiscalDocumentId,
            FiscalIssuanceEvidenceStatus: succeeded ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned : null,
            FiscalNumberAssignmentState: succeeded ? FiscalNumberAssignmentState.Assigned : null,
            FiscalDocumentStatusCodeId: succeeded ? Guid.Parse("33333333-3333-3333-3333-333333333333") : null);

    private static FiscalIssuanceReferenceRecord Reference(FiscalIssuanceIntegrationState state)
    {
        var now = DateTimeOffset.Parse("2026-07-04T10:00:00+08:00");
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
            FiscalDocumentStatusCodeId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid());
    }

    private sealed class FakeReadbackClient : IFiscalExceptionReadbackClient
    {
        private readonly PosServerFiscalDocumentReadResult? _result;

        public FakeReadbackClient(
            bool supportsReadback,
            PosServerFiscalDocumentReadResult? result = null)
        {
            SupportsFiscalDocumentIdReadback = supportsReadback;
            _result = result;
        }

        public bool SupportsFiscalDocumentIdReadback { get; }

        public int CallCount { get; private set; }

        public Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
            Guid fiscalDocumentId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result ?? ReadResult(fiscalDocumentId));
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
