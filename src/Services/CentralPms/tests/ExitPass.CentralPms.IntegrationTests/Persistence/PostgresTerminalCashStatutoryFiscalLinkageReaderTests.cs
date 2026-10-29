using System.Reflection;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class PostgresTerminalCashStatutoryFiscalLinkageReaderTests
{
    private static readonly Guid DecisionId = Guid.Parse("51000000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("51000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("51000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("51000000-0000-0000-0000-000000000004");
    private static readonly Guid OriginalTariffId = Guid.Parse("51000000-0000-0000-0000-000000000005");
    private static readonly Guid AppliedTariffId = Guid.Parse("51000000-0000-0000-0000-000000000006");

    [Theory]
    [InlineData("SENIOR_CITIZEN", "SENIOR_CITIZEN_ID")]
    [InlineData("PWD", "PWD_ID")]
    public async Task FullFeeExemption_AttachesFinalizedBeneficiaryIdentity(
        string entitlementType,
        string statutoryIdType)
    {
        var identity = Identity(entitlementType, statutoryIdType);
        var repository = new FixedIdentityRepository(identity);
        var reader = new PostgresTerminalCashStatutoryFiscalLinkageReader("Host=unused", repository);

        var result = await AttachAsync(reader, Complete(entitlementType, "FULL_FEE_EXEMPTION"), Subject());

        result.Status.Should().Be(TerminalCashStatutoryFiscalLinkageStatus.CompleteApprovedContext);
        result.Context!.BeneficiaryIdentity.Should().BeSameAs(identity);
        repository.GetCount.Should().Be(1);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public async Task PercentageDiscount_PreservesExistingFinalizedIdentityBehavior(int rateBasisPoints)
    {
        var identity = Identity("SENIOR_CITIZEN", "SENIOR_CITIZEN_ID");
        var reader = new PostgresTerminalCashStatutoryFiscalLinkageReader(
            "Host=unused",
            new FixedIdentityRepository(identity));
        var context = Context("SENIOR_CITIZEN", "PERCENTAGE_DISCOUNT") with
        {
            StatutoryDiscountRateBasisPoints = rateBasisPoints,
            PercentageComponentSeparatelyProven = true
        };

        var result = await AttachAsync(
            reader,
            TerminalCashStatutoryFiscalLinkageResult.Complete(context),
            Subject());

        result.Status.Should().Be(TerminalCashStatutoryFiscalLinkageStatus.CompleteApprovedContext);
        result.Context!.BeneficiaryIdentity.Should().BeSameAs(identity);
    }

    [Theory]
    [InlineData("UNFINALIZED")]
    [InlineData("WRONG_DECISION")]
    [InlineData("WRONG_SITE")]
    [InlineData("WRONG_ENTITLEMENT")]
    public async Task InvalidIdentityLinkage_FailsClosed(string mismatch)
    {
        var identity = Identity("SENIOR_CITIZEN", "SENIOR_CITIZEN_ID") with
        {
            IsFinal = mismatch != "UNFINALIZED",
            StatutoryDiscountDecisionCommandId = mismatch == "WRONG_DECISION" ? Guid.NewGuid() : DecisionId,
            SiteId = mismatch == "WRONG_SITE" ? Guid.NewGuid() : SiteId,
            EntitlementType = mismatch == "WRONG_ENTITLEMENT" ? "PWD" : "SENIOR_CITIZEN"
        };
        var reader = new PostgresTerminalCashStatutoryFiscalLinkageReader(
            "Host=unused",
            new FixedIdentityRepository(identity));

        var result = await AttachAsync(
            reader,
            Complete("SENIOR_CITIZEN", "FULL_FEE_EXEMPTION"),
            Subject());

        result.Status.Should().Be(TerminalCashStatutoryFiscalLinkageStatus.TerminallyInconsistent);
        result.SafeErrorCode.Should().Be("STATUTORY_BENEFICIARY_IDENTITY_REQUIRED");
        result.Context.Should().BeNull();
    }

    [Fact]
    public async Task SupportedEntitlement_WithoutIdentityRepository_FailsClosed()
    {
        var reader = new PostgresTerminalCashStatutoryFiscalLinkageReader("Host=unused");

        var result = await AttachAsync(
            reader,
            Complete("SENIOR_CITIZEN", "FULL_FEE_EXEMPTION"),
            Subject());

        result.Status.Should().Be(TerminalCashStatutoryFiscalLinkageStatus.TerminallyInconsistent);
        result.SafeErrorCode.Should().Be("STATUTORY_BENEFICIARY_IDENTITY_REQUIRED");
    }

    private static TerminalCashStatutoryFiscalLinkageResult Complete(string entitlementType, string benefitType) =>
        TerminalCashStatutoryFiscalLinkageResult.Complete(Context(entitlementType, benefitType));

    private static TerminalCashStatutoryFiscalLinkageContext Context(string entitlementType, string benefitType) =>
        new(
            DecisionId,
            Guid.Parse("51000000-0000-0000-0000-000000000007"),
            Guid.Parse("51000000-0000-0000-0000-000000000008"),
            Guid.Parse("51000000-0000-0000-0000-000000000009"),
            SessionId,
            SiteId,
            SiteGroupId,
            OriginalTariffId,
            AppliedTariffId,
            Guid.Parse("51000000-0000-0000-0000-000000000010"),
            "LOCAL_ORDINANCE_APPLIED",
            entitlementType,
            "ASSISTED_PAYMENT_TERMINAL",
            OriginalAmountMinorUnits: 5000,
            VatExclusiveBasisAmountMinorUnits: 4464,
            VatAmountMinorUnits: 536,
            VatTreatment: "VAT_INCLUSIVE_NO_EXEMPTION",
            StatutoryDiscountAmountMinorUnits: 4464,
            FinalPayableAmountMinorUnits: 0,
            Currency: "PHP",
            DecisionTimestamp: DateTimeOffset.Parse("2026-10-29T00:00:00Z"),
            AppliedAt: DateTimeOffset.Parse("2026-10-29T00:01:00Z"),
            IdControlReference: "TEST-0001",
            PolicyCode: entitlementType == "PWD"
                ? "PH_PARANAQUE_PWD_FREE_PARKING"
                : "PH_PARANAQUE_SENIOR_FREE_PARKING",
            PolicyVersionId: Guid.Parse("51000000-0000-0000-0000-000000000011"),
            OrdinanceReference: "City Ordinance No. 48",
            PolicyBenefitType: benefitType);

    private static StatutoryFiscalLinkageSubject Subject() =>
        new(SessionId, AppliedTariffId, SiteId, SiteGroupId, 0, "PHP");

    private static StatutoryBeneficiaryIdentityRecord Identity(string entitlementType, string statutoryIdType)
    {
        var capturedAt = DateTimeOffset.Parse("2026-10-29T00:00:00Z");
        var actorId = Guid.Parse("51000000-0000-0000-0000-000000000012");
        return new StatutoryBeneficiaryIdentityRecord(
            DecisionId,
            SiteId,
            entitlementType,
            "Synthetic Beneficiary",
            statutoryIdType,
            "TEST-0001",
            BeneficiaryTin: "000-000-000-000",
            CapturedByActorRef: actorId,
            CapturedByChannel: StatutoryBeneficiaryIdentityCaptureChannels.OperatorConsole,
            CapturedAt: capturedAt,
            LastUpdatedByActorRef: actorId,
            LastUpdatedByChannel: StatutoryBeneficiaryIdentityCaptureChannels.OperatorConsole,
            LastUpdatedAt: capturedAt,
            ReviewedByActorRef: actorId,
            ReviewedAt: capturedAt.AddMinutes(1),
            ApprovalDecisionReference: "APPROVE",
            CorrelationReference: Guid.Parse("51000000-0000-0000-0000-000000000013"),
            IsFinal: true);
    }

    private static async Task<TerminalCashStatutoryFiscalLinkageResult> AttachAsync(
        PostgresTerminalCashStatutoryFiscalLinkageReader reader,
        TerminalCashStatutoryFiscalLinkageResult result,
        StatutoryFiscalLinkageSubject subject)
    {
        var method = typeof(PostgresTerminalCashStatutoryFiscalLinkageReader).GetMethod(
            "AttachRequiredBeneficiaryIdentityAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = method!.Invoke(reader, [result, subject, CancellationToken.None])
            .Should().BeAssignableTo<Task<TerminalCashStatutoryFiscalLinkageResult>>().Subject;
        return await task;
    }

    private sealed class FixedIdentityRepository(StatutoryBeneficiaryIdentityRecord? identity)
        : IStatutoryBeneficiaryIdentityRepository
    {
        public int GetCount { get; private set; }

        public Task<StatutoryBeneficiaryIdentityWriteResult> SaveBeforeDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid actorUserId,
            string captureChannel,
            StatutoryBeneficiaryIdentityInput input,
            Guid correlationReference,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryBeneficiaryIdentityWriteResult> FinalizeApprovedAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid reviewedByActorRef,
            string approvalDecisionReference,
            Guid correlationReference,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryBeneficiaryIdentityRecord?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(identity?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId
                ? identity
                : null);
        }
    }
}
