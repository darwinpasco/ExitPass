using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class HumanAuthenticationPolicySurfaceTests
{
    [Fact]
    public void Public_routes_expose_totp_reset_and_no_email_or_link_reset_path()
    {
        var humanEndpoints = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Api", "Endpoints", "HumanAuthenticationEndpoints.cs");
        var administrationEndpoints = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Api", "Endpoints", "ManagementPlatformIdentityAdministrationEndpoints.cs");

        humanEndpoints.Should().Contain("/password-resets");
        humanEndpoints.Should().Contain("ResetPasswordWithTotpAsync");
        humanEndpoints.Should().Contain("/{sessionReference:guid}/password/change");
        humanEndpoints.Should().NotContain("MapPost(\"/password-reset-requests\"");
        humanEndpoints.Should().NotContain("MapPost(\"/activations\"");
        administrationEndpoints.Should().NotContain("credential-reset-challenges");
        administrationEndpoints.Should().NotContain("mfa-authenticators/reset");
        administrationEndpoints.Should().NotContain("mfa-authenticators/remove");
    }

    [Fact]
    public void Native_parking_uses_the_audience_neutral_web_password_change_route()
    {
        var endpoints = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Api", "Endpoints", "HumanAuthenticationEndpoints.cs");
        var audiences = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Application", "HumanAuthentication", "HumanAuthenticationModels.cs");

        endpoints.Should().Contain("web.MapPost(\"/password/change\", ChangePasswordAsync)")
            .And.Contain("service.ChangePasswordAsync(token, body.CurrentPassword, body.NewPassword, body.TotpCode")
            .And.NotContain("native-parking/password/change");
        audiences.Should().Contain("audience is ManagementPlatform or OperatorConsole or NativeParkingApp");
    }

    [Fact]
    public void Schema_patch_requires_expiry_for_change_required_credentials()
    {
        var sql = ReadRepoFile("infra", "db", "patches", "ExitPass_HumanAuthenticationLifecycle_v1.3.sql");
        var startup = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Api", "Program.cs");

        sql.Should().Contain("temporary_password_expires_at");
        sql.Should().Contain("credential_status = 'CHANGE_REQUIRED' AND temporary_password_expires_at IS NOT NULL");
        startup.Should().Contain("TemporaryPasswordHours == 72");
    }

    [Fact]
    public void Password_replacement_rotates_credential_versions_and_revokes_active_sessions()
    {
        var repository = ReadRepoFile("src", "Services", "CentralPms", "src",
            "ExitPass.CentralPms.Infrastructure", "HumanAuthentication", "PostgresHumanAuthenticationRepository.cs");

        repository.Should().Contain("credential_version=credential_version+1");
        repository.Should().Contain("revocation_reason_code='CREDENTIAL_CHANGED'");
        repository.Should().Contain("WHERE user_id=@user_id AND session_status='ACTIVE'");
    }

    [Fact]
    public void Ordinary_identity_read_models_cannot_return_bootstrap_secrets()
    {
        var ordinaryReadProperties = typeof(IdentityUserSummary).GetProperties()
            .Concat(typeof(IdentityUserDetail).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        ordinaryReadProperties.Should().NotContain("TemporaryPassword")
            .And.NotContain("TotpSharedSecret")
            .And.NotContain("TotpProvisioningUri")
            .And.NotContain("OneTimeBootstrap");
        typeof(CreateIdentityUserResult).GetProperty("OneTimeBootstrap").Should().NotBeNull();
    }

    [Fact]
    public void Create_user_contract_has_optional_descriptive_user_type_and_no_delivery_or_precreation_handoff()
    {
        var properties = typeof(CreateIdentityUserRequest).GetProperties().Select(property => property.Name).ToArray();

        properties.Should().Contain("UserType")
            .And.NotContain("ActivationDeliveryMode")
            .And.NotContain("AdminIssuedHandoffAcknowledged");
        typeof(OneTimeHumanBootstrapMaterial).GetProperty("PasswordChangeRequired").Should().NotBeNull();
    }

    private static string ReadRepoFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine([directory!.FullName, .. path]));
    }
}
