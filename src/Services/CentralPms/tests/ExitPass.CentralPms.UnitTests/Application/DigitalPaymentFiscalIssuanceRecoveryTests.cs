using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class DigitalPaymentFiscalIssuanceRecoveryTests
{
    private static readonly Guid ConfirmationId = Guid.Parse("75000000-0000-4000-8000-000000000001");
    private static readonly Guid AttemptId = Guid.Parse("75000000-0000-4000-8000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("75000000-0000-4000-8000-000000000003");
    private static readonly Guid TariffId = Guid.Parse("75000000-0000-4000-8000-000000000004");
    private static readonly Guid SiteId = Guid.Parse("75000000-0000-4000-8000-000000000005");
    private static readonly Guid PosId = Guid.Parse("75000000-0000-4000-8000-000000000006");
    private static readonly Guid FiscalReferenceId = Guid.Parse("75000000-0000-4000-8000-000000000007");

    [Fact]
    public async Task RetryableExistingReference_RetriesSameFiscalReferenceAndSitePosRoute()
    {
        var contextReader = Substitute.For<IDigitalPaymentFiscalContextReader>();
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var existing = Reference(
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
            FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
        references.FindByPaymentConfirmationIdAsync(ConfirmationId, Arg.Any<CancellationToken>())
            .Returns(existing);
        contextReader.ReadAsync(AttemptId, ConfirmationId, SessionId, Arg.Any<CancellationToken>())
            .Returns(Context());
        posServer.TryIssueFiscalDocumentViaPosServerAsync(
                FiscalReferenceId,
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.ConfigurationInvalid(["pos_temporarily_unavailable"]));

        var sut = new DigitalPaymentFiscalIssuanceService(contextReader, references, orchestration, posServer);

        await sut.IssueOrReadAsync(Command(), CancellationToken.None);

        await posServer.Received(1).TryIssueFiscalDocumentViaPosServerAsync(
            FiscalReferenceId,
            Arg.Is<CentralPmsFiscalDocumentMappingContext>(value =>
                value.SitePosServerId == PosId && value.SitePosServerRef == "IST-POS-A"),
            Arg.Any<PosServerCreateResultRecordingContext>(),
            Arg.Any<CancellationToken>());
        await orchestration.DidNotReceiveWithAnyArgs().PreparePendingAsync(default!, default);
    }

    [Fact]
    public async Task RetryableExistingReference_WhenSitePosRouteChanged_FailsBeforePosCall()
    {
        var contextReader = Substitute.For<IDigitalPaymentFiscalContextReader>();
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        references.FindByPaymentConfirmationIdAsync(ConfirmationId, Arg.Any<CancellationToken>())
            .Returns(Reference(
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
                FiscalIssuanceErrorPosture.RetryAfterServiceRecovery));
        contextReader.ReadAsync(AttemptId, ConfirmationId, SessionId, Arg.Any<CancellationToken>())
            .Returns(Context() with { SitePosServerId = Guid.NewGuid(), SitePosServerRef = "IST-POS-B" });
        var sut = new DigitalPaymentFiscalIssuanceService(contextReader, references, orchestration, posServer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("digital_payment_fiscal_routing_context_mismatch", error.Message);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default,
            default!,
            default!,
            default);
    }

    [Fact]
    public async Task RecordedExistingReference_ReturnsWithoutRetry()
    {
        var contextReader = Substitute.For<IDigitalPaymentFiscalContextReader>();
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var recorded = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, null) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 1,
            FiscalDocumentNumber = "SI-A-000001",
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned
        };
        references.FindByPaymentConfirmationIdAsync(ConfirmationId, Arg.Any<CancellationToken>())
            .Returns(recorded);
        var sut = new DigitalPaymentFiscalIssuanceService(contextReader, references, orchestration, posServer);

        var result = await sut.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.True(result.ReadyForExitAuthorization);
        await contextReader.DidNotReceiveWithAnyArgs().ReadAsync(default, default, default, default);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(
            default,
            default!,
            default!,
            default);
    }

    private static DigitalPaymentFiscalIssuanceCommand Command() =>
        new(AttemptId, ConfirmationId, SessionId, "IST-PROVIDER-RECOVERY", Guid.NewGuid(), null);

    private static DigitalPaymentFiscalContext Context() =>
        new(SiteId, TariffId, 12500, "PHP", DateTimeOffset.Parse("2026-08-24T01:00:00Z"), PosId, "IST-POS-A");

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceErrorPosture? posture) =>
        new(
            FiscalIssuanceReferenceId: FiscalReferenceId,
            PaymentConfirmationId: ConfirmationId,
            PaymentAttemptId: AttemptId,
            ParkingSessionId: SessionId,
            TariffSnapshotId: TariffId,
            SiteId: SiteId,
            SitePosServerId: PosId,
            SitePosServerRef: "IST-POS-A",
            PayableBasisRef: TariffId.ToString("D"),
            UpstreamFinalityReference: $"PAYMENT_CONFIRMATION:{ConfirmationId:D}",
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
            LatestExceptionReason: FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: "pos_temporarily_unavailable",
            LatestErrorPosture: posture,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-08-24T01:00:01Z"),
            RecordedByServiceIdentityId: null,
            FiscalDocumentTypeCodeId: Guid.Parse("75000000-0000-4000-8000-000000000008"),
            FiscalDocumentTypeCodeKey: "sales_invoice");
}
