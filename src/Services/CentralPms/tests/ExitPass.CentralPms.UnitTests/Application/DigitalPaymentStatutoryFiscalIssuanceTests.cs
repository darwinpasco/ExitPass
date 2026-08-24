using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class DigitalPaymentStatutoryFiscalIssuanceTests
{
    private static readonly Guid ConfirmationId = Guid.Parse("76000000-0000-4000-8000-000000000001");
    private static readonly Guid AttemptId = Guid.Parse("76000000-0000-4000-8000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("76000000-0000-4000-8000-000000000003");
    private static readonly Guid OriginalTariffId = Guid.Parse("76000000-0000-4000-8000-000000000004");
    private static readonly Guid AppliedTariffId = Guid.Parse("76000000-0000-4000-8000-000000000005");
    private static readonly Guid SiteAId = Guid.Parse("76000000-0000-4000-8000-000000000006");
    private static readonly Guid SiteAGroupId = Guid.Parse("76000000-0000-4000-8000-000000000007");
    private static readonly Guid PosAId = Guid.Parse("76000000-0000-4000-8000-000000000008");
    private static readonly Guid DecisionId = Guid.Parse("76000000-0000-4000-8000-000000000009");
    private static readonly Guid ApplicationCommandId = Guid.Parse("76000000-0000-4000-8000-000000000010");
    private static readonly Guid ValidationId = Guid.Parse("76000000-0000-4000-8000-000000000011");
    private static readonly Guid PolicyId = Guid.Parse("76000000-0000-4000-8000-000000000012");
    private static readonly Guid FiscalReferenceId = Guid.Parse("76000000-0000-4000-8000-000000000013");

    [Fact]
    public async Task ApprovedPositiveAdjustment_MapsAuthoritativeWebPayFactsAndExactFiscalEconomics()
    {
        var fixture = CreateFixture(Context());

        await fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None);

        var request = new PosServerFiscalDocumentRequestMapper().Map(fixture.CapturedMapping!);
        Assert.Equal(8929, request.PayableBasis.PayableAmountMinorUnits);
        Assert.Equal(8929, Assert.Single(request.Tenders).AmountMinorUnits);
        Assert.Equal(8929, Assert.Single(request.Totals).AmountMinorUnits);

        var line = Assert.Single(request.DocumentLines);
        Assert.Equal(11161, line.GrossAmountMinorUnits);
        Assert.Equal(2232, line.DiscountAmountMinorUnits);
        Assert.Equal(8929, line.NetAmountMinorUnits);

        var tax = Assert.Single(request.TaxDetails);
        Assert.Equal(11161, tax.TaxableAmountMinorUnits);
        Assert.Equal(1339, tax.TaxAmountMinorUnits);

        var privilege = Assert.Single(request.DiscountPrivilegeDetails);
        Assert.Equal(2232, privilege.DiscountAmountMinorUnits);
        Assert.Null(privilege.BeneficiaryRef);
        Assert.Null(privilege.EvidenceRef);

        var facts = Assert.IsType<PosServerAppliedStatutoryFiscalFactsRequest>(request.AppliedStatutoryFiscalFacts);
        Assert.Equal(DecisionId, facts.StatutoryDiscountDecisionCommandId);
        Assert.Equal(ApplicationCommandId, facts.StatutoryPayableBasisApplicationCommandId);
        Assert.Equal(ValidationId, facts.StatutoryValidationId);
        Assert.Equal(SiteAId, facts.SiteId);
        Assert.Equal(SiteAGroupId, facts.SiteGroupId);
        Assert.Equal("SENIOR_CITIZEN", facts.EntitlementType);
        Assert.Equal("VAT_EXEMPTION_AND_STATUTORY_DISCOUNT", facts.BenefitClassification);
        Assert.Equal("LOCAL_ORDINANCE", facts.PolicyReference.ResolutionBasis);
        Assert.Equal(PolicyId, facts.PolicyReference.AppliedPolicyReferenceId);
        Assert.Equal("WEBPAY", facts.SourcePaymentChannel);
        Assert.Equal(8929, facts.FinalPayableAmountMinorUnits);
    }

    [Fact]
    public async Task ApprovedAdjustment_RequestContainsNoRawEvidenceOrBeneficiaryData()
    {
        var fixture = CreateFixture(Context());

        await fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None);

        var json = JsonSerializer.Serialize(new PosServerFiscalDocumentRequestMapper().Map(fixture.CapturedMapping!));
        Assert.DoesNotContain("image", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ocr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidenceUrl", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SiteSpecificRoute_IsTakenOnlyFromPersistedContext(bool siteA)
    {
        var siteId = siteA ? SiteAId : Guid.Parse("76000000-0000-4000-8000-000000000020");
        var siteGroupId = siteA ? SiteAGroupId : Guid.Parse("76000000-0000-4000-8000-000000000021");
        var posId = siteA ? PosAId : Guid.Parse("76000000-0000-4000-8000-000000000022");
        var posRef = siteA ? "IST-POS-A" : "IST-POS-B";
        var context = Context() with
        {
            SiteId = siteId,
            SiteGroupId = siteGroupId,
            SitePosServerId = posId,
            SitePosServerRef = posRef,
            AppliedStatutoryFiscalContext = Statutory() with { SiteId = siteId, SiteGroupId = siteGroupId }
        };
        var fixture = CreateFixture(context);

        await fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.Equal(posId, fixture.CapturedMapping!.SitePosServerId);
        Assert.Equal(posRef, fixture.CapturedMapping.SitePosServerRef);
    }

    [Fact]
    public async Task CrossSiteDecision_FailsBeforePosContact()
    {
        var context = Context() with
        {
            AppliedStatutoryFiscalContext = Statutory() with { SiteId = Guid.NewGuid() }
        };
        var fixture = CreateFixture(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_ROUTING_CONTEXT_MISMATCH", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task MismatchedConfirmedAmount_FailsBeforePosContact()
    {
        var fixture = CreateFixture(Context() with { AmountMinorUnits = 8930 });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_AMOUNT_OR_CHANNEL_MISMATCH", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task CrossSessionDecision_FailsBeforePosContact()
    {
        var fixture = CreateFixture(Context() with
        {
            AppliedStatutoryFiscalContext = Statutory() with { ParkingSessionId = Guid.NewGuid() }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_ROUTING_CONTEXT_MISMATCH", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task NonWebPayStatutorySource_FailsBeforePosContact()
    {
        var fixture = CreateFixture(Context() with
        {
            AppliedStatutoryFiscalContext = Statutory() with { SourceChannel = "OPERATOR_CONSOLE" }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_AMOUNT_OR_CHANNEL_MISMATCH", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task UnsupportedEntitlement_FailsBeforePosContact()
    {
        var fixture = CreateFixture(Context() with
        {
            AppliedStatutoryFiscalContext = Statutory() with { EntitlementType = "UNSUPPORTED" }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_ENTITLEMENT_UNSUPPORTED", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task MissingPolicyReference_FailsBeforePosContact()
    {
        var fixture = CreateFixture(Context() with
        {
            AppliedStatutoryFiscalContext = Statutory() with { AppliedPolicyReferenceId = null }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_REQUIRED_FACTS_UNAVAILABLE", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ZeroPayableStatutoryContext_IsNotTreatedAsDigitalPayment()
    {
        var zero = Statutory() with
        {
            VatExclusiveBasisAmountMinorUnits = 0,
            VatAmountMinorUnits = 12500,
            StatutoryDiscountAmountMinorUnits = 0,
            FinalPayableAmountMinorUnits = 0
        };
        var fixture = CreateFixture(Context() with { AmountMinorUnits = 0, AppliedStatutoryFiscalContext = zero });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("STATUTORY_FISCAL_ARITHMETIC_INVALID", error.Message);
        await fixture.PosServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task CompletedFiscalEvidence_ReturnsWithoutReapplyingAdjustmentOrCallingPos()
    {
        var contextReader = Substitute.For<IDigitalPaymentFiscalContextReader>();
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        references.FindByPaymentConfirmationIdAsync(ConfirmationId, Arg.Any<CancellationToken>())
            .Returns(Reference(Context()) with
            {
                PosServerFiscalDocumentId = Guid.NewGuid(),
                FiscalIdentityId = Guid.NewGuid(),
                FiscalSequencePolicyId = Guid.NewGuid(),
                FiscalSequenceValue = 7,
                FiscalDocumentNumber = "SI-A-000007",
                FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
                FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            });
        var sut = new DigitalPaymentFiscalIssuanceService(contextReader, references, orchestration, posServer);

        var result = await sut.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.True(result.ReadyForExitAuthorization);
        await contextReader.DidNotReceiveWithAnyArgs().ReadAsync(default, default, default, default);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    private static Fixture CreateFixture(DigitalPaymentFiscalContext context)
    {
        var contextReader = Substitute.For<IDigitalPaymentFiscalContextReader>();
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var reference = Reference(context);
        contextReader.ReadAsync(AttemptId, ConfirmationId, SessionId, Arg.Any<CancellationToken>()).Returns(context);
        references.FindByPaymentConfirmationIdAsync(ConfirmationId, Arg.Any<CancellationToken>()).Returns((FiscalIssuanceReferenceRecord?)null);
        orchestration.PreparePendingAsync(Arg.Any<PrepareFiscalIssuanceCommand>(), Arg.Any<CancellationToken>()).Returns(reference);
        CentralPmsFiscalDocumentMappingContext? captured = null;
        posServer.TryIssueFiscalDocumentViaPosServerAsync(
                FiscalReferenceId,
                Arg.Do<CentralPmsFiscalDocumentMappingContext>(value => captured = value),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.ConfigurationInvalid(["test_stop_after_mapping"]));
        var sut = new DigitalPaymentFiscalIssuanceService(contextReader, references, orchestration, posServer);
        return new Fixture(sut, posServer, () => captured);
    }

    private static DigitalPaymentFiscalIssuanceCommand Command() =>
        new(AttemptId, ConfirmationId, SessionId, "IST-PROVIDER-STATUTORY", Guid.NewGuid(), null);

    private static DigitalPaymentFiscalContext Context() =>
        new(
            SiteAId,
            SiteAGroupId,
            AppliedTariffId,
            8929,
            "PHP",
            DateTimeOffset.Parse("2026-08-24T02:00:00Z"),
            PosAId,
            "IST-POS-A",
            Statutory());

    private static TerminalCashStatutoryFiscalLinkageContext Statutory() =>
        new(
            DecisionId,
            ApplicationCommandId,
            ValidationId,
            Guid.Parse("76000000-0000-4000-8000-000000000014"),
            SessionId,
            SiteAId,
            SiteAGroupId,
            OriginalTariffId,
            AppliedTariffId,
            PolicyId,
            "LOCAL_ORDINANCE",
            "SENIOR_CITIZEN",
            "WEBPAY",
            12500,
            11161,
            1339,
            "VAT_EXCLUSIVE",
            2232,
            8929,
            "PHP",
            DateTimeOffset.Parse("2026-08-24T01:30:00Z"),
            DateTimeOffset.Parse("2026-08-24T01:45:00Z"),
            null);

    private static FiscalIssuanceReferenceRecord Reference(DigitalPaymentFiscalContext context) =>
        new(
            FiscalIssuanceReferenceId: FiscalReferenceId,
            PaymentConfirmationId: ConfirmationId,
            PaymentAttemptId: AttemptId,
            ParkingSessionId: SessionId,
            TariffSnapshotId: AppliedTariffId,
            SiteId: context.SiteId,
            SitePosServerId: context.SitePosServerId,
            SitePosServerRef: context.SitePosServerRef,
            PayableBasisRef: AppliedTariffId.ToString("D"),
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
            FiscalIssuanceState: FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.Parse("2026-08-24T02:00:00Z"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-08-24T02:00:00Z"),
            RecordedByServiceIdentityId: null,
            FiscalDocumentTypeCodeId: Guid.Parse("76000000-0000-4000-8000-000000000015"),
            FiscalDocumentTypeCodeKey: "sales_invoice");

    private sealed record Fixture(
        DigitalPaymentFiscalIssuanceService Sut,
        IFiscalIssuancePosServerLiveIntegrationService PosServer,
        Func<CentralPmsFiscalDocumentMappingContext?> Capture)
    {
        public CentralPmsFiscalDocumentMappingContext? CapturedMapping => Capture();
    }
}
