using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class PitxJuanZReadingAuthoritySeedTests
{
    private static readonly string Script = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "scripts", "v1.3", "catalog", "Grant-PitxJuanZReadingAuthority.sql"));

    [Fact]
    public void SeedTargetsOnlyJuanAtPitxAndPreservesSiteOperatorSeparation()
    {
        Script.Should().Contain("username_normalized = 'juandc01'");
        Script.Should().Contain("display_name = 'Juan Dela Cruz'");
        Script.Should().Contain("2d1dcdf8-f563-537c-8542-0bde7cc9da97");
        Script.Should().Contain("PITX Level 3");
        Script.Should().Contain("role.role_code = 'FISCAL_Z_READING_CLOSER'");
        Script.Should().Contain("r.role_code = 'SITE_OPERATOR'");
        Script.Should().Contain("scope_grant.scope_type::text <> 'SITE'");
        Script.Should().Contain("scope_grant.site_id <> target_site");
        Script.Should().Contain("exactly one active PITX Level 3 scope");
        Script.Should().Contain("Ordinary SITE_OPERATOR users must not inherit Z close authority.");
        Script.Should().Contain("Canonical reference-data service identity is unavailable or ambiguous.");
        Script.Should().Contain("The fiscal Z closer role code uses a non-canonical identity.");
        Script.Should().NotContain("SET role_code = 'SITE_OPERATOR'");
    }

    [Fact]
    public void DedicatedRoleGrantsOnlyZGenerationAndVerifiesTheCompleteEffectiveBundle()
    {
        var required = new[]
        {
            "fiscal-reporting.ej.read",
            "fiscal-reporting.ej.export",
            "fiscal-reporting.x.read",
            "fiscal-reporting.x.generate",
            "fiscal-reporting.z.read",
            "fiscal-reporting.z.generate"
        };
        foreach (var permission in required) Script.Should().Contain($"'{permission}'");

        Script.Should().Contain("permission.permission_code <> 'fiscal-reporting.z.generate'");
        Script.Should().Contain("Fiscal Z Reading Closer contains unrelated permissions.");
        Script.Should().Contain("requires_elevated_approval");
        Script.Should().Contain("sign out and authenticate again");
        Script.Should().NotContain("period_sequence");
        Script.Should().NotContain("reset_counter");
        Script.Should().NotContain("grand_total");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ExitPass.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("ExitPass repository root was not found.");
    }
}
