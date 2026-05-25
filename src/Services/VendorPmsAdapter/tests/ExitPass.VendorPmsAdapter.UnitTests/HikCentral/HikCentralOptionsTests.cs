using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.HikCentral;

/// <summary>
/// Unit tests for HikCentral Professional adapter configuration validation.
/// </summary>
public sealed class HikCentralOptionsTests
{
    /// <summary>
    /// Verifies that disabled HikCentral configuration does not require credentials in local mock/dev mode.
    /// </summary>
    [Fact]
    public void Validate_WhenDisabled_DoesNotRequireVendorCredentials()
    {
        var options = new HikCentralOptions
        {
            Enabled = false
        };

        var errors = options.Validate();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that enabled HikCentral configuration accepts a secret-backed HTTPS endpoint.
    /// </summary>
    [Fact]
    public void Validate_WhenEnabledAndComplete_ReturnsNoErrors()
    {
        var options = new HikCentralOptions
        {
            Enabled = true,
            BaseUrl = "https://hikcentral.example",
            AppKey = "test-ak",
            AppSecret = "test-secret",
            UserId = "exitpass-adapter"
        };

        var errors = options.Validate();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that configuration validation returns stable, secret-free error codes.
    /// </summary>
    [Fact]
    public void Validate_WhenEnabledAndInvalid_ReturnsSecretFreeErrors()
    {
        var options = new HikCentralOptions
        {
            Enabled = true,
            BaseUrl = "http://hikcentral.example",
            AppKey = "",
            AppSecret = "do-not-leak-this-secret",
            UserId = "bad/user"
        };

        var errors = options.Validate();

        Assert.Contains("HIKCENTRAL_BASE_URL_MUST_USE_HTTPS", errors);
        Assert.Contains("HIKCENTRAL_APP_KEY_REQUIRED", errors);
        Assert.Contains("HIKCENTRAL_USER_ID_INVALID", errors);
        Assert.DoesNotContain(errors, error => error.Contains("do-not-leak-this-secret", StringComparison.Ordinal));
    }
}
