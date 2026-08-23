using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Auditing;

public sealed class CentralPmsAuditEventIntegrationArchitectureTests
{
    [Fact]
    public void NormalRuntime_ComposesAuditEventClientAndProjectionPublisher()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Api", "Program.cs"));
        var projection = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Infrastructure",
            "VendorSessions", "SiteVendorAdapterProjectionSyncService.cs"));

        program.Should().Contain("AddCentralPmsAuditEventClient");
        projection.Should().Contain("IAuditEventPublisher");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
