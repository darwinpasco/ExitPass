using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class PitxAptRegisteredIdentityScriptTests
{
    private static readonly string Script = File.ReadAllText(FindRepositoryFile(
        "scripts", "v1.3", "local-runtime", "Set-PitxAptRegisteredIdentity.ps1"));

    [Fact]
    public void SiteGroupComesFromTheAuthoritativeSiteRelationship()
    {
        Assert.Contains("JOIN sites.sites site ON site.site_id=a.site_id", Script, StringComparison.Ordinal);
        Assert.Contains("a.site_id,site.site_group_id", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("a.site_group_id", Script, StringComparison.Ordinal);
        Assert.Contains("active_site IS NULL OR active_group IS NULL", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectModeValidatesTheDeployedSiteGroupRelationship()
    {
        Assert.Contains("'draft_assignment_site'", Script, StringComparison.Ordinal);
        Assert.Contains("JOIN sites.sites s ON s.site_id=a.site_id", Script, StringComparison.Ordinal);
        Assert.Contains("'site_group_id',s.site_group_id", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernedCorrectionPreservesTheApprovedDeviceAndCredentialBoundary()
    {
        Assert.Contains("7af324ce-bc3a-44ef-b3c3-5d3880b39ffe", Script, StringComparison.Ordinal);
        Assert.Contains("874a7af5-add6-4736-9372-0f2a0886ba67", Script, StringComparison.Ordinal);
        Assert.Contains("PITX-APT-01", Script, StringComparison.Ordinal);
        Assert.Contains("ACE4_2E00_653A_D952", Script, StringComparison.Ordinal);
        Assert.Contains("previous_credential_version,resulting_credential_version", Script, StringComparison.Ordinal);
        Assert.Contains("active_site,active_group,0,0", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE identity.device_credential_history", Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE identity.device_provisioning_credentials", Script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)} from {AppContext.BaseDirectory}.");
    }
}
