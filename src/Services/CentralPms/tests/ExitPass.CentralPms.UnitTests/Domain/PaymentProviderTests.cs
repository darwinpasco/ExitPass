using ExitPass.CentralPms.Domain.PaymentAttempts;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Domain;

/// <summary>
/// Unit tests for <see cref="PaymentProvider"/>.
/// </summary>
public sealed class PaymentProviderTests
{
    /// <summary>
    /// Verifies AUB QR Ph rail code is explicitly supported.
    /// </summary>
    [Fact]
    public void FromCode_WhenAubQrphProvided_ReturnsAubQrphRail()
    {
        var provider = PaymentProvider.FromCode("AUB_QRPH");

        Assert.Equal("AUB_QRPH", provider.Code);
        Assert.Equal(PaymentProvider.AubQrPh, provider);
    }

    /// <summary>
    /// Verifies AUB card cashier rail code is explicitly supported.
    /// </summary>
    [Fact]
    public void FromCode_WhenAubCardCashierProvided_ReturnsAubCardCashierRail()
    {
        var provider = PaymentProvider.FromCode("AUB_CARD_CASHIER");

        Assert.Equal("AUB_CARD_CASHIER", provider.Code);
        Assert.Equal(PaymentProvider.AubCardCashier, provider);
    }

    /// <summary>
    /// Verifies provider code matching remains deterministic and case-insensitive.
    /// </summary>
    [Fact]
    public void FromCode_WhenProviderCodeHasMixedCase_ReturnsCanonicalRail()
    {
        var provider = PaymentProvider.FromCode(" aub_qrph ");

        Assert.Equal("AUB_QRPH", provider.Code);
        Assert.Equal(PaymentProvider.AubQrPh, provider);
    }

    /// <summary>
    /// Verifies unknown AUB-like provider values are still rejected.
    /// </summary>
    [Theory]
    [InlineData("AUB")]
    [InlineData("AUB_CASHIER")]
    [InlineData("AUB_CARD")]
    [InlineData("AUB_QRPH_DEV")]
    public void FromCode_WhenUnknownAubLikeProviderCodeProvided_Throws(string code)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => PaymentProvider.FromCode(code));

        Assert.Contains($"Unsupported payment provider: {code}", exception.Message);
    }
}
