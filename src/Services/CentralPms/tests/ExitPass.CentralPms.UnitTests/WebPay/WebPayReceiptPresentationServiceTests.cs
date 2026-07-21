using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.WebPay;

public sealed class WebPayReceiptPresentationServiceTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CorrelationId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid PosFiscalDocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task GetByPaymentAttemptIdAsync_WhenRecorded_ReturnsAuthoritativePosPresentation()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(reference);
        var posClient = Substitute.For<IPosServerFiscalDocumentClient>();
        posClient
            .GetFiscalDocumentPresentationAsync(PosFiscalDocumentId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(new PosServerFiscalDocumentPresentationReadResult(
                PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: 200,
                Code: "OK",
                Message: "Presentation returned.",
                FiscalDocumentId: PosFiscalDocumentId,
                FiscalDocumentNumber: "SI-20260523-000001",
                FiscalDocumentStatus: "RECORDED",
                FiscalNumberAssignmentState: "ASSIGNED",
                FiscalDocumentStatusCodeId: Guid.NewGuid(),
                FiscalDocumentType: "SALES_INVOICE",
                FiscalDocumentTypeCodeId: Guid.NewGuid(),
                FiscalSeries: null,
                FiscalNumberPrefixText: null,
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-05-23T13:00:00+08:00"),
                RecordedAt: DateTimeOffset.Parse("2026-05-23T13:01:00+08:00"),
                VoidStatus: null,
                VoidReasonCode: null,
                VoidedAt: null,
                PresentationVersion: "digital-sales-invoice-presentation-json-v1",
                TemplateVersion: "digital-sales-invoice-json-v1",
                ContentType: "application/json",
                AuthoritativeResponse: JsonSerializer.SerializeToElement(new
                {
                    presentation = new
                    {
                        documentTitle = "Sales Invoice"
                    }
                })));
        var sut = new WebPayReceiptPresentationService(repository, posClient);

        var result = await sut.GetByPaymentAttemptIdAsync(PaymentAttemptId, CorrelationId, CancellationToken.None);

        result.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.PosFiscalDocumentId.Should().Be(PosFiscalDocumentId);
        result.FiscalDocumentNumber.Should().Be("SI-20260523-000001");
        result.AuthoritativePresentation
            .GetProperty("presentation")
            .GetProperty("documentTitle")
            .GetString()
            .Should()
            .Be("Sales Invoice");
    }

    [Fact]
    public async Task GetByPaymentAttemptIdAsync_WhenFiscalIssuancePending_DoesNotCallPosServer()
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance) with
            {
                PosServerFiscalDocumentId = null,
                FiscalDocumentNumber = null
            });
        var posClient = Substitute.For<IPosServerFiscalDocumentClient>();
        var sut = new WebPayReceiptPresentationService(repository, posClient);

        var act = () => sut.GetByPaymentAttemptIdAsync(PaymentAttemptId, CorrelationId, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WebPayReceiptPresentationRejectedException>();
        exception.Which.ErrorCode.Should().Be("WEBPAY_RECEIPT_PRESENTATION_NOT_READY");
        exception.Which.Retryable.Should().BeTrue();
        await posClient
            .DidNotReceiveWithAnyArgs()
            .GetFiscalDocumentPresentationAsync(default, default, default);
    }

    [Fact]
    public async Task GetByPaymentAttemptIdAsync_WhenPosServerUnavailable_ReturnsSafeRetryableRejection()
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded));
        var posClient = Substitute.For<IPosServerFiscalDocumentClient>();
        posClient
            .GetFiscalDocumentPresentationAsync(PosFiscalDocumentId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(new PosServerFiscalDocumentPresentationReadResult(
                PosServerFiscalDocumentOutcome.FailedService,
                Succeeded: false,
                HttpStatusCode: 503,
                Code: "UNAVAILABLE",
                Message: "Unavailable.",
                FiscalDocumentId: PosFiscalDocumentId,
                FiscalDocumentNumber: null,
                FiscalDocumentStatus: null,
                FiscalNumberAssignmentState: null,
                FiscalDocumentStatusCodeId: null,
                FiscalDocumentType: null,
                FiscalDocumentTypeCodeId: null,
                FiscalSeries: null,
                FiscalNumberPrefixText: null,
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: null,
                RecordedAt: null,
                VoidStatus: null,
                VoidReasonCode: null,
                VoidedAt: null,
                PresentationVersion: null,
                TemplateVersion: null,
                ContentType: null,
                AuthoritativeResponse: null));
        var sut = new WebPayReceiptPresentationService(repository, posClient);

        var act = () => sut.GetByPaymentAttemptIdAsync(PaymentAttemptId, CorrelationId, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WebPayReceiptPresentationRejectedException>();
        exception.Which.ErrorCode.Should().Be("POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE");
        exception.Which.Retryable.Should().BeTrue();
        exception.Which.Message.Should().NotContain("http");
    }

    private static FiscalIssuanceReferenceRecord Reference(FiscalIssuanceIntegrationState state) =>
        new(
            FiscalIssuanceReferenceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PaymentConfirmationId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PaymentAttemptId: PaymentAttemptId,
            ParkingSessionId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TariffSnapshotId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            SiteId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            SitePosServerId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            SitePosServerRef: "pos-server-test",
            PayableBasisRef: "webpay-test-payable-basis",
            UpstreamFinalityReference: "webpay-test-finality",
            PosServerFiscalDocumentId: PosFiscalDocumentId,
            FiscalIdentityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            FiscalSequencePolicyId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            FiscalSequenceValue: 1,
            FiscalDocumentNumber: "SI-20260523-000001",
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-05-23T13:00:00+08:00"),
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: state == FiscalIssuanceIntegrationState.FiscalIssuanceRecorded ? null : FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.Parse("2026-05-23T13:01:00+08:00"),
            FirstRecordedAt: DateTimeOffset.Parse("2026-05-23T13:00:00+08:00"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-05-23T13:01:00+08:00"),
            RecordedByServiceIdentityId: null);
}
