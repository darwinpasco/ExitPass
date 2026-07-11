using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests the statutory discount payable-basis computation contract used by Operator Console.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountComputationContractTests
{
    /// <summary>
    /// Proves Senior Citizen parking discounts use VAT-exclusive basis, 20% discount, and half-away-from-zero rounding.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeniorCitizenCases))]
    public void Compute_ForSeniorCitizenParking_ReturnsExpectedVatExclusiveDiscount(
        long grossAmountMinorUnits,
        long expectedVatExclusiveMinorUnits,
        long expectedVatMinorUnits,
        long expectedDiscountMinorUnits,
        long expectedFinalPayableMinorUnits)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(grossAmountMinorUnits, "SENIOR_CITIZEN"));

        AssertAccepted(
            result,
            "SENIOR_CITIZEN",
            grossAmountMinorUnits,
            expectedVatExclusiveMinorUnits,
            expectedVatMinorUnits,
            expectedDiscountMinorUnits,
            expectedFinalPayableMinorUnits);
    }

    /// <summary>
    /// Proves PWD parking discounts use the same currently supported statutory VAT-exclusive computation contract.
    /// </summary>
    [Theory]
    [MemberData(nameof(PwdCases))]
    public void Compute_ForPwdParking_ReturnsExpectedVatExclusiveDiscount(
        long grossAmountMinorUnits,
        long expectedVatExclusiveMinorUnits,
        long expectedVatMinorUnits,
        long expectedDiscountMinorUnits,
        long expectedFinalPayableMinorUnits)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(grossAmountMinorUnits, "PWD"));

        AssertAccepted(
            result,
            "PWD",
            grossAmountMinorUnits,
            expectedVatExclusiveMinorUnits,
            expectedVatMinorUnits,
            expectedDiscountMinorUnits,
            expectedFinalPayableMinorUnits);
    }

    /// <summary>
    /// Proves zero and negative gross amounts fail closed instead of producing payable amounts.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_WhenGrossAmountInvalid_FailsClosed(long grossAmountMinorUnits)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(grossAmountMinorUnits, "SENIOR_CITIZEN"));

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("PAYABLE_BASIS_COMPONENTS_MISSING");
        result.VatExclusiveAmountMinorUnits.Should().BeNull();
        result.VatAmountMinorUnits.Should().BeNull();
        result.StatutoryDiscountAmountMinorUnits.Should().BeNull();
        result.FinalPayableAmountMinorUnits.Should().BeNull();
    }

    /// <summary>
    /// Proves unsupported entitlement categories fail closed for this slice.
    /// </summary>
    [Theory]
    [InlineData("OTHER_STATUTORY")]
    [InlineData("SOLO_PARENT")]
    public void Compute_WhenEntitlementUnsupported_FailsClosed(string entitlementType)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(11200, entitlementType));

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_ENTITLEMENT_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
        result.FinalPayableAmountMinorUnits.Should().BeNull();
    }

    /// <summary>
    /// Proves unsupported policy benefit posture fails closed instead of applying an invented rule.
    /// </summary>
    [Fact]
    public void Compute_WhenBenefitTypeUnsupported_FailsClosed()
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(11200, "SENIOR_CITIZEN") with { BenefitType = "FREE_DURATION" });

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
    }

    /// <summary>
    /// Proves unsupported discount base scopes fail closed instead of mixing gross/net statutory rules.
    /// </summary>
    [Theory]
    [InlineData("GROSS")]
    [InlineData("NET")]
    [InlineData("NOT_APPLICABLE")]
    public void Compute_WhenDiscountBaseScopeUnsupported_FailsClosed(string discountBaseScope)
    {
        var result = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            Request(11200, "PWD") with { DiscountBaseScope = discountBaseScope });

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("POLICY_DISCOUNT_BASE_SCOPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
    }

    public static TheoryData<long, long, long, long, long> SeniorCitizenCases() => new()
    {
        // gross, VAT-exclusive, VAT, 20% statutory discount, final payable
        { 11200, 10000, 1200, 2000, 8000 },
        { 12500, 11161, 1339, 2232, 8929 },
        { 6, 5, 1, 1, 4 }
    };

    public static TheoryData<long, long, long, long, long> PwdCases() => new()
    {
        // gross, VAT-exclusive, VAT, 20% statutory discount, final payable
        { 22400, 20000, 2400, 4000, 16000 },
        { 9999, 8928, 1071, 1786, 7142 },
        { 6, 5, 1, 1, 4 }
    };

    private static OperatorConsoleStatutoryDiscountComputationRequest Request(
        long grossAmountMinorUnits,
        string entitlementType) =>
        new(
            grossAmountMinorUnits,
            entitlementType,
            OperatorConsoleStatutoryDiscountComputationContract.SupportedBenefitType,
            OperatorConsoleStatutoryDiscountComputationContract.SupportedDiscountBaseScope);

    private static void AssertAccepted(
        OperatorConsoleStatutoryDiscountComputationResult result,
        string entitlementType,
        long grossAmountMinorUnits,
        long expectedVatExclusiveMinorUnits,
        long expectedVatMinorUnits,
        long expectedDiscountMinorUnits,
        long expectedFinalPayableMinorUnits)
    {
        result.Accepted.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.GrossAmountMinorUnits.Should().Be(grossAmountMinorUnits);
        result.VatExclusiveAmountMinorUnits.Should().Be(expectedVatExclusiveMinorUnits);
        result.VatAmountMinorUnits.Should().Be(expectedVatMinorUnits);
        result.StatutoryDiscountAmountMinorUnits.Should().Be(expectedDiscountMinorUnits);
        result.FinalPayableAmountMinorUnits.Should().Be(expectedFinalPayableMinorUnits);
        result.EntitlementType.Should().Be(entitlementType);
        result.BenefitType.Should().Be(OperatorConsoleStatutoryDiscountComputationContract.SupportedBenefitType);
        result.DiscountBaseScope.Should().Be(OperatorConsoleStatutoryDiscountComputationContract.SupportedDiscountBaseScope);
        result.VatRate.Should().Be(0.12m);
        result.StatutoryDiscountRate.Should().Be(0.20m);
        result.RoundingMode.Should().Be("HALF_AWAY_FROM_ZERO");
    }
}
