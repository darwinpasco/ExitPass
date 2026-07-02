using System.Net.Http;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceOrchestrationServiceTests
{
    [Fact]
    public async Task PreparePendingAsync_WhenCommandIsValid_PersistsPendingFiscalIssuance()
    {
        var repository = new InMemoryFiscalIssuanceReferenceRepository();
        var sut = new FiscalIssuanceOrchestrationService(repository);

        var result = await sut.PreparePendingAsync(ValidPrepareCommand(), CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.PendingFiscalIssuance);
        result.FiscalNumberAssignmentState.Should().Be(FiscalNumberAssignmentState.NotAssigned);
        result.UpstreamFinalityReference.Should().NotBeNullOrWhiteSpace();
        repository.CreatedRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task PreparePendingAsync_WhenUpstreamFinalityReferenceIsMissing_FailsValidation()
    {
        var sut = new FiscalIssuanceOrchestrationService(new InMemoryFiscalIssuanceReferenceRepository());
        var command = ValidPrepareCommand() with { UpstreamFinalityReference = "" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.PreparePendingAsync(command, CancellationToken.None));

        ex.Message.Should().Contain("upstream_finality_reference_required");
    }

    [Fact]
    public async Task PreparePendingAsync_WhenPaymentConfirmationIdIsMissing_FailsValidation()
    {
        var sut = new FiscalIssuanceOrchestrationService(new InMemoryFiscalIssuanceReferenceRepository());
        var command = ValidPrepareCommand() with { PaymentConfirmationId = Guid.Empty };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.PreparePendingAsync(command, CancellationToken.None));

        ex.Message.Should().Contain("payment_confirmation_id_required");
    }

    [Fact]
    public async Task MarkRequestedAsync_WhenPendingExists_TransitionsToRequested()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkRequestedAsync(
            reference.FiscalIssuanceReferenceId,
            TransitionContext(),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceRequested);
        result.FiscalNumberAssignmentState.Should().Be(FiscalNumberAssignmentState.NotAssigned);
    }

    [Fact]
    public async Task MarkFailedRequestAsync_WhenReasonProvided_MapsToFailedRequest()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkFailedRequestAsync(
            reference.FiscalIssuanceReferenceId,
            FailureContext(FiscalIssuanceExceptionReason.RequestConstructionError),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest);
        result.LatestExceptionReason.Should().Be(FiscalIssuanceExceptionReason.RequestConstructionError);
    }

    [Fact]
    public async Task MarkFailedConfigurationAsync_WhenReasonProvided_MapsToFailedConfiguration()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkFailedConfigurationAsync(
            reference.FiscalIssuanceReferenceId,
            FailureContext(
                FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound,
                FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration);
        result.LatestExceptionReason.Should().Be(FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound);
        result.LatestErrorPosture.Should().Be(FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection);
    }

    [Fact]
    public async Task MarkFailedServiceAsync_WhenServiceRecoveryPostureProvided_MapsToFailedService()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkFailedServiceAsync(
            reference.FiscalIssuanceReferenceId,
            FailureContext(
                FiscalIssuanceExceptionReason.PersistenceWriteFailed,
                FiscalIssuanceErrorPosture.RetryAfterServiceRecovery),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService);
        result.LatestErrorPosture.Should().Be(FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
    }

    [Fact]
    public async Task MarkUnknownAsync_WhenTimeoutReasonProvided_MapsToUnknown()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkUnknownAsync(
            reference.FiscalIssuanceReferenceId,
            FailureContext(FiscalIssuanceExceptionReason.PostTimeout),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        result.LatestExceptionReason.Should().Be(FiscalIssuanceExceptionReason.PostTimeout);
    }

    [Fact]
    public async Task MarkConflictAsync_WhenCalled_MapsToConflictAndDoNotRetryPosture()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkConflictAsync(
            reference.FiscalIssuanceReferenceId,
            FailureContext(null, null),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceConflict);
        result.LatestExceptionReason.Should().Be(FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict);
        result.LatestErrorPosture.Should().Be(FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange);
    }

    [Fact]
    public async Task MarkManualReviewRequiredAsync_WhenReasonIsMissing_FailsValidation()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.MarkManualReviewRequiredAsync(
                reference.FiscalIssuanceReferenceId,
                FailureContext(null, null),
                CancellationToken.None));

        ex.Message.Should().Contain("Fiscal issuance exception reason is required");
    }

    [Fact]
    public async Task MarkRecordedAsync_WhenEvidenceIsComplete_TransitionsToRecordedAndGatingReady()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkRecordedAsync(
            reference.FiscalIssuanceReferenceId,
            CompleteEvidence(),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(result).Should().BeTrue();
    }

    [Fact]
    public async Task MarkReplayedAsync_WhenEvidenceIsComplete_TransitionsToReplayedAndGatingReady()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();

        var result = await sut.MarkReplayedAsync(
            reference.FiscalIssuanceReferenceId,
            CompleteEvidence(),
            CancellationToken.None);

        result.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed);
        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.IdempotentReplay);
        FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(result).Should().BeTrue();
    }

    [Fact]
    public async Task MarkRecordedAsync_WhenEvidenceIsIncomplete_FailsValidation()
    {
        var (sut, reference) = await CreatePreparedServiceAsync();
        var incompleteEvidence = CompleteEvidence() with { FiscalDocumentNumber = null };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.MarkRecordedAsync(
                reference.FiscalIssuanceReferenceId,
                incompleteEvidence,
                CancellationToken.None));

        ex.Message.Should().Contain("fiscal_document_number_required");
    }

    [Fact]
    public async Task IsNormalExitAuthorizationGatingReady_WhenEvidenceIsIncomplete_ReturnsFalse()
    {
        var repository = new InMemoryFiscalIssuanceReferenceRepository();
        var sut = new FiscalIssuanceOrchestrationService(repository);
        var reference = await sut.PreparePendingAsync(ValidPrepareCommand(), CancellationToken.None);

        FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(reference).Should().BeFalse();
    }

    [Fact]
    public void FiscalIssuanceOrchestrationService_DoesNotIntroducePosServerNetworkDependencies()
    {
        var constructorParameters = typeof(FiscalIssuanceOrchestrationService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().ContainSingle(type => type == typeof(IFiscalIssuanceReferenceRepository));
        constructorParameters.Should().NotContain(type =>
            type == typeof(HttpClient) ||
            type.Name.Contains("Client", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Mapper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IssueExitAuthorizationHandler_DoesNotDependOnFiscalIssuanceOrchestrationYet()
    {
        var constructorParameters = typeof(IssueExitAuthorizationHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().NotContain(typeof(IFiscalIssuanceOrchestrationService));
        constructorParameters.Should().NotContain(typeof(IFiscalIssuanceReferenceRepository));
    }

    private static async Task<(FiscalIssuanceOrchestrationService Service, FiscalIssuanceReferenceRecord Reference)>
        CreatePreparedServiceAsync()
    {
        var repository = new InMemoryFiscalIssuanceReferenceRepository();
        var sut = new FiscalIssuanceOrchestrationService(repository);
        var reference = await sut.PreparePendingAsync(ValidPrepareCommand(), CancellationToken.None);
        return (sut, reference);
    }

    private static PrepareFiscalIssuanceCommand ValidPrepareCommand() =>
        new(
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "site-pos-server-main",
            FiscalDocumentTypeCodeId: Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            PayableBasisRef: "tariff-snapshot-ref",
            UpstreamFinalityReference: $"upstream-finality-{Guid.NewGuid():N}",
            CorrelationId: Guid.NewGuid(),
            ServiceIdentityId: Guid.NewGuid());

    private static FiscalIssuanceTransitionContext TransitionContext() =>
        new(CorrelationId: Guid.NewGuid(), ServiceIdentityId: Guid.NewGuid());

    private static FiscalIssuanceFailureTransitionContext FailureContext(
        FiscalIssuanceExceptionReason? reason,
        FiscalIssuanceErrorPosture? posture = FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange) =>
        new(
            ExceptionReason: reason,
            ErrorCode: reason?.ToString(),
            ErrorPosture: posture,
            CorrelationId: Guid.NewGuid(),
            ServiceIdentityId: Guid.NewGuid());

    private static FiscalIssuanceEvidenceInput CompleteEvidence() =>
        new(
            PosServerFiscalDocumentId: Guid.NewGuid(),
            FiscalIdentityId: Guid.NewGuid(),
            FiscalSequencePolicyId: Guid.NewGuid(),
            FiscalSequenceValue: 10001,
            FiscalDocumentNumber: "SI-010001",
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-02T10:30:00+08:00"),
            FiscalNumberAssignedByRef: "pos-server-runtime",
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            ServiceIdentityId: Guid.NewGuid());

    private sealed class InMemoryFiscalIssuanceReferenceRepository : IFiscalIssuanceReferenceRepository
    {
        private readonly Dictionary<Guid, FiscalIssuanceReferenceRecord> _records = new();

        public List<CreateFiscalIssuanceReferenceRequest> CreatedRequests { get; } = new();

        public Task<FiscalIssuanceReferenceRecord> CreateAsync(
            CreateFiscalIssuanceReferenceRequest request,
            CancellationToken cancellationToken)
        {
            var validationErrors = request.Validate();
            validationErrors.Should().BeEmpty();

            CreatedRequests.Add(request);
            var now = DateTimeOffset.UtcNow;
            var record = new FiscalIssuanceReferenceRecord(
                FiscalIssuanceReferenceId: Guid.NewGuid(),
                PaymentConfirmationId: request.PaymentConfirmationId,
                PaymentAttemptId: request.PaymentAttemptId,
                ParkingSessionId: request.ParkingSessionId,
                TariffSnapshotId: request.TariffSnapshotId,
                SiteId: request.SiteId,
                SitePosServerId: request.SitePosServerId,
                SitePosServerRef: request.SitePosServerRef,
                PayableBasisRef: request.PayableBasisRef,
                UpstreamFinalityReference: request.UpstreamFinalityReference,
                PosServerFiscalDocumentId: request.PosServerFiscalDocumentId,
                FiscalIdentityId: request.FiscalIdentityId,
                FiscalSequencePolicyId: request.FiscalSequencePolicyId,
                FiscalSequenceValue: request.FiscalSequenceValue,
                FiscalDocumentNumber: request.FiscalDocumentNumber,
                FiscalSeries: request.FiscalSeries,
                FiscalNumberPrefixText: request.FiscalNumberPrefixText,
                FiscalNumberSuffixText: request.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: request.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: request.FiscalNumberAssignedByRef,
                FiscalDocumentStatusCodeId: request.FiscalDocumentStatusCodeId,
                ResultClassification: request.ResultClassification,
                FiscalIssuanceEvidenceStatus: request.FiscalIssuanceEvidenceStatus,
                FiscalNumberAssignmentState: request.FiscalNumberAssignmentState,
                FiscalIssuanceState: request.FiscalIssuanceState,
                LatestExceptionReason: request.LatestExceptionReason,
                LatestErrorCode: request.LatestErrorCode,
                LatestErrorPosture: request.LatestErrorPosture,
                CorrelationId: request.CorrelationId,
                PosServerResponseTimestamp: request.PosServerResponseTimestamp,
                FirstRecordedAt: now,
                LastUpdatedAt: now,
                RecordedByServiceIdentityId: request.RecordedByServiceIdentityId);

            _records[record.FiscalIssuanceReferenceId] = record;
            return Task.FromResult(record);
        }

        public Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(
            Guid fiscalIssuanceReferenceId,
            FiscalIssuanceStateTransitionRequest request,
            CancellationToken cancellationToken)
        {
            var validationErrors = request.Validate();
            validationErrors.Should().BeEmpty();

            if (!_records.TryGetValue(fiscalIssuanceReferenceId, out var existing))
            {
                throw new InvalidOperationException("Fiscal issuance reference was not found.");
            }

            var updated = existing with
            {
                PosServerFiscalDocumentId = request.PosServerFiscalDocumentId,
                FiscalIdentityId = request.FiscalIdentityId,
                FiscalSequencePolicyId = request.FiscalSequencePolicyId,
                FiscalSequenceValue = request.FiscalSequenceValue,
                FiscalDocumentNumber = request.FiscalDocumentNumber,
                FiscalSeries = request.FiscalSeries,
                FiscalNumberPrefixText = request.FiscalNumberPrefixText,
                FiscalNumberSuffixText = request.FiscalNumberSuffixText,
                FiscalNumberAssignedAt = request.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef = request.FiscalNumberAssignedByRef,
                FiscalDocumentStatusCodeId = request.FiscalDocumentStatusCodeId,
                ResultClassification = request.ResultClassification,
                FiscalIssuanceEvidenceStatus = request.FiscalIssuanceEvidenceStatus,
                FiscalNumberAssignmentState = request.FiscalNumberAssignmentState,
                FiscalIssuanceState = request.FiscalIssuanceState,
                LatestExceptionReason = request.LatestExceptionReason,
                LatestErrorCode = request.LatestErrorCode,
                LatestErrorPosture = request.LatestErrorPosture,
                CorrelationId = request.CorrelationId ?? existing.CorrelationId,
                PosServerResponseTimestamp = request.PosServerResponseTimestamp,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };

            _records[fiscalIssuanceReferenceId] = updated;
            return Task.FromResult(updated);
        }

        public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(
            Guid paymentConfirmationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.Values.SingleOrDefault(record => record.PaymentConfirmationId == paymentConfirmationId));

        public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(
            string upstreamFinalityReference,
            Guid? sitePosServerId,
            Guid? fiscalDocumentTypeCodeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.Values.SingleOrDefault(record =>
                record.UpstreamFinalityReference == upstreamFinalityReference &&
                (sitePosServerId is null || record.SitePosServerId == sitePosServerId)));

        public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(
            Guid posServerFiscalDocumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.Values.SingleOrDefault(record =>
                record.PosServerFiscalDocumentId == posServerFiscalDocumentId));
    }
}
