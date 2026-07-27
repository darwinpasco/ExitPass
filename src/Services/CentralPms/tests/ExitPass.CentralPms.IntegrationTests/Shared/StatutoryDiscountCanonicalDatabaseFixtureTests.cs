using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

public sealed class StatutoryDiscountCanonicalDatabaseFixtureTests
{
    [Fact]
    public void CreateDatabaseName_UsesSafeDisposablePrefix()
    {
        var databaseName = StatutoryDiscountCanonicalDatabaseFixture.CreateDatabaseName("ExitPass Statutory Fixture!");

        databaseName.Should().StartWith("exitpass_statutory_fixture_");
        databaseName.Should().MatchRegex("^[a-z][a-z0-9_]{0,62}$");
        databaseName.Length.Should().BeLessThanOrEqualTo(63);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("exitpass_v12_dev")]
    public void EnsureSafeDatabaseName_RejectsProtectedNames(string databaseName)
    {
        var action = () => StatutoryDiscountCanonicalDatabaseFixture.EnsureSafeDatabaseName(databaseName);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*protected database*");
    }
}
