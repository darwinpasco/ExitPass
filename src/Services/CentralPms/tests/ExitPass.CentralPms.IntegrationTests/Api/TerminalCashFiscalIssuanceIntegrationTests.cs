using System.Net;
using System.Net.Http.Json;
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
/// Focused coverage for terminal cash-payment fiscal issuance trigger and readback.
/// </summary>
public sealed class TerminalCashFiscalIssuanceIntegrationTests
{
    private static readonly Guid TerminalCashTenderId = Guid.Parse("21000000-0000-4000-8000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("21000000-0000-4000-8000-000000000002");
    private static readonly Guid PaymentConfirmationId = Guid.Parse("21000000-0000-4000-8000-000000000003");
    private static readonly Guid ParkingSessionId = Guid.Parse("21000000-0000-4000-8000-000000000004");
    private static readonly Guid TariffSnapshotId = Guid.Parse("21000000-0000-4000-8000-000000000005");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("21000000-0000-4000-8000-000000000006");
    private static readonly Guid PosFiscalDocumentId = Guid.Parse("21000000-0000-4000-8000-000000000007");
    private static readonly Guid StatutoryDecisionCommandId = Guid.Parse("21000000-0000-4000-8000-000000000030");
    private static readonly Guid StatutoryApplicationCommandId = Guid.Parse("21000000-0000-4000-8000-000000000031");
    private static readonly Guid StatutoryValidationId = Guid.Parse("21000000-0000-4000-8000-000000000032");
    private static readonly Guid StatutoryApplicationId = Guid.Parse("21000000-0000-4000-8000-000000000033");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("21000000-0000-4000-8000-000000000034");
    private static readonly Guid AppliedPolicyReferenceId = Guid.Parse("21000000-0000-4000-8000-000000000035");
    private static readonly Guid PosServerFiscalDiscountPrivilegeTypeCodeId =
        Guid.Parse("10000000-0000-0000-0000-000000000501");

    [Fact]
    public async Task TerminalCashFiscalIssuance_ConfirmedCashPayment_TriggersExistingFiscalIssuancePath()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var posIntegration = new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        var service = CreateService(CashPayment(), repo, posIntegration);

        var result = await service.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.Equal(TerminalCashTenderId, result.TerminalCashTenderId);
        Assert.Equal(PaymentAttemptId, result.PaymentAttemptId);
        Assert.Equal(PaymentConfirmationId, result.PaymentConfirmationId);
        Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, result.FiscalIssuanceState);
        Assert.Equal(FiscalIssuanceResultClassification.NewlyCreated, result.ResultClassification);
        Assert.Equal(PosFiscalDocumentId, result.PosFiscalDocumentId);
        Assert.True(result.PosServerCallAttempted);
        Assert.False(result.ExitAuthorizationIssued);
        Assert.False(result.GateBehaviorTriggered);
        Assert.Equal(1, repo.CreateCount);
        Assert.Equal(1, posIntegration.IssueCallCount);
        Assert.NotNull(posIntegration.LastFiscalContext);
        Assert.Equal("CASH", posIntegration.LastFiscalContext!.Tenders.Single().ProviderRef);
        Assert.Empty(posIntegration.LastFiscalContext.PayableBasis.DiscountReferences);
        Assert.Empty(posIntegration.LastFiscalContext.DiscountPrivilegeDetails);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_AppliedStatutoryPayment_PopulatesPosServerDiscountReferencesAndPrivilegeDetails()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var posIntegration = new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        var service = CreateService(
            CashPayment(amountDueMinorUnits: 8_929),
            repo,
            posIntegration,
            new FakeStatutoryFiscalLinkageReader(TerminalCashStatutoryFiscalLinkageResult.Complete(StatutoryContext())));

        await service.IssueOrReadAsync(Command(), CancellationToken.None);

