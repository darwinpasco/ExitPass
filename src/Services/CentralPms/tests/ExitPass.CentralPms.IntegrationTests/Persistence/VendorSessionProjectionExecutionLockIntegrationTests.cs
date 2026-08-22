using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class VendorSessionProjectionExecutionLockIntegrationTests
{
    [Fact]
    public async Task TargetScopedAdvisoryLock_ContendsAndReleasesWithoutGlobalSerialization()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_central_pms_it_",
            "the canonical integration harness restricts the test to its task-owned disposable database");

        var sut = new PostgresVendorSessionProjectionExecutionLock(connectionString);
        var firstTarget = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondTarget = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var firstLease = await sut.TryAcquireAsync(firstTarget, CancellationToken.None);
        firstLease.Should().NotBeNull();
        (await sut.TryAcquireAsync(firstTarget, CancellationToken.None)).Should().BeNull();

        var independentLease = await sut.TryAcquireAsync(secondTarget, CancellationToken.None);
        independentLease.Should().NotBeNull();
        await independentLease!.DisposeAsync();

        await firstLease!.DisposeAsync();
        var reacquired = await sut.TryAcquireAsync(firstTarget, CancellationToken.None);
        reacquired.Should().NotBeNull();
        await reacquired!.DisposeAsync();
    }
}
