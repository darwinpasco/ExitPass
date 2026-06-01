using ExitPass.GateIntegrationService.Application.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class GateActionAdapterOptionsTests
{
    [Fact]
    public void ResolveMode_WhenModeMissing_DefaultsToNoOp()
    {
        var options = new GateActionAdapterOptions();

        Assert.Equal(GateActionAdapterMode.NoOp, options.ResolveMode());
    }

    [Theory]
    [InlineData("NoOp", GateActionAdapterMode.NoOp)]
    [InlineData("HikCentralFake", GateActionAdapterMode.HikCentralFake)]
    [InlineData("HikCentralLive", GateActionAdapterMode.HikCentralLive)]
    [InlineData("hikcentralfake", GateActionAdapterMode.HikCentralFake)]
    public void ResolveMode_WhenModeKnown_ReturnsConfiguredMode(
        string mode,
        GateActionAdapterMode expected)
    {
        var options = new GateActionAdapterOptions { Mode = mode };

        Assert.Equal(expected, options.ResolveMode());
    }

    [Fact]
    public void ResolveMode_WhenModeUnknown_FailsDeterministically()
    {
        var options = new GateActionAdapterOptions { Mode = "HikCentralProbablyLive" };

        var exception = Assert.Throws<InvalidOperationException>(() => options.ResolveMode());

        Assert.Contains("Unsupported gate action adapter mode", exception.Message, StringComparison.Ordinal);
    }
}

#pragma warning restore CS1591
