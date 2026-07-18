using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformSalesInvoiceProfileApiContractTests
{
    [Theory]
    [InlineData("SalesInvoiceProfileRead", "sales-invoice-profile.read")]
    [InlineData("SalesInvoiceProfileManage", "sales-invoice-profile.manage")]
    [InlineData("SalesInvoiceProfileApprove", "sales-invoice-profile.approve")]
    public void ManagementPlatformSalesInvoiceProfileApi_PoliciesMapToNarrowPermissions(
        string policyName,
        string permission)
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(policyName).Should().ContainSingle(permission);
    }

    [Fact]
    public void ManagementPlatformSalesInvoiceProfileApi_BrowserRequestDtosContainNoActorOrSecretFields()
    {
        var dtoTypes = new[]
        {
            typeof(ManagementPlatformFiscalIdentityMutationRequestDto),
            typeof(ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto),
            typeof(ManagementPlatformSalesInvoiceHeaderProfileRetirementRequestDto)
        };

        var propertyNames = dtoTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(name => name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("BaseUrl", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("CreatedByRef", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("UpdatedByRef", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("ApprovedByRef", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("RetiredByRef", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("TerminalId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ManagementPlatformSalesInvoiceProfileApi_ResponseDtosPreserveDistinctStatutoryDates()
    {
        var names = typeof(ManagementPlatformSalesInvoiceHeaderProfileDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        names.Should().Contain(nameof(ManagementPlatformSalesInvoiceHeaderProfileDto.BirAccreditationIssuedDate));
        names.Should().Contain(nameof(ManagementPlatformSalesInvoiceHeaderProfileDto.BirAccreditationValidUntil));
        names.Should().Contain(nameof(ManagementPlatformSalesInvoiceHeaderProfileDto.PtuIssuedDate));
        names.Should().NotContain("DateIssued");
        names.Should().NotContain("TerminalId");
    }

    [Fact]
    public void ManagementPlatformSalesInvoiceProfileApi_EndpointConstantsKeepPoliciesSeparate()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions("SalesInvoiceProfileRead")
            .Should()
            .Equal("sales-invoice-profile.read");
        CentralPmsRbacPolicyCatalog.ResolvePermissions("SalesInvoiceProfileApprove")
            .Should()
            .Equal("sales-invoice-profile.approve");

        CentralPmsRbacPolicyCatalog.ResolvePermissions("SalesInvoiceProfileManage")
            .Should()
            .NotContain("sales-invoice-profile.approve");
    }
}
