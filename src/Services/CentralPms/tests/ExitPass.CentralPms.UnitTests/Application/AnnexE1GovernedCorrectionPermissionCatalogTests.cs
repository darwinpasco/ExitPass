using ExitPass.CentralPms.Application.Security;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class AnnexE1GovernedCorrectionPermissionCatalogTests
{
    [Fact]
    public void SensitiveAnnexE1PoliciesUseDedicatedPermissions()
    {
        Assert.Equal(
            ["fiscal-reporting.annex-e1.known-zero.attest"],
            CentralPmsRbacPolicyCatalog.ResolvePermissions("FiscalReportingAnnexE1KnownZeroAttest"));
        Assert.Equal(
            ["fiscal-reporting.annex-e1.correct"],
            CentralPmsRbacPolicyCatalog.ResolvePermissions("FiscalReportingAnnexE1Correct"));
        Assert.DoesNotContain(
            "fiscal-reporting.z.generate",
            CentralPmsRbacPolicyCatalog.ResolvePermissions("FiscalReportingAnnexE1KnownZeroAttest"));
        Assert.DoesNotContain(
            "fiscal-reporting.z.generate",
            CentralPmsRbacPolicyCatalog.ResolvePermissions("FiscalReportingAnnexE1Correct"));
    }

    [Fact]
    public void ForwardOnlyCatalogPatchCreatesSensitiveAuditedPermissionsWithoutRoleAssignment()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(root,"scripts","v1.3","catalog","Add-AnnexE1GovernedCorrectionPermissions.sql"));

        Assert.Contains("'fiscal-reporting.annex-e1.known-zero.attest'",script);
        Assert.Contains("'fiscal-reporting.annex-e1.correct'",script);
        Assert.Contains("is_sensitive",script);
        Assert.Contains("requires_audit",script);
        Assert.DoesNotContain("INSERT INTO identity.role_permissions",script,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO identity.user_roles",script,StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"ExitPass.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("ExitPass repository root was not found.");
    }
}
