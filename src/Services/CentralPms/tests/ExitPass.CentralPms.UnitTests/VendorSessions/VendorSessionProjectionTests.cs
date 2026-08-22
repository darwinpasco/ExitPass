using ExitPass.CentralPms.Application.VendorSessions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorSessions;

/// <summary>Projection boundary regressions after Site Adapter routing replaced direct HikCentral composition.</summary>
public sealed class VendorSessionProjectionTests
{
    [Fact]
    public void Projection_RetainsSourceAdapterIdentityAndBothUsableIdentifiers()
    {
        var adapterId = Guid.NewGuid();
        var projection = Projection() with { SourceAdapterIdentityId = adapterId };
        Assert.Equal(adapterId, projection.SourceAdapterIdentityId);
        Assert.Equal("CARD-A", projection.CardNum);
        Assert.Equal("PLATE-A", projection.PlateLicense);
    }

    [Fact]
    public void Architecture_CentralPmsContainsNoNormalDirectHikCentralProjectionClient()
    {
        var root = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root,
            "src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorSessions/HikCentralVendorSessionProjectionSyncService.cs")));
        Assert.False(File.Exists(Path.Combine(root,
            "src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/HikCentralVendorPmsParkingResolutionClient.cs")));
        var project = File.ReadAllText(Path.Combine(root,
            "src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/ExitPass.CentralPms.Infrastructure.csproj"));
        Assert.DoesNotContain("VendorPmsAdapter.Infrastructure", project, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaChange_IsV13PatchAndDoesNotModifyLockedV12Baseline()
    {
        var root = FindRepositoryRoot();
        var patch = File.ReadAllText(Path.Combine(root,
            "infra/db/patches/ExitPass_MultiSiteVendorAdapterRouting_v1.3.sql"));
        Assert.Contains("source_adapter_identity_id", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", patch, StringComparison.OrdinalIgnoreCase);
    }

    private static VendorSessionProjection Projection() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "Lot A", "P1", "Entry",
        "L1", "Lane 1", "ENTRY", "RECORD-A", "CARD-A", "PLATE-A", DateTimeOffset.UtcNow, null,
        "1", "1", null, "/vendor/passageway", new string('a', 64), "record:a", DateTimeOffset.UtcNow,
        "VENDOR_RECORD_GUID", "HIKCENTRAL|GUID|RECORD-A", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, VendorSessionProjectionStatus.Active, Guid.NewGuid(), DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
               !File.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
