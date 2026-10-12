using ExitPass.CentralPms.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Security;

public sealed class OperatorConsoleDeviceBindingCookieTests
{
    [Fact]
    public void Issue_CreatesLongLivedSecureServerOnlyWorkstationProofCookie()
    {
        var now = new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
        var context = new DefaultHttpContext();

        OperatorConsoleDeviceBindingCookie.Issue(
            context.Response,
            "governed-browser-proof-with-more-than-32-characters",
            now);

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.Single()!);
        cookie.Name.Value.Should().Be(OperatorConsoleDeviceBindingCookie.CookieName);
        cookie.Expires.Should().Be(now.AddDays(400));
        cookie.MaxAge.Should().Be(TimeSpan.FromDays(400));
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        cookie.Path.Value.Should().Be("/");
    }

    [Fact]
    public void Issue_RenewsBrowserProofRetentionFromEachSuccessfulValidation()
    {
        var firstValidation = new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
        var laterValidation = firstValidation.AddYears(3);
        var context = new DefaultHttpContext();

        OperatorConsoleDeviceBindingCookie.Issue(
            context.Response,
            "same-governed-browser-proof-with-more-than-32-characters",
            laterValidation);

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.Single()!);
        cookie.Expires.Should().Be(laterValidation.AddDays(400));
        cookie.MaxAge.Should().Be(TimeSpan.FromDays(400));
        cookie.Value.Value.Should().Be("same-governed-browser-proof-with-more-than-32-characters");
    }
}