        var fiscalContext = posIntegration.LastFiscalContext!;
        Assert.Equal(8_929, fiscalContext.PayableBasis.PayableAmountMinorUnits);
        Assert.Equal(8_929, fiscalContext.Tenders.Single().AmountMinorUnits);
        Assert.Equal(8_929, fiscalContext.Totals.Single().AmountMinorUnits);
        var discountReference = Assert.Single(fiscalContext.PayableBasis.DiscountReferences);
        Assert.Equal(StatutoryValidationId.ToString("D"), discountReference.DiscountValidationRef);
        Assert.Equal("approved", discountReference.Status);
        Assert.True(discountReference.AppliesStatutoryDiscountTreatment);
        Assert.Equal(StatutoryDecisionCommandId.ToString("D"), discountReference.StatutoryDiscountDecisionCommandRef);
        Assert.Equal("SENIOR_CITIZEN", discountReference.EntitlementType);
        Assert.Equal(OriginalTariffSnapshotId.ToString("D"), discountReference.OriginalTariffSnapshotRef);
        Assert.Equal(TariffSnapshotId.ToString("D"), discountReference.AppliedTariffSnapshotRef);
        Assert.Equal(12_500, discountReference.OriginalAmountMinorUnits);
        Assert.Equal(11_161, discountReference.VatExclusiveBasisAmountMinorUnits);
        Assert.Equal(2_232, discountReference.DiscountAmountMinorUnits);
        Assert.Equal(8_929, discountReference.FinalPayableAmountMinorUnits);

        var privilege = Assert.Single(fiscalContext.DiscountPrivilegeDetails);
        Assert.Equal(PosServerFiscalDiscountPrivilegeTypeCodeId, privilege.DiscountPrivilegeTypeCodeId);
        Assert.Equal(11_161, privilege.BasisAmountMinorUnits);
        Assert.Equal(2_232, privilege.DiscountAmountMinorUnits);
        Assert.Equal(1_339, privilege.VatPrivilegeAmountMinorUnits);
        Assert.Equal("PHP", privilege.CurrencyCode);
        Assert.Equal("SC-***-1234", privilege.BeneficiaryRef);
        Assert.Equal($"statutory-discount-validation:{StatutoryValidationId:D}", privilege.EvidenceRef);
        Assert.Equal(StatutoryValidationId.ToString("D"), privilege.ApprovalRef);
        Assert.Contains("statutoryDiscountDecisionCommandId", fiscalContext.ReferenceContext.Keys);
        Assert.DoesNotContain("reviewer", Serialize(fiscalContext), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_id", Serialize(fiscalContext), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_InvalidStatutoryLinkage_BlocksBeforeFiscalReferenceAndPosServerCall()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var posIntegration = new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        var service = CreateService(
            CashPayment(amountDueMinorUnits: 8_929),
            repo,
            posIntegration,
            new FakeStatutoryFiscalLinkageReader(
                TerminalCashStatutoryFiscalLinkageResult.TerminallyInconsistent("STATUTORY_FISCAL_LINKAGE_MISSING")));

