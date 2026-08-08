using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Net;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class IdentityAdministrationSecurityBoundaryTests
{
    [Fact]
    public void ActorAccessor_UsesAuthenticatedSessionClaims()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(HttpContextIdentityAdministrationActorAccessor.HumanSessionIdClaimType, sessionId.ToString())
            ], "I020Session"))
        };
        var accessor = new HttpContextIdentityAdministrationActorAccessor(new HttpContextAccessor { HttpContext = context });

        accessor.Current.Should().Be(new IdentityAdministrationActor(userId, sessionId));
    }

    [Fact]
    public void ActorAccessor_DoesNotTreatProductionHeadersAsAuthority()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-ExitPass-User-Id"] = Guid.NewGuid().ToString();
        context.Request.Headers["X-ExitPass-Permissions"] = "user.manage";
        context.Request.Headers["X-ExitPass-Human-Session-Id"] = Guid.NewGuid().ToString();
        var accessor = new HttpContextIdentityAdministrationActorAccessor(new HttpContextAccessor { HttpContext = context });

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void PublicRequests_HaveNoActorOrSecretProperties()
    {
        var requestTypes = new[]
        {
            typeof(CreateIdentityUserRequest), typeof(UpdateIdentityUserRequest), typeof(IdentityLifecycleRequest),
            typeof(CredentialResetChallengeRequest), typeof(AssignIdentityRoleRequest), typeof(GrantIdentityScopeRequest),
            typeof(CreatePrivilegedAccessRequest), typeof(DecidePrivilegedAccessRequest), typeof(ChangeIdentityMfaRequest)
        };
        var forbidden = new[] { "actor", "password", "secret", "token", "cipher", "totp", "sessionid" };

        requestTypes.SelectMany(type => type.GetProperties()).Select(property => property.Name)
            .Should().NotContain(name => forbidden.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SafeSessionAndMfaModels_ExposeNoCredentialMaterial()
    {
        var propertyNames = new[] { typeof(IdentitySessionSummary), typeof(IdentityMfaStatus) }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(name =>
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cipher", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Provision", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostedAdministrationRoute_DoesNotAcceptLegacyFixtureHeadersAsHumanAuthority()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ExitPass-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-ExitPass-Permissions", "user.view,user.manage");
        client.DefaultRequestHeaders.Add("X-ExitPass-Human-Session-Id", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/v1/management-platform/identity/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
