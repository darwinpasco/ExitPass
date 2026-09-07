using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class HumanAuthenticationSchemaTests
{
    [Fact]
    public void Patch_DefinesCurrentRuntimePersistenceContract()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "infra", "db", "patches", "ExitPass_HumanAuthentication_v1.3.sql"));

        source.Should().Contain("GENERATED ALWAYS AS (lower(btrim(username))) STORED");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.local_credentials");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.human_sessions");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.authentication_attempts");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.credential_challenges");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.user_mfa_authenticators");
        source.Should().Contain("CREATE TABLE IF NOT EXISTS identity.user_role_scope_grants");
        source.Should().Contain("ux_local_credentials__current_user");
        source.Should().Contain("ux_user_role_scope_grants__current_exact");
    }

    [Fact]
    public void Bootstrap_OrdersHumanAuthenticationBeforeOperatorContext()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "Services", "CentralPms", "tests", "ExitPass.CentralPms.IntegrationTests",
            "Shared", "StatutoryDiscountCanonicalDatabaseFixture.cs"));

        source.IndexOf("ExitPass_HumanAuthentication_v1.3.sql", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("ExitPass_OperatorConsoleOperatingContext_v1.3.sql", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_CoversHealthDependenciesAndReferentialAuthorities()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "infra", "db", "patches", "validation", "Validate_HumanAuthentication_v1.3.sql"));

        source.Should().Contain("identity.local_credentials");
        source.Should().Contain("identity.human_sessions");
        source.Should().Contain("fk_human_sessions__local_credential");
        source.Should().Contain("fk_user_role_scope_grants__site_group");
        source.Should().Contain("CANONICAL_HUMAN_AUTH_SCHEMA_VALIDATION_PASSED");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
