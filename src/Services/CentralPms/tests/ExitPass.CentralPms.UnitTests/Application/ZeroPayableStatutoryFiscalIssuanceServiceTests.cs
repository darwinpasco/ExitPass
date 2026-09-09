using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ZeroPayableStatutoryFiscalIssuanceServiceTests
{
    private static readonly Guid SessionId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid DecisionId = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid ApplicationId = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private static readonly Guid ValidationId = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    private static readonly Guid PolicyId = Guid.Parse("a1000000-0000-4000-8000-000000000005");
    private static readonly Guid OriginalTariffId = Guid.Parse("a1000000-0000-4000-8000-000000000006");
    private static readonly Guid AppliedTariffId = Guid.Parse("a1000000-0000-4000-8000-000000000007");
    private static readonly Guid SiteId = Guid.Parse("a1000000-0000-4000-8000-000000000008");
    private static readonly Guid SiteGroupId = Guid.Parse("a1000000-0000-4000-8000-000000000009");
    private static readonly Guid SitePosServerId = Guid.Parse("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid FiscalReferenceId = Guid.Parse("a1000000-0000-4000-8000-00000000000b");
    private static readonly DateTimeOffset AppliedAt = DateTimeOffset.Parse("2026-09-09T08:00:00Z");

    [Fact]
    public async Task IssueOrReadAsync_MapsCanonicalNonPaymentFiscalFacts()
    {
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        PrepareFiscalIssuanceCommand? prepared = null;
        CentralPmsFiscalDocumentMappingContext? mapped = null;
        references.FindByStatutoryApplicationCommandIdAsync(ApplicationId, Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceReferenceRecord?)null);
        orchestration.PreparePendingAsync(
                Arg.Do<PrepareFiscalIssuanceCommand>(value => prepared = value),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance));
        posServer.TryIssueFiscalDocumentViaPosServerAsync(
                FiscalReferenceId,
                Arg.Do<CentralPmsFiscalDocumentMappingContext>(value => mapped = value),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(FiscalIssuancePosServerLiveIntegrationResult.ConfigurationInvalid(["test_stop_after_mapping"]));
        var sut = CreateService(references, orchestration, posServer);

        var result = await sut.IssueOrReadAsync(Command(), CancellationToken.None);

        Assert.NotNull(prepared);
        Assert.Null(prepared.PaymentAttemptId);
        Assert.Null(prepared.PaymentConfirmationId);
        Assert.Equal(FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality, prepared.CompletionBasis);
        Assert.Equal(ApplicationId, prepared.CompletionAuthorityReferenceId);
        Assert.Equal(ApplicationId, prepared.StatutoryDiscountPayableBasisApplicationCommandId);
        Assert.NotNull(mapped);
        Assert.Equal(FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality, mapped.CompletionBasis);
        Assert.Equal(ApplicationId.ToString("D"), mapped.CompletionAuthorityRef);
        Assert.Null(mapped.CentralPmsPaymentAttemptRef);
        Assert.Null(mapped.CentralPmsPaymentConfirmationRef);
        Assert.Null(mapped.PaymentFinalityRef);
        Assert.Empty(mapped.Tenders);
        Assert.Equal(0, mapped.PayableBasis.PayableAmountMinorUnits);
        var line = Assert.Single(mapped.DocumentLines);
        Assert.Equal(2679, line.UnitAmountMinorUnits);
        Assert.Equal(2679, line.GrossAmountMinorUnits);
        Assert.Equal(2679, line.DiscountAmountMinorUnits);
        Assert.Equal(0, line.TaxAmountMinorUnits);
        Assert.Equal(0, line.NetAmountMinorUnits);
        var tax = Assert.Single(mapped.TaxDetails);
        Assert.Equal(2679, tax.TaxableAmountMinorUnits);
        Assert.Equal(321, tax.TaxAmountMinorUnits);
        Assert.Equal(0, Assert.Single(mapped.Totals).AmountMinorUnits);
        Assert.False(result.FiscalPrerequisiteSatisfied);
    }

    [Fact]
    public async Task ReadAsync_WhenFiscalDocumentAndEjAreRecorded_ReturnsSatisfiedWithoutNewIssuance()
    {
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        references.FindByStatutoryApplicationCommandIdAsync(ApplicationId, Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, completed: true));
        var sut = CreateService(references, orchestration, posServer);

        var result = await sut.ReadAsync(ApplicationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.FiscalPrerequisiteSatisfied);
        Assert.Equal("EJ:a1000000-0000-4000-8000-00000000000c", result.ElectronicJournalEventReference);
        Assert.Equal(FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality, result.CompletionBasis);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task IssueOrReadAsync_WhenExistingAncestryConflicts_FailsClosed()
    {
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        references.FindByStatutoryApplicationCommandIdAsync(ApplicationId, Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance) with
            {
                StatutoryDiscountValidationId = Guid.NewGuid()
            });
        var sut = CreateService(references, orchestration, posServer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.IssueOrReadAsync(Command(), CancellationToken.None));

        Assert.Equal("ZERO_PAYABLE_STATUTORY_FISCAL_REFERENCE_CONFLICT", error.Message);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task IssueOrReadAsync_WhenCanonicalAmountsDoNotReconcile_FailsBeforePersistence()
    {
        var references = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServer = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var sut = CreateService(references, orchestration, posServer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.IssueOrReadAsync(Command() with { VatExclusiveBasisAmountMinorUnits = 2678 }, CancellationToken.None));

        Assert.Equal("ZERO_PAYABLE_STATUTORY_FISCAL_CONTEXT_INVALID", error.Message);
        await orchestration.DidNotReceiveWithAnyArgs().PreparePendingAsync(default!, default);
        await posServer.DidNotReceiveWithAnyArgs().TryIssueFiscalDocumentViaPosServerAsync(default, default!, default!, default);
    }

    private static ZeroPayableStatutoryFiscalIssuanceService CreateService(
        IFiscalIssuanceReferenceRepository references,
        IFiscalIssuanceOrchestrationService orchestration,
        IFiscalIssuancePosServerLiveIntegrationService posServer) =>
        new(references, orchestration, posServer, Options());

    private static ZeroPayableStatutoryFiscalIssuanceCommand Command() =>
        new(Finality(), Authority(), Guid.Parse("a1000000-0000-4000-8000-00000000000d"), 2679, "VAT_EXCLUSIVE", "LOCAL_ORDINANCE_APPLIED");

    private static StatutoryDiscountZeroPayableFinality Finality() =>
        new(
            SessionId,
            DecisionId,
            ApplicationId,
            ValidationId,
            PolicyId,
            OriginalTariffId,
            AppliedTariffId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "FULL_FEE_EXEMPTION",
            3000,
            3000,
            321,
            0,
            "PHP",
            "WEBPAY",
            AppliedAt.AddMinutes(-1),
            AppliedAt,
            Guid.Parse("a1000000-0000-4000-8000-00000000000e"));

    private static CompletionAuthority Authority() =>
        new(
            SessionId,
            AppliedTariffId,
            SiteId,
            SiteGroupId,
            CompletionBasisCodes.ZeroPayableStatutoryFinality,
            ApplicationId,
            AppliedAt,
            Guid.Parse("a1000000-0000-4000-8000-00000000000e"),
            0,
            "PHP",
            StatutoryDiscountDecisionCommandId: DecisionId,
            StatutoryDiscountPayableBasisApplicationCommandId: ApplicationId,
            StatutoryDiscountValidationId: ValidationId,
            AppliedPolicyReferenceId: PolicyId);

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        bool completed = false) =>
        new(
            FiscalIssuanceReferenceId: FiscalReferenceId,
            PaymentConfirmationId: null,
            PaymentAttemptId: null,
            ParkingSessionId: SessionId,
            TariffSnapshotId: AppliedTariffId,
            SiteId: SiteId,
            SitePosServerId: SitePosServerId,
            SitePosServerRef: "PITX-POS",
            PayableBasisRef: AppliedTariffId.ToString("D"),
            UpstreamFinalityReference: $"ZERO_PAYABLE_STATUTORY_FINALITY:{ApplicationId:D}",
            PosServerFiscalDocumentId: completed ? Guid.Parse("a1000000-0000-4000-8000-00000000000f") : null,
            FiscalIdentityId: completed ? Guid.Parse("a1000000-0000-4000-8000-000000000010") : null,
            FiscalSequencePolicyId: completed ? Guid.Parse("a1000000-0000-4000-8000-000000000011") : null,
            FiscalSequenceValue: completed ? 7 : null,
            FiscalDocumentNumber: completed ? "SI-00000007" : null,
            FiscalSeries: completed ? "SI" : null,
            FiscalNumberPrefixText: completed ? "SI-" : null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: completed ? AppliedAt.AddSeconds(1) : null,
            FiscalNumberAssignedByRef: completed ? "pos-server" : null,
            FiscalDocumentStatusCodeId: completed ? Guid.Parse("a1000000-0000-4000-8000-000000000012") : null,
            ResultClassification: completed ? FiscalIssuanceResultClassification.NewlyCreated : null,
            FiscalIssuanceEvidenceStatus: completed ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned : null,
            FiscalNumberAssignmentState: completed ? FiscalNumberAssignmentState.Assigned : FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.Parse("a1000000-0000-4000-8000-00000000000e"),
            PosServerResponseTimestamp: completed ? AppliedAt.AddSeconds(1) : null,
            FirstRecordedAt: AppliedAt,
            LastUpdatedAt: AppliedAt,
            RecordedByServiceIdentityId: null,
            FiscalDocumentTypeCodeId: Guid.Parse("a1000000-0000-4000-8000-000000000013"),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            CompletionBasis: FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality,
            CompletionAuthorityReferenceId: ApplicationId,
            StatutoryDiscountDecisionCommandId: DecisionId,
            StatutoryDiscountPayableBasisApplicationCommandId: ApplicationId,
            StatutoryDiscountValidationId: ValidationId,
            AppliedPolicyReferenceId: PolicyId,
            ElectronicJournalEventReference: completed ? "EJ:a1000000-0000-4000-8000-00000000000c" : null);

    private static FiscalIssuancePosServerIntegrationOptions Options() =>
        new()
        {
            RuntimeEnvironment = "Development",
            Endpoints =
            [
                new SitePosServerEndpointOptions
                {
                    SiteId = SiteId,
                    SitePosServerId = SitePosServerId,
                    SitePosServerRef = "PITX-POS",
                    BaseUrl = "http://pos-server/",
                    ApiKeyFile = "unused-in-unit-test",
                    Environment = "Development",
                    Enabled = true,
                    FiscalDocumentTypeCodeId = Guid.Parse("a1000000-0000-4000-8000-000000000013")
                }
            ]
        };
}
