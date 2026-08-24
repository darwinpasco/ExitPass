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

    [Fact]
    public void Load_OrdersAuthoritativeProjectionPatchesAndValidators()
    {
        var options = StatutoryDiscountCanonicalDatabaseFixture.StatutoryDiscountCanonicalDatabaseFixtureOptions.Load();

        options.ApplicationSchemaSources.Select(source => Path.GetFileName(source.PatchPath)).Should().Equal(
            "HikCentralProjectionSchemaPatch.sql",
            "ExitPass_HikCentralProjectionSafety_v1.3.sql",
            "ExitPass_MultiSiteVendorAdapterRouting_v1.3.sql",
            "ExitPass_OperatorConsoleOperatingContext_v1.3.sql");
        options.ApplicationSchemaSources.Where(source => source.ValidatorPath is not null)
            .Select(source => Path.GetFileName(source.ValidatorPath)).Should().Equal(
            "Validate_HikCentralProjectionSafety_v1.3.sql",
            "Validate_MultiSiteVendorAdapterRouting_v1.3.sql",
            "Validate_OperatorConsoleOperatingContext_v1.3.sql");
        options.ApplicationSchemaSources.Should().OnlyContain(source => File.Exists(source.PatchPath));
        options.ApplicationSchemaSources.Where(source => source.ValidatorPath is not null).Should().OnlyContain(source =>
            File.Exists(source.ValidatorPath));
    }
}
