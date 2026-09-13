using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class CredentialChallengeDeliveryTests
{
    [Fact]
    public void ActivationLink_ContainsOnlyOpaqueChallengeMaterialOnStableRoute()
    {
        var reference = Guid.NewGuid();
        var builder = new CredentialChallengeLinkBuilder(Options.Create(new CredentialChallengeDeliveryOptions
        {
            PublicAccountLifecycleBaseUrl = "https://accounts.exitpass.test/root"
        }));

        var url = builder.BuildUrl("ACCOUNT_ACTIVATION", reference, "secret_value");

        url.Should().Be($"https://accounts.exitpass.test/account/activate?challengeReference={reference:D}&challengeSecret=secret_value");
        url.Should().NotContainAny("username", "role", "siteGroup", "administrator");
    }

    [Theory]
    [InlineData("employee@example.com", true)]
    [InlineData(" employee@example.com ", true)]
    [InlineData("employee", false)]
    [InlineData("", false)]
    public void SmtpEmailValidation_IsDeterministic(string value, bool expected) =>
        SmtpCredentialChallengeDelivery.IsUsableEmail(value).Should().Be(expected);

    [Theory]
    [InlineData("https://accounts.exitpass.test", true)]
    [InlineData("http://accounts.exitpass.test", false)]
    [InlineData("https://accounts.exitpass.test?leak=value", false)]
    [InlineData("", false)]
    public void PublicLifecycleUrl_RequiresCleanHttpsOrigin(string value, bool expected) =>
        CredentialChallengeLinkBuilder.IsUsablePublicBaseUrl(value).Should().Be(expected);
}