        var ex = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(
            () => service.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_LINKAGE_MISSING", ex.ErrorCode);
        Assert.Equal(0, repo.CreateCount);
        Assert.Equal(0, posIntegration.IssueCallCount);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_UnconfirmedCashPayment_IsRejectedWithoutPaymentOrchestratorEvidence()
    {
        var service = CreateService(CashPayment(canonicalPaymentStatus: "PENDING"));

        var ex = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(
            () => service.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("TERMINAL_CASH_PAYMENT_NOT_CONFIRMED", ex.ErrorCode);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_MissingTerminalCashPayment_ReturnsNotFoundRejection()
    {
        var service = CreateService(cashPayment: null);

        var ex = await Assert.ThrowsAsync<TerminalCashFiscalIssuanceRejectedException>(
            () => service.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.True(ex.IsNotFound);
        Assert.Equal("TERMINAL_CASH_PAYMENT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_SameRequest_ReturnsExistingResultAndDoesNotCreateSecondPosDocument()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var posIntegration = new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        var service = CreateService(CashPayment(), repo, posIntegration);

        var first = await service.IssueOrReadAsync(Command(), CancellationToken.None);
        var second = await service.IssueOrReadAsync(Command(idempotencyKey: "terminal-cash-fiscal-second"), CancellationToken.None);

        Assert.Equal(first.FiscalIssuanceReferenceId, second.FiscalIssuanceReferenceId);
        Assert.Equal(first.PosFiscalDocumentId, second.PosFiscalDocumentId);
        Assert.Equal(1, repo.CreateCount);
        Assert.Equal(1, posIntegration.IssueCallCount);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_PosServerUnavailable_PreservesDurablePendingReference()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var posIntegration = new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Disabled);
        var service = CreateService(CashPayment(), repo, posIntegration);

        var result = await service.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.Equal(FiscalIssuanceIntegrationState.PendingFiscalIssuance, result.FiscalIssuanceState);
        Assert.Equal("pos_server_fiscal_issuance_live_call_disabled", result.SafeErrorCode);
        Assert.False(result.PosServerCallAttempted);
        Assert.Equal(1, repo.CreateCount);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_Readback_ReturnsDurableReferenceAfterNewServiceScope()
    {
        var repo = new InMemoryFiscalReferenceRepository();
        var firstService = CreateService(
            CashPayment(),
            repo,
            new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied));
        var issued = await firstService.IssueOrReadAsync(Command(), CancellationToken.None);

        var secondService = CreateService(
            CashPayment(),
            repo,
            new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied));
        var readback = await secondService.GetByTerminalCashTenderIdAsync(
            TerminalCashTenderId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(readback);
        Assert.Equal(issued.FiscalIssuanceReferenceId, readback!.FiscalIssuanceReferenceId);
        Assert.Equal(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, readback.FiscalIssuanceState);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_ReadbackBeforeIssuance_ReturnsNull()
    {
        var service = CreateService(CashPayment());

        var readback = await service.GetByTerminalCashTenderIdAsync(
            TerminalCashTenderId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(readback);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_EndpointReadbackBeforeIssuance_ReturnsNotFound()
    {
        var fake = new FakeTerminalCashFiscalIssuanceService(readback: null);
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/terminal-cash-payments/references/{TerminalCashTenderId}/fiscal-issuance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("TERMINAL_CASH_FISCAL_ISSUANCE_NOT_FOUND", error!.ErrorCode);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_EndpointIssue_ReturnsFiscalReferenceAndDoesNotIssueExitOrGate()
    {
        var fake = new FakeTerminalCashFiscalIssuanceService(Result());
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/terminal-cash-payments/references/{TerminalCashTenderId}/fiscal-issuance")
        {
            Content = JsonContent.Create(new TerminalCashFiscalIssuanceRequest())
        };
        request.Headers.Add("Idempotency-Key", "terminal-cash-fiscal-key");
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TerminalCashFiscalIssuanceResponse>();
        Assert.Equal(FiscalIssuanceReferenceId, body!.FiscalIssuanceReferenceId);
        Assert.Equal("FISCAL_ISSUANCE_RECORDED", body.FiscalIssuanceState);
        Assert.False(body.ExitAuthorizationIssued);
        Assert.False(body.GateBehaviorTriggered);
        Assert.Equal(1, fake.IssueCallCount);
    }

    [Fact]
    public async Task TerminalCashFiscalIssuance_EndpointConflict_MapsSafeConflict()
    {
        var fake = new FakeTerminalCashFiscalIssuanceService(
            new TerminalCashFiscalIssuanceRejectedException(
                "TERMINAL_CASH_FISCAL_SEMANTIC_CONFLICT",
                "Terminal cash payment is already linked to a conflicting fiscal issuance request."));
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/terminal-cash-payments/references/{TerminalCashTenderId}/fiscal-issuance")
        {
            Content = JsonContent.Create(new TerminalCashFiscalIssuanceRequest())
        };
        request.Headers.Add("Idempotency-Key", "terminal-cash-fiscal-key");
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("TERMINAL_CASH_FISCAL_SEMANTIC_CONFLICT", error!.ErrorCode);
    }

    private static ITerminalCashFiscalIssuanceService CreateService(
        TerminalCashPaymentReadback? cashPayment,
        InMemoryFiscalReferenceRepository? repo = null,
        FakePosServerIntegration? posIntegration = null,
        ITerminalCashStatutoryFiscalLinkageReader? statutoryFiscalLinkageReader = null)
    {
        repo ??= new InMemoryFiscalReferenceRepository();
        return new TerminalCashFiscalIssuanceService(
            new FakeTerminalCashPaymentService(cashPayment),
            repo,
            new FiscalIssuanceOrchestrationService(repo),
            posIntegration ?? new FakePosServerIntegration(repo, FiscalIssuancePosServerLiveIntegrationStatus.Applied),
            statutoryFiscalLinkageReader ?? new FakeStatutoryFiscalLinkageReader(
                TerminalCashStatutoryFiscalLinkageResult.NotApplicable()));
    }

    private static CustomWebApplicationFactory CreateFactory(ITerminalCashFiscalIssuanceService service) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ITerminalCashFiscalIssuanceService>();
                services.AddSingleton(service);
            });

    private static TerminalCashFiscalIssuanceCommand Command(string idempotencyKey = "terminal-cash-fiscal-key") =>
        new(TerminalCashTenderId, idempotencyKey, Guid.Parse("21000000-0000-4000-8000-000000000099"));

    private static TerminalCashPaymentReadback CashPayment(
        string canonicalPaymentStatus = "CONFIRMED",
        long amountDueMinorUnits = 10_000) =>
        new(
            TerminalCashPaymentCommandId: Guid.Parse("21000000-0000-4000-8000-000000000010"),
            TerminalCashTenderId: TerminalCashTenderId,
            PaymentAttemptId: PaymentAttemptId,
            CashCustodySessionId: Guid.Parse("21000000-0000-4000-8000-000000000011"),
            ParkingSessionId: ParkingSessionId,
            TariffSnapshotId: TariffSnapshotId,
            TerminalId: "terminal-001",
            SiteId: Guid.Parse("21000000-0000-4000-8000-000000000012"),
            SiteGroupId: Guid.Parse("21000000-0000-4000-8000-000000000013"),
            PosServerId: "DEV-POS-SERVER-ATC-001",
            CashierId: "cashier-001",
            CashierShiftId: "shift-001",
            Currency: "PHP",
            AmountDueMinorUnits: amountDueMinorUnits,
            AmountTenderedMinorUnits: amountDueMinorUnits,
            ChangeDueMinorUnits: 0,
            CanonicalPaymentStatus: canonicalPaymentStatus,
            PaymentConfirmationId: PaymentConfirmationId,
            ResultClassification: "CREATED",
            IdempotencyScope: "terminal-cash-payment:test",
            SemanticHashSourceVersion: "terminal-cash-payment:sha256:v1",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            ConfirmedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastUpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: Guid.Parse("21000000-0000-4000-8000-000000000014"),
            FiscalStatus: "NOT_STARTED_IN_THIS_SLICE");

    private static TerminalCashStatutoryFiscalLinkageContext StatutoryContext() =>
        new(
            StatutoryDecisionCommandId,
            StatutoryApplicationCommandId,
            StatutoryValidationId,
            StatutoryApplicationId,
            ParkingSessionId,
            Guid.Parse("21000000-0000-4000-8000-000000000012"),
            Guid.Parse("21000000-0000-4000-8000-000000000013"),
            OriginalTariffSnapshotId,
            TariffSnapshotId,
            AppliedPolicyReferenceId,
            "NATIONAL_LAW_FALLBACK",
            "SENIOR_CITIZEN",
            "ASSISTED_PAYMENT_TERMINAL",
            OriginalAmountMinorUnits: 12_500,
            VatExclusiveBasisAmountMinorUnits: 11_161,
            VatAmountMinorUnits: 1_339,
            VatTreatment: "VAT_EXCLUSIVE",
            StatutoryDiscountAmountMinorUnits: 2_232,
            FinalPayableAmountMinorUnits: 8_929,
            Currency: "PHP",
            DecisionTimestamp: DateTimeOffset.Parse("2026-07-28T01:20:00Z"),
            AppliedAt: DateTimeOffset.Parse("2026-07-28T01:21:00Z"),
            MaskedIdReference: "SC-***-1234");

    private static string Serialize(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private static TerminalCashFiscalIssuanceResult Result() =>
        new(
            TerminalCashTenderId,
            PaymentAttemptId,
            PaymentConfirmationId,
            FiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            FiscalIssuanceResultClassification.NewlyCreated,
            PosFiscalDocumentId: PosFiscalDocumentId,
            FiscalDocumentNumber: "SI-000001",
            FiscalNumberAssignedAt: DateTimeOffset.UtcNow,
            SemanticHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid(),
            SafeErrorCode: null,
            SafeErrorPosture: null,
            PosServerCallAttempted: true,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false);

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
            throw new NotSupportedException("Terminal cash payment creation is not used by fiscal issuance tests.");

        public Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
            Guid terminalCashTenderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_cashPayment?.TerminalCashTenderId == terminalCashTenderId ? _cashPayment : null);
    }

    private sealed class FakeStatutoryFiscalLinkageReader : ITerminalCashStatutoryFiscalLinkageReader
    {
        private readonly TerminalCashStatutoryFiscalLinkageResult _result;

        public FakeStatutoryFiscalLinkageReader(TerminalCashStatutoryFiscalLinkageResult result)
        {
            _result = result;
        }

        public Task<TerminalCashStatutoryFiscalLinkageResult> ReadByAppliedTariffSnapshotAsync(
            TerminalCashPaymentReadback cashPayment,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class FakePosServerIntegration : IFiscalIssuancePosServerLiveIntegrationService
    {
        private readonly InMemoryFiscalReferenceRepository _repo;
        private readonly FiscalIssuancePosServerLiveIntegrationStatus _status;

        public FakePosServerIntegration(
            InMemoryFiscalReferenceRepository repo,
            FiscalIssuancePosServerLiveIntegrationStatus status)
        {
            _repo = repo;
            _status = status;
        }

        public int IssueCallCount { get; private set; }

        public CentralPmsFiscalDocumentMappingContext? LastFiscalContext { get; private set; }

        public Task<FiscalIssuancePosServerLiveIntegrationResult> TryIssueFiscalDocumentViaPosServerAsync(
            Guid fiscalIssuanceReferenceId,
            CentralPmsFiscalDocumentMappingContext fiscalContext,
            PosServerCreateResultRecordingContext recordingContext,
            CancellationToken cancellationToken)
        {
            IssueCallCount++;
            LastFiscalContext = fiscalContext;

            if (_status == FiscalIssuancePosServerLiveIntegrationStatus.Disabled)
            {
                return Task.FromResult(FiscalIssuancePosServerLiveIntegrationResult.Disabled());
            }

            var result = new PosServerFiscalDocumentCreateResult(
                PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: 201,
                Code: "fiscal_document_created",
                Message: "Fiscal document created.",
                FiscalDocumentId: PosFiscalDocumentId,
                ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalIdentityId: Guid.Parse("21000000-0000-4000-8000-000000000020"),
                FiscalDocumentStatusCodeId: Guid.Parse("21000000-0000-4000-8000-000000000021"),
                FiscalSequencePolicyId: Guid.Parse("21000000-0000-4000-8000-000000000022"),
                FiscalSequenceValue: 1,
                FiscalDocumentNumber: "SI-000001",
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-",
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: DateTimeOffset.UtcNow,
                FiscalNumberAssignedByRef: "pos-server",
                ErrorPosture: null);
            var reference = _repo.MarkRecorded(fiscalIssuanceReferenceId, recordingContext, result);
            return Task.FromResult(FiscalIssuancePosServerLiveIntegrationResult.Applied(
                new PosServerFiscalDocumentRequestMapper().Map(fiscalContext),
                result,
                reference));
        }

        public Task<FiscalIssuancePosServerDiagnosticResult> RunPosServerFiscalIssuanceDiagnosticAsync(
            Guid fiscalIssuanceReferenceId,
            CentralPmsFiscalDocumentMappingContext fiscalContext,
            PosServerCreateResultRecordingContext recordingContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagnostics are not used by terminal cash fiscal issuance tests.");
    }

    private sealed class FakeTerminalCashFiscalIssuanceService : ITerminalCashFiscalIssuanceService
    {
        private readonly TerminalCashFiscalIssuanceResult? _result;
        private readonly TerminalCashFiscalIssuanceRejectedException? _rejection;
        private readonly TerminalCashFiscalIssuanceResult? _readback;

        public FakeTerminalCashFiscalIssuanceService(TerminalCashFiscalIssuanceResult? result = null, TerminalCashFiscalIssuanceResult? readback = null)
        {
            _result = result;
            _readback = readback ?? result;
        }

        public FakeTerminalCashFiscalIssuanceService(TerminalCashFiscalIssuanceRejectedException rejection)
        {
            _rejection = rejection;
        }

        public int IssueCallCount { get; private set; }

        public Task<TerminalCashFiscalIssuanceResult> IssueOrReadAsync(
            TerminalCashFiscalIssuanceCommand command,
            CancellationToken cancellationToken)
        {
            IssueCallCount++;
            if (_rejection is not null)
            {
                throw _rejection;
            }

            return Task.FromResult(_result!);
        }

        public Task<TerminalCashFiscalIssuanceResult?> GetByTerminalCashTenderIdAsync(
            Guid terminalCashTenderId,
            Guid? correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_readback);
    }

    private sealed class InMemoryFiscalReferenceRepository : IFiscalIssuanceReferenceRepository
    {
        private readonly List<FiscalIssuanceReferenceRecord> _records = [];

        public int CreateCount { get; private set; }

        public Task<FiscalIssuanceReferenceRecord> CreateAsync(
            CreateFiscalIssuanceReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var now = DateTimeOffset.UtcNow;
            var record = new FiscalIssuanceReferenceRecord(
                FiscalIssuanceReferenceId,
                request.PaymentConfirmationId,
                request.PaymentAttemptId,
                request.ParkingSessionId,
                request.TariffSnapshotId,
                request.SiteId,
                request.SitePosServerId,
                request.SitePosServerRef,
                request.PayableBasisRef,
                request.UpstreamFinalityReference,
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
                FiscalNumberAssignmentState.NotAssigned,
                FiscalIssuanceIntegrationState.PendingFiscalIssuance,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                request.CorrelationId,
                PosServerResponseTimestamp: null,
                FirstRecordedAt: now,
                LastUpdatedAt: now,
                RecordedByServiceIdentityId: null,
                request.FiscalDocumentTypeCodeId,
                request.FiscalDocumentTypeCodeKey);
            _records.Add(record);
            return Task.FromResult(record);
        }

        public FiscalIssuanceReferenceRecord MarkRecorded(
            Guid fiscalIssuanceReferenceId,
            PosServerCreateResultRecordingContext context,
            PosServerFiscalDocumentCreateResult result)
        {
            var index = _records.FindIndex(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId);
            var current = _records[index];
            var updated = current with
            {
                PosServerFiscalDocumentId = result.FiscalDocumentId,
                FiscalIdentityId = result.FiscalIdentityId,
                FiscalSequencePolicyId = result.FiscalSequencePolicyId,
                FiscalSequenceValue = result.FiscalSequenceValue,
                FiscalDocumentNumber = result.FiscalDocumentNumber,
                FiscalSeries = result.FiscalSeries,
                FiscalNumberPrefixText = result.FiscalNumberPrefixText,
                FiscalNumberSuffixText = result.FiscalNumberSuffixText,
                FiscalNumberAssignedAt = result.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef = result.FiscalNumberAssignedByRef,
                FiscalDocumentStatusCodeId = result.FiscalDocumentStatusCodeId,
                ResultClassification = result.ResultClassification,
                FiscalIssuanceEvidenceStatus = result.FiscalIssuanceEvidenceStatus,
                FiscalNumberAssignmentState = result.FiscalNumberAssignmentState ?? FiscalNumberAssignmentState.NotAssigned,
                FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                CorrelationId = context.CorrelationId,
                PosServerResponseTimestamp = context.PosServerResponseTimestamp,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
                SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion
            };
            _records[index] = updated;
            return updated;
        }

        public Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(
            Guid fiscalIssuanceReferenceId,
            FiscalIssuanceStateTransitionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FiscalIssuanceReferenceRecord> RecordSemanticRequestHashAsync(
            Guid fiscalIssuanceReferenceId,
            FiscalSemanticRequestHashResult semanticRequestHash,
            Guid? serviceIdentityId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FiscalIssuanceReferenceRecord?> FindByFiscalIssuanceReferenceIdAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId));

        public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(
            Guid paymentConfirmationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record => record.PaymentConfirmationId == paymentConfirmationId));

        public Task<FiscalIssuanceReferenceRecord?> FindLatestByPaymentAttemptIdAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.LastOrDefault(record => record.PaymentAttemptId == paymentAttemptId));

        public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(
            string upstreamFinalityReference,
            Guid? sitePosServerId,
            Guid? fiscalDocumentTypeCodeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record =>
                string.Equals(record.UpstreamFinalityReference, upstreamFinalityReference, StringComparison.Ordinal)));

        public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(
            Guid posServerFiscalDocumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record => record.PosServerFiscalDocumentId == posServerFiscalDocumentId));

        public Task<IReadOnlyList<FiscalIssuanceReferenceRecord>> FindByFiscalDocumentNumberAsync(
            string fiscalDocumentNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalIssuanceReferenceRecord>>(
                _records.Where(record => record.FiscalDocumentNumber == fiscalDocumentNumber).ToArray());
    }
}
