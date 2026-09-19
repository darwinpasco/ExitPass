using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountParkingPolicyResolutionRulesTests
{
    [Fact]
    public void Discovery_KeepsResidentOnlySeniorAndPwdEntitlementsAvailableBeforeSelection()
    {
        StatutoryDiscountParkingPolicyResolutionRules.SatisfiesResidencyForResolution(
                requestedEntitlementType: null,
                beneficiaryResidencySatisfied: null,
                beneficiaryResidencyScope: "RESIDENT_ONLY")
            .Should().BeTrue();
    }

    [Fact]
    public void SelectedResidentOnlyEntitlement_RequiresAffirmativeResidencyConfirmation()
    {
        StatutoryDiscountParkingPolicyResolutionRules.SatisfiesResidencyForResolution(
                requestedEntitlementType: "SENIOR_CITIZEN",
                beneficiaryResidencySatisfied: false,
                beneficiaryResidencyScope: "RESIDENT_ONLY")
            .Should().BeFalse();

        StatutoryDiscountParkingPolicyResolutionRules.SatisfiesResidencyForResolution(
                requestedEntitlementType: "PWD",
                beneficiaryResidencySatisfied: true,
                beneficiaryResidencyScope: "RESIDENT_ONLY")
            .Should().BeTrue();
    }

    [Fact]
    public void SeparateSeniorAndPwdPolicies_DoNotConflictAtEqualJurisdictionPrecedence()
    {
        StatutoryDiscountParkingPolicyResolutionRules.CompetesAtSamePrecedence(
                "SENIOR_CITIZEN",
                "PWD",
                candidateScopeWeight: 1,
                selectedScopeWeight: 1,
                candidatePrecedenceRank: 100,
                selectedPrecedenceRank: 100)
            .Should().BeFalse();

        StatutoryDiscountParkingPolicyResolutionRules.CompetesAtSamePrecedence(
                "PWD",
                "PWD",
                candidateScopeWeight: 1,
                selectedScopeWeight: 1,
                candidatePrecedenceRank: 100,
                selectedPrecedenceRank: 100)
            .Should().BeTrue();
    }
}
