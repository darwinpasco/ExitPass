using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionPosServerRetryContractReadinessServiceTests
{
    [Fact]
    public async Task Evaluate_WhenSemanticHashUsesPosServerSha256V1Contract_ReturnsReady()
    {
        var detail = await DetailAsync(
            semanticHashAlgorithm: "sha256",
            semanticHashSourceVersion: FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(new FiscalExceptionPosServerRetryContractReadinessRequest(detail));

        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Ready);
        result.SemanticHashCompatibilityStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Ready);
        result.IdempotencyMappingStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Ready);
        result.RetryExecutionAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_WhenCentralPmsHashSourceCannotProvePosServerByteCompatibility_ReturnsUnconfirmed()
    {
        var detail = await DetailAsync(
            semanticHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            semanticHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(new FiscalExceptionPosServerRetryContractReadinessRequest(detail));

        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed);
        result.SemanticHashCompatibilityStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_compatibility_unconfirmed");
    }

    [Theory]
    [InlineData(null, "sha256:v1")]
    [InlineData("sha256", null)]
    public async Task Evaluate_WhenSemanticHashVersionOrSourceIsMissing_ReturnsBlocked(
        string? algorithm,
        string? sourceVersion)
    {
        var detail = await DetailAsync(
            semanticHashAlgorithm: algorithm,
            semanticHashSourceVersion: sourceVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(new FiscalExceptionPosServerRetryContractReadinessRequest(detail));

        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.SemanticHashCompatibilityStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_required_but_missing_or_unconfirmed");
    }

    [Fact]
    public async Task Evaluate_WhenRequestedUpstreamFinalityReferenceIsSame_ReturnsIdempotencyReady()
    {
        var detail = await DetailAsync(
            semanticHashAlgorithm: "sha256",
            semanticHashSourceVersion: FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(
            new FiscalExceptionPosServerRetryContractReadinessRequest(
                detail,
                RequestedUpstreamFinalityReference: detail.Summary.UpstreamFinalityReference));

        result.IdempotencyMappingStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Ready);
        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Ready);
    }

    [Fact]
    public async Task Evaluate_WhenUpstreamFinalityReferenceIsMissing_BlocksIdempotencyMapping()
    {
        var detail = await DetailAsync(
            upstreamFinalityReference: "",
            semanticHashAlgorithm: "sha256",
            semanticHashSourceVersion: FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(new FiscalExceptionPosServerRetryContractReadinessRequest(detail));

        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.IdempotencyMappingStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.BlockReasonCode.Should().Be("pos_server_idempotency_mapping_not_compatible");
    }

    [Fact]
    public async Task Evaluate_WhenNewUpstreamFinalityReferenceIsRequested_BlocksIdempotencyMapping()
    {
        var detail = await DetailAsync(
            semanticHashAlgorithm: "sha256",
            semanticHashSourceVersion: FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion);
        var sut = new FiscalExceptionPosServerRetryContractReadinessService();

        var result = sut.Evaluate(
            new FiscalExceptionPosServerRetryContractReadinessRequest(
                detail,
                RequestedUpstreamFinalityReference: $"new-upstream-{Guid.NewGuid():N}"));

        result.Status.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.IdempotencyMappingStatus.Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Blocked);
        result.BlockReasonCode.Should().Be("pos_server_idempotency_mapping_not_compatible");
    }

    [Fact]
    public async Task GetAsync_WhenCurrentCentralPmsHashCannotProvePosServerCompatibility_ReturnsSafeReadinessPosture()
    {
        var reference = Reference(
            upstreamFinalityReference: $"CPS-POS-UAT:{Guid.NewGuid():N}",
            semanticHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            semanticHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        var service = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));

        var detail = await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.PosServerRetryContractReadinessStatus
            .Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed);
        detail.Summary.PosServerSemanticHashCompatibilityStatus
            .Should().Be(FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed);
        detail.Summary.PosServerRetryContractBlockReasonCode
            .Should().Be("pos_server_semantic_hash_compatibility_unconfirmed");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
    }

    private static async Task<FiscalExceptionQueueCaseDetail> DetailAsync(
        string upstreamFinalityReference = "CPS-POS-UAT:ready",
        string? semanticHashAlgorithm = "sha256",
        string? semanticHashSourceVersion = "sha256:v1")
    {
        var reference = Reference(upstreamFinalityReference, semanticHashAlgorithm, semanticHashSourceVersion);
        var service = new FiscalExceptionQueueService(new FakeReferenceReader([reference]));
        return (await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None))!;
    }

    private static FiscalIssuanceReferenceRecord Reference(
        string upstreamFinalityReference,
        string? semanticHashAlgorithm,
        string? semanticHashSourceVersion)
    {
        var now = DateTimeOffset.Parse("2026-07-06T09:00:00+08:00");
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
            UpstreamFinalityReference: upstreamFinalityReference,
            PosServerFiscalDocumentId: Guid.NewGuid(),
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
            SemanticRequestHashValue: new string('a', 64),
            SemanticRequestHashAlgorithm: semanticHashAlgorithm,
            SemanticRequestHashSourceVersion: semanticHashSourceVersion,
            SemanticRequestHashSourceFactCount: 42,
            SemanticRequestHashSafeSummary: "semantic_request_hash_source_available:facts=42");
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
