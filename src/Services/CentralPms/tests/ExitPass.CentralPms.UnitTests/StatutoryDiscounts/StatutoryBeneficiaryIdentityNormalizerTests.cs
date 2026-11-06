using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryDiscounts;

public sealed class StatutoryBeneficiaryIdentityNormalizerTests
{
    [Fact]
    public void TryNormalize_AcceptsCustomerNameBoundaryWithoutTruncation()
    {
        var name = new string('A', StatutoryBeneficiaryIdentityNormalizer.BeneficiaryFullNameMaximumLength);

        var accepted = StatutoryBeneficiaryIdentityNormalizer.TryNormalize(
            new StatutoryBeneficiaryIdentityInput(name, "PWD_ID", "PWD-0001", null),
            out var normalized);

        Assert.True(accepted);
        Assert.Equal(name, normalized?.BeneficiaryFullName);
    }

    [Fact]
    public void TryNormalize_RejectsNameThatCannotFitInvoiceCustomerSnapshot()
    {
        var name = new string('A', StatutoryBeneficiaryIdentityNormalizer.BeneficiaryFullNameMaximumLength + 1);

        var accepted = StatutoryBeneficiaryIdentityNormalizer.TryNormalize(
            new StatutoryBeneficiaryIdentityInput(name, "SENIOR_CITIZEN_ID", "SC-0001", null),
            out var normalized);

        Assert.False(accepted);
        Assert.Null(normalized);
    }
}
