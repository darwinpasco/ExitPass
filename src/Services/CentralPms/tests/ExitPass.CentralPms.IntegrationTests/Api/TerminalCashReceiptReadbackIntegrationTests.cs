using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Focused coverage for terminal cash receipt-presentation readback.
/// </summary>
public sealed class TerminalCashReceiptReadbackIntegrationTests
{
    private static readonly Guid TerminalCashTenderId = Guid.Parse("31000000-0000-4000-8000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("31000000-0000-4000-8000-000000000002");
    private static readonly Guid PaymentConfirmationId = Guid.Parse("31000000-0000-4000-8000-000000000003");
    private static readonly Guid ParkingSessionId = Guid.Parse("31000000-0000-4000-8000-000000000004");
    private static readonly Guid TariffSnapshotId = Guid.Parse("31000000-0000-4000-8000-000000000005");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("31000000-0000-4000-8000-000000000006");
    private static readonly Guid PosFiscalDocumentId = Guid.Parse("31000000-0000-4000-8000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("31000000-0000-4000-8000-000000000008");

    [Fact]
    public async Task TerminalCashReceiptReadback_RecordedFiscalDocument_ReturnsAuthoritativePresentation()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("AVAILABLE", result.ReceiptAvailabilityState);
        Assert.Equal(PosFiscalDocumentId, result.PosFiscalDocumentId);
        Assert.Equal("presented", result.AuthoritativePresentation.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_ResponsePreservesTerminalCashPaymentFiscalAndPosReferences()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal(TerminalCashTenderId, result.TerminalCashTenderId);
        Assert.Equal(PaymentAttemptId, result.PaymentAttemptId);
        Assert.Equal(PaymentConfirmationId, result.PaymentConfirmationId);
        Assert.Equal("CONFIRMED", result.CanonicalPaymentStatus);
        Assert.Equal(FiscalIssuanceReferenceId, result.FiscalIssuanceReferenceId);
        Assert.Equal(PosFiscalDocumentId, result.PosFiscalDocumentId);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_PresentationPayloadComesFromPosServerClient()
    {
        var posClient = new FakePosServerPresentationClient();
        var result = await CreateService(posClient: posClient)
            .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal(1, posClient.PresentationReadCount);
        Assert.Equal("POS Server", result.AuthoritativePresentation.GetProperty("presentation").GetProperty("owner").GetString());
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_PresentationVersionIsPreserved()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("digital-sales-invoice-presentation-json-v1", result.PresentationVersion);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_TemplateVersionIsPreserved()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("digital-sales-invoice-json-v1", result.TemplateVersion);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_SemanticRequestHashIsPreservedFromDurableFiscalReference()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("sha256:test", result.SemanticRequestHash);
        Assert.Equal(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion, result.SemanticRequestHashVersion);
        Assert.Equal("AVAILABLE", result.SemanticRequestHashStatus);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_ContentTypeIsPreserved()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_FiscalNumberIsPreserved()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("SI-310001", result.FiscalDocumentNumber);
        Assert.Equal("SI-310001", result.AuthoritativePresentation.GetProperty("fiscalDocumentNumber").GetString());
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_CashTenderPresentationPassesThroughUnchanged()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);
        var tender = result.AuthoritativePresentation.GetProperty("presentation").GetProperty("tenders")[0];

        Assert.Equal("CASH", tender.GetProperty("tenderType").GetString());
        Assert.Equal(10_000, tender.GetProperty("amountMinorUnits").GetInt64());
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_TerminalCashPaymentNotFoundReturns404()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(cashPaymentMissing: true).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(404, ex.HttpStatusCode);
        Assert.Equal("TERMINAL_CASH_PAYMENT_NOT_FOUND", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_UnconfirmedCanonicalPaymentIsRejected()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(cashPayment: CashPayment("PENDING")).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(409, ex.HttpStatusCode);
        Assert.Equal("TERMINAL_CASH_PAYMENT_NOT_CONFIRMED", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_FiscalIssuanceNotCreatedReturnsNotFound()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(fiscalReferenceMissing: true).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(404, ex.HttpStatusCode);
        Assert.Equal("TERMINAL_CASH_FISCAL_ISSUANCE_NOT_FOUND", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_PendingFiscalIssuanceReturnsNotReadyWithoutPresentation()
    {
        var posClient = new FakePosServerPresentationClient();
        var reference = RecordedReference(FiscalIssuanceIntegrationState.PendingFiscalIssuance) with
        {
            PosServerFiscalDocumentId = null,
            FiscalDocumentNumber = null
        };

        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(reference: reference, posClient: posClient).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal("TERMINAL_CASH_RECEIPT_PRESENTATION_NOT_READY", ex.ErrorCode);
        Assert.True(ex.Retryable);
        Assert.Equal(0, posClient.PresentationReadCount);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_RecordedFiscalIssuanceWithoutPosFiscalDocumentIdFailsSafely()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(reference: RecordedReference() with { PosServerFiscalDocumentId = null })
                .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal("POS_FISCAL_DOCUMENT_ID_MISSING", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_PosServerUnavailableReturnsSafeUnavailablePosture()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(posClient: new FakePosServerPresentationClient(FakePosPresentationMode.Unavailable))
                .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(503, ex.HttpStatusCode);
        Assert.Equal("POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_PosFiscalDocumentNotFoundReturnsSafeInconsistency()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(posClient: new FakePosServerPresentationClient(FakePosPresentationMode.NotFound))
                .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(409, ex.HttpStatusCode);
        Assert.Equal("POS_FISCAL_DOCUMENT_PRESENTATION_INCONSISTENT", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_MalformedPosPresentationReturnsTerminalSafeContractError()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(posClient: new FakePosServerPresentationClient(FakePosPresentationMode.Malformed))
                .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(409, ex.HttpStatusCode);
        Assert.Equal("POS_SERVER_RECEIPT_PRESENTATION_MALFORMED", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_UnsupportedPosPresentationRemainsDistinguishable()
    {
        var ex = await Assert.ThrowsAsync<TerminalCashReceiptPresentationRejectedException>(
            () => CreateService(posClient: new FakePosServerPresentationClient(FakePosPresentationMode.Unsupported))
                .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None));

        Assert.Equal(409, ex.HttpStatusCode);
        Assert.Equal("POS_SERVER_RECEIPT_PRESENTATION_UNSUPPORTED", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_VoidedPresentationIsRepresentedSafely()
    {
        var result = await CreateService(posClient: new FakePosServerPresentationClient(FakePosPresentationMode.Voided))
            .GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("VOIDED_PRESENTATION_AVAILABLE", result.ReceiptAvailabilityState);
        Assert.Equal("voided", result.VoidStatus);
        Assert.Equal("operator_void", result.VoidReasonCode);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_RepeatedReadsAreSideEffectFree()
    {
        var repo = new InMemoryFiscalReferenceRepository(RecordedReference());
        var posClient = new FakePosServerPresentationClient();
        var service = CreateService(repo: repo, posClient: posClient);

        var first = await service.GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);
        var second = await service.GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal(first.FiscalIssuanceReferenceId, second.FiscalIssuanceReferenceId);
        Assert.Equal(0, repo.CreateCount);
        Assert.Equal(2, posClient.PresentationReadCount);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_ReadbackWorksAfterNewServiceScope()
    {
        var repo = new InMemoryFiscalReferenceRepository(RecordedReference());
        var first = await CreateService(repo: repo).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);
        var second = await CreateService(repo: repo).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal(first.PosFiscalDocumentId, second.PosFiscalDocumentId);
        Assert.Equal(first.FiscalDocumentNumber, second.FiscalDocumentNumber);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_CorrelationIdIsPreserved()
    {
        var posClient = new FakePosServerPresentationClient();
        var result = await CreateService(posClient: posClient).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal(CorrelationId, result.CorrelationId);
        Assert.Equal(CorrelationId, posClient.LastCorrelationId);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_DoesNotCreateReceiptRenderOrPrintState()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);
        var json = result.AuthoritativePresentation.GetRawText();

        Assert.DoesNotContain("printedAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("renderedByApt", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_DoesNotCreateNewFiscalDocumentOrNumber()
    {
        var repo = new InMemoryFiscalReferenceRepository(RecordedReference());
        var result = await CreateService(repo: repo).GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.Equal("SI-310001", result.FiscalDocumentNumber);
        Assert.Equal(0, repo.CreateCount);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_DoesNotExposeExitAuthorization()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.DoesNotContain("exitAuthorization", result.AuthoritativePresentation.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_DoesNotExposeGateBehavior()
    {
        var result = await CreateService().GetByTerminalCashTenderIdAsync(TerminalCashTenderId, CorrelationId, CancellationToken.None);

        Assert.DoesNotContain("gateCommand", result.AuthoritativePresentation.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalCashReceiptReadback_ServiceDoesNotDependOnPaymentOrchestrator()
    {
        var constructorParameterNames = typeof(TerminalCashReceiptPresentationService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name);

        Assert.DoesNotContain(constructorParameterNames, name => name.Contains("PaymentOrchestrator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_EndpointReturnsPresentationWithSafeWrapper()
    {
        var service = new FakeTerminalCashReceiptPresentationService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/terminal-cash-payments/references/{TerminalCashTenderId}/receipt-presentation");
        request.Headers.Add("X-Correlation-Id", CorrelationId.ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TerminalCashReceiptPresentationResponse>();
        Assert.Equal(TerminalCashTenderId, body!.TerminalCashTenderId);
        Assert.Equal("CONFIRMED", body.CanonicalPaymentStatus);
        Assert.Equal("FISCAL_ISSUANCE_RECORDED", body.FiscalIssuanceState);
        Assert.Equal("sha256:test", body.SemanticRequestHash);
        Assert.Equal(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion, body.SemanticRequestHashVersion);
        Assert.Equal("AVAILABLE", body.SemanticRequestHashStatus);
        Assert.Equal("presented", body.AuthoritativePresentation.GetProperty("code").GetString());
        Assert.Equal(1, service.ReadCount);
    }

    [Fact]
    public async Task TerminalCashReceiptReadback_EndpointReusesExistingErrorEnvelope()
    {
        using var factory = CreateFactory(new FakeTerminalCashReceiptPresentationService(
            new TerminalCashReceiptPresentationRejectedException(
                "TERMINAL_CASH_RECEIPT_PRESENTATION_NOT_READY",
                "Fiscal issuance is not recorded; receipt presentation is not available.",
                409,
                retryable: true)));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/terminal-cash-payments/references/{TerminalCashTenderId}/receipt-presentation");
        request.Headers.Add("X-Correlation-Id", CorrelationId.ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("TERMINAL_CASH_RECEIPT_PRESENTATION_NOT_READY", error!.ErrorCode);
        Assert.Equal(CorrelationId, error.CorrelationId);
        Assert.True(error.Retryable);
    }

    private static ITerminalCashReceiptPresentationService CreateService(
        TerminalCashPaymentReadback? cashPayment = null,
        FiscalIssuanceReferenceRecord? reference = null,
        InMemoryFiscalReferenceRepository? repo = null,
        FakePosServerPresentationClient? posClient = null,
        bool cashPaymentMissing = false,
        bool fiscalReferenceMissing = false)
    {
        repo ??= new InMemoryFiscalReferenceRepository(fiscalReferenceMissing ? null : reference ?? RecordedReference());
        return new TerminalCashReceiptPresentationService(
            new FakeTerminalCashPaymentService(cashPaymentMissing ? null : cashPayment ?? CashPayment()),
            repo,
            posClient ?? new FakePosServerPresentationClient());
    }

    private static CustomWebApplicationFactory CreateFactory(ITerminalCashReceiptPresentationService service) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ITerminalCashReceiptPresentationService>();
                services.AddSingleton(service);
            });

    private static TerminalCashPaymentReadback CashPayment(string canonicalPaymentStatus = "CONFIRMED") =>
        new(
            TerminalCashPaymentCommandId: Guid.Parse("31000000-0000-4000-8000-000000000010"),
            TerminalCashTenderId: TerminalCashTenderId,
            PaymentAttemptId: PaymentAttemptId,
            CashCustodySessionId: Guid.Parse("31000000-0000-4000-8000-000000000011"),
            ParkingSessionId: ParkingSessionId,
            TariffSnapshotId: TariffSnapshotId,
            TerminalId: "terminal-001",
            SiteId: Guid.Parse("31000000-0000-4000-8000-000000000012"),
            SiteGroupId: Guid.Parse("31000000-0000-4000-8000-000000000013"),
            PosServerId: "DEV-POS-SERVER-ATC-001",
            CashierId: "cashier-001",
            CashierShiftId: "shift-001",
            Currency: "PHP",
            AmountDueMinorUnits: 10_000,
            AmountTenderedMinorUnits: 10_000,
            ChangeDueMinorUnits: 0,
            CanonicalPaymentStatus: canonicalPaymentStatus,
            PaymentConfirmationId: PaymentConfirmationId,
            ResultClassification: "CREATED",
            IdempotencyScope: "terminal-cash-payment:test",
            SemanticHashSourceVersion: "terminal-cash-payment:sha256:v1",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            ConfirmedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastUpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: CorrelationId,
            FiscalStatus: "FISCAL_ISSUANCE_RECORDED");

    private static FiscalIssuanceReferenceRecord RecordedReference(
        FiscalIssuanceIntegrationState state = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) =>
        new(
            FiscalIssuanceReferenceId,
            PaymentConfirmationId,
            PaymentAttemptId,
            ParkingSessionId,
            TariffSnapshotId,
            Guid.Parse("31000000-0000-4000-8000-000000000012"),
            SitePosServerId: null,
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: TariffSnapshotId.ToString("D"),
            UpstreamFinalityReference: $"terminal-cash-payment-confirmation:{PaymentConfirmationId:D}:sales_invoice",
            PosServerFiscalDocumentId: PosFiscalDocumentId,
            FiscalIdentityId: Guid.Parse("31000000-0000-4000-8000-000000000020"),
            FiscalSequencePolicyId: Guid.Parse("31000000-0000-4000-8000-000000000021"),
            FiscalSequenceValue: 1,
            FiscalDocumentNumber: "SI-310001",
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            FiscalNumberAssignedByRef: "pos-server",
            FiscalDocumentStatusCodeId: Guid.Parse("31000000-0000-4000-8000-000000000022"),
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState.Assigned,
            state,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow.AddMinutes(-1),
            FirstRecordedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastUpdatedAt: DateTimeOffset.UtcNow,
            RecordedByServiceIdentityId: null,
            FiscalDocumentTypeCodeId: null,
            FiscalDocumentTypeCodeKey: "sales_invoice",
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: "sha256:test",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount: 1,
            SemanticRequestHashSafeSummary: "terminal cash fiscal issuance",
            SemanticRequestHashRecordedAt: DateTimeOffset.UtcNow);

    private sealed class FakeTerminalCashPaymentService : ITerminalCashPaymentService
    {
        private readonly TerminalCashPaymentReadback? _cashPayment;

        public FakeTerminalCashPaymentService(TerminalCashPaymentReadback? cashPayment)
        {
            _cashPayment = cashPayment;
        }

        public Task<TerminalCashPaymentResult> CreateOrReadAsync(
            TerminalCashPaymentCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback does not create terminal cash payments.");

        public Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
            Guid terminalCashTenderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_cashPayment?.TerminalCashTenderId == terminalCashTenderId ? _cashPayment : null);
    }

    private sealed class InMemoryFiscalReferenceRepository : IFiscalIssuanceReferenceRepository
    {
        private readonly FiscalIssuanceReferenceRecord? _record;

        public InMemoryFiscalReferenceRepository(FiscalIssuanceReferenceRecord? record)
        {
            _record = record;
        }

        public int CreateCount { get; private set; }

        public Task<FiscalIssuanceReferenceRecord> CreateAsync(
            CreateFiscalIssuanceReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            throw new NotSupportedException("Receipt readback must not create fiscal issuance references.");
        }

        public Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(
            Guid fiscalIssuanceReferenceId,
            FiscalIssuanceStateTransitionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback must not mutate fiscal state.");

        public Task<FiscalIssuanceReferenceRecord> RecordSemanticRequestHashAsync(
            Guid fiscalIssuanceReferenceId,
            FiscalSemanticRequestHashResult semanticRequestHash,
            Guid? serviceIdentityId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback must not mutate fiscal semantic hashes.");

        public Task<FiscalIssuanceReferenceRecord?> FindByFiscalIssuanceReferenceIdAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId ? _record : null);

        public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(
            Guid paymentConfirmationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.PaymentConfirmationId == paymentConfirmationId ? _record : null);

        public Task<FiscalIssuanceReferenceRecord?> FindLatestByPaymentAttemptIdAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.PaymentAttemptId == paymentAttemptId ? _record : null);

        public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(
            string upstreamFinalityReference,
            Guid? sitePosServerId,
            Guid? fiscalDocumentTypeCodeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.UpstreamFinalityReference == upstreamFinalityReference ? _record : null);

        public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(
            Guid posServerFiscalDocumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.PosServerFiscalDocumentId == posServerFiscalDocumentId ? _record : null);
    }

    private enum FakePosPresentationMode
    {
        Presented,
        Unavailable,
        NotFound,
        Voided,
        Malformed,
        Unsupported
    }

    private sealed class FakePosServerPresentationClient : IPosServerFiscalDocumentClient
    {
        private readonly FakePosPresentationMode _mode;

        public FakePosServerPresentationClient(FakePosPresentationMode mode = FakePosPresentationMode.Presented)
        {
            _mode = mode;
        }

        public int PresentationReadCount { get; private set; }

        public Guid? LastCorrelationId { get; private set; }

        public Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
            PosServerFiscalDocumentCreateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback must not create fiscal documents.");

        public Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
            Guid fiscalDocumentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback uses the presentation endpoint.");

        public Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
            Guid fiscalDocumentId,
            Guid? correlationId,
            CancellationToken cancellationToken)
        {
            PresentationReadCount++;
            LastCorrelationId = correlationId;

            return _mode switch
            {
                FakePosPresentationMode.Unavailable => Task.FromResult(PresentationFailure(503, "persistence_read_failed")),
                FakePosPresentationMode.NotFound => Task.FromResult(PresentationFailure(404, "fiscal_document_not_found")),
                FakePosPresentationMode.Voided => Task.FromResult(PresentationSuccess("voided")),
                FakePosPresentationMode.Malformed => Task.FromResult(PresentationFailure(200, "invalid_json_response", PosServerFiscalDocumentOutcome.InvalidResponse)),
                FakePosPresentationMode.Unsupported => Task.FromResult(PresentationFailure(400, "unsupported_presentation")),
                _ => Task.FromResult(PresentationSuccess())
            };
        }

        public Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
            Guid fiscalDocumentId,
            PosServerFiscalDocumentVoidRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Receipt readback must not void fiscal documents.");

        private static PosServerFiscalDocumentPresentationReadResult PresentationSuccess(string? voidStatus = null)
        {
            var authoritative = AuthoritativePresentation(voidStatus);
            return new PosServerFiscalDocumentPresentationReadResult(
                PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: 200,
                Code: "presented",
                Message: "Digital Sales Invoice presentation returned.",
                FiscalDocumentId: PosFiscalDocumentId,
                FiscalDocumentNumber: "SI-310001",
                FiscalDocumentStatus: "recorded",
                FiscalNumberAssignmentState: "assigned",
                FiscalDocumentStatusCodeId: Guid.Parse("31000000-0000-4000-8000-000000000022"),
                FiscalDocumentType: "sales_invoice",
                FiscalDocumentTypeCodeId: Guid.Parse("31000000-0000-4000-8000-000000000023"),
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-",
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                RecordedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                VoidStatus: voidStatus,
                VoidReasonCode: voidStatus is null ? null : "operator_void",
                VoidedAt: voidStatus is null ? null : DateTimeOffset.UtcNow,
                PresentationVersion: "digital-sales-invoice-presentation-json-v1",
                TemplateVersion: "digital-sales-invoice-json-v1",
                ContentType: "application/json",
                AuthoritativeResponse: authoritative);
        }

        private static PosServerFiscalDocumentPresentationReadResult PresentationFailure(
            int status,
            string code,
            PosServerFiscalDocumentOutcome outcome = PosServerFiscalDocumentOutcome.FailedService) =>
            new(
                outcome,
                Succeeded: false,
                HttpStatusCode: status,
                Code: code,
                Message: "POS Server presentation unavailable.",
                FiscalDocumentId: null,
                FiscalDocumentNumber: null,
                FiscalDocumentStatus: null,
                FiscalNumberAssignmentState: "not_assigned",
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
                AuthoritativeResponse: null);

        public static JsonElement AuthoritativePresentation(string? voidStatus = null)
        {
            var voidJson = voidStatus is null
                ? "\"voidStatus\":null,\"voidReasonCode\":null,\"voidedAt\":null"
                : "\"voidStatus\":\"voided\",\"voidReasonCode\":\"operator_void\",\"voidedAt\":\"2026-07-17T00:00:00Z\"";
            var json = $$"""
            {
              "succeeded": true,
              "code": "presented",
              "message": "Digital Sales Invoice presentation returned.",
              "templateContract": {
                "templateContractVersion": "digital-sales-invoice-json-v1",
                "renderFormat": "application/json"
              },
              "presentation": {
                "owner": "POS Server",
                "presentationVersion": "digital-sales-invoice-presentation-json-v1",
                "lines": [
                  { "description": "Parking fee - cash", "amountMinorUnits": 10000 }
                ],
                "taxes": [
                  { "taxType": "VAT", "amountMinorUnits": 0 }
                ],
                "totals": [
                  { "totalType": "grand_total", "amountMinorUnits": 10000 }
                ],
                "tenders": [
                  { "tenderType": "CASH", "amountMinorUnits": 10000 }
                ]
              },
              "fiscalNumberAssignmentState": "assigned",
              "fiscalDocumentStatus": "recorded",
              "fiscalDocumentId": "{{PosFiscalDocumentId:D}}",
              "fiscalDocumentNumber": "SI-310001",
              "presentationVersion": "digital-sales-invoice-presentation-json-v1",
              "templateVersion": "digital-sales-invoice-json-v1",
              "contentType": "application/json",
              {{voidJson}}
            }
            """;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private sealed class FakeTerminalCashReceiptPresentationService : ITerminalCashReceiptPresentationService
    {
        private readonly TerminalCashReceiptPresentationRejectedException? _rejection;

        public FakeTerminalCashReceiptPresentationService(TerminalCashReceiptPresentationRejectedException? rejection = null)
        {
            _rejection = rejection;
        }

        public int ReadCount { get; private set; }

        public Task<TerminalCashReceiptPresentationResult> GetByTerminalCashTenderIdAsync(
            Guid terminalCashTenderId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            if (_rejection is not null)
            {
                throw _rejection;
            }

            return Task.FromResult(new TerminalCashReceiptPresentationResult(
                terminalCashTenderId,
                PaymentAttemptId,
                PaymentConfirmationId,
                "CONFIRMED",
                FiscalIssuanceReferenceId,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                PosFiscalDocumentId,
                "SI-310001",
                "recorded",
                "AVAILABLE",
                "digital-sales-invoice-presentation-json-v1",
                "digital-sales-invoice-json-v1",
                "sha256:test",
                FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                "AVAILABLE",
                "application/json",
                FakePosServerPresentationClient.AuthoritativePresentation(),
                VoidStatus: null,
                VoidReasonCode: null,
                VoidedAt: null,
                CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                UpdatedAt: DateTimeOffset.UtcNow,
                correlationId));
        }
    }
}
