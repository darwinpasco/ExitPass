using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsolePhpCurrencyTests
{
    [Fact]
    public void RequireForAmounts_WithPhpAndAmount_ReturnsPhp()
    {
        OperatorConsolePhpCurrency.RequireForAmounts("PHP", 12_500)
            .Should()
            .Be("PHP");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("php")]
    [InlineData("₱")]
    public void RequireForAmounts_WithMissingOrUnsupportedCurrency_FailsClosed(string? currencyCode)
    {
        var action = () => OperatorConsolePhpCurrency.RequireForAmounts(currencyCode, 12_500);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Operator Console monetary data requires currency code PHP.");
    }

    [Fact]
    public void RequireForAmounts_WithoutMoneyOrCurrency_RemainsAbsent()
    {
        OperatorConsolePhpCurrency.RequireForAmounts(null, null, null)
            .Should()
            .BeNull();
    }
}
