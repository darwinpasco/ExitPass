using ExitPass.CentralPms.Application.VendorParking;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ContinuityTariffCalculatorTests
{
    private static readonly Guid SiteId = Guid.Parse("35a625de-9034-4fb6-b527-0950d384e51e");
    private static readonly Guid DefinitionId = Guid.Parse("b6ec5a68-828d-5f0a-8f5c-f79926279736");
    private static readonly Guid RuleId = Guid.Parse("0d698f82-2bb4-5d09-9f79-f56e907d8cac");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 4, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 5000)]
    [InlineData(75, 5000)]
    [InlineData(76, 10000)]
    [InlineData(135, 10000)]
    [InlineData(136, 15000)]
    public async Task CalculateAsync_UsesGraceAndWholeStartedHour(int elapsedMinutes, long expectedMinorUnits)
    {
        var result = await CreateCalculator().CalculateAsync(
            SiteId,
            ContinuityVehicleTypes.Car,
            Now.AddMinutes(-elapsedMinutes),
            Now,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(expectedMinorUnits);
        result.Currency.Should().Be("PHP");
        result.TariffVersionReference.Should().Be("EXITPASS-CONTINUITY:PITX-L3-CAR-V1");
        result.ExpiresAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public async Task CalculateAsync_WhenVehicleTariffIsMissing_DoesNotReuseCarTariff()
    {
        var result = await CreateCalculator().CalculateAsync(
            SiteId,
            ContinuityVehicleTypes.Motorcycle,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be("CONTINUITY_TARIFF_NOT_CONFIGURED");
        result.AmountMinorUnits.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public async Task CalculateAsync_WhenVehicleTypeIsUnresolved_FailsClosed(string? vehicleType)
    {
        var result = await CreateCalculator().CalculateAsync(
            SiteId,
            vehicleType,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be("CONTINUITY_VEHICLE_TYPE_UNRESOLVED");
    }

    [Fact]
    public async Task CalculateAsync_WhenEntryIsBeyondClockTolerance_FailsClosed()
    {
        var result = await CreateCalculator().CalculateAsync(
            SiteId,
            ContinuityVehicleTypes.Car,
            Now.AddMinutes(3),
            Now,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be("CONTINUITY_ENTRY_TIME_INVALID");
    }

    [Fact]
    public async Task PreviewAsync_ComposesInitialFlatAndSucceedingUnitRules()
    {
        var definition = CreateDefinition() with
        {
            ParkingGracePeriodMinutes = 0,
            Rules =
            [
                new ContinuityTariffRule(Guid.NewGuid(), 1, "DURATION", "FLAT_RATE", 0, 180, null, null, 5000, null, null),
                new ContinuityTariffRule(Guid.NewGuid(), 2, "DURATION", "UNIT_DURATION", 180, null, null, null, 2000, 60, "WHOLE_STARTED_HOUR")
            ]
        };

        var result = await CreateCalculator().PreviewAsync(definition, Now.AddMinutes(-181), Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(7000);
        result.AppliedRuleIds.Should().Equal(definition.Rules.Select(rule => rule.SiteTariffRuleId));
    }

    [Fact]
    public async Task PreviewAsync_ComposesTieredDurationRules()
    {
        var definition = CreateDefinition() with
        {
            ParkingGracePeriodMinutes = 0,
            Rules =
            [
                new ContinuityTariffRule(Guid.NewGuid(), 1, "DURATION", "FLAT_RATE", 0, 180, null, null, 5000, null, null),
                new ContinuityTariffRule(Guid.NewGuid(), 2, "DURATION", "UNIT_DURATION", 180, 360, null, null, 2000, 60, "WHOLE_STARTED_HOUR"),
                new ContinuityTariffRule(Guid.NewGuid(), 3, "DURATION", "UNIT_DURATION", 360, null, null, null, 1000, 60, "WHOLE_STARTED_HOUR")
            ]
        };

        var result = await CreateCalculator().PreviewAsync(definition, Now.AddMinutes(-361), Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(12000);
    }

    [Theory]
    [InlineData("FLAT_RATE")]
    [InlineData("SESSION")]
    public async Task PreviewAsync_SupportsSingleChargeRules(string chargeType)
    {
        var definition = CreateDefinition() with
        {
            ParkingGracePeriodMinutes = 0,
            Rules = [new ContinuityTariffRule(Guid.NewGuid(), 1, "DURATION", chargeType, 0, null, null, null, 5000, null, null)]
        };

        var result = await CreateCalculator().PreviewAsync(definition, Now.AddMinutes(-90), Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(5000);
    }

    [Fact]
    public async Task PreviewAsync_SelectsCrossMidnightClockTimeBand()
    {
        var calculation = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero); // 02:00 in Asia/Manila.
        var definition = CreateDefinition() with
        {
            ParkingGracePeriodMinutes = 0,
            Rules =
            [
                new ContinuityTariffRule(Guid.NewGuid(), 1, "CLOCK_TIME", "SESSION", null, null, new TimeOnly(5, 0), new TimeOnly(1, 59), 5000, null, null),
                new ContinuityTariffRule(Guid.NewGuid(), 2, "CLOCK_TIME", "SESSION", null, null, new TimeOnly(1, 59), new TimeOnly(5, 0), 20000, null, null)
            ]
        };

        var result = await CreateCalculator().PreviewAsync(definition, calculation.AddMinutes(-30), calculation, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(20000);
        result.AppliedRuleIds.Should().Equal(definition.Rules[1].SiteTariffRuleId);
    }

    [Fact]
    public async Task PreviewAsync_AppliesOptionalDailyMaximum()
    {
        var definition = CreateDefinition() with { ParkingGracePeriodMinutes = 0, DailyMaxFeeMinorUnits = 12000 };

        var result = await CreateCalculator().PreviewAsync(definition, Now.AddHours(-4), Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AmountMinorUnits.Should().Be(12000);
    }

    private static ContinuityTariffCalculator CreateCalculator() =>
        new(new StubRepository(CreateDefinition()));

    private static ContinuityTariffDefinition CreateDefinition() =>
        new(
            DefinitionId,
            SiteId,
            "Asia/Manila",
            ContinuityVehicleTypes.Car,
            "PITX-L3-CAR",
            "PITX-L3-CAR-V1",
            "PHP",
            15,
            null,
            null,
            5,
            Now.AddDays(-1),
            null,
            [new ContinuityTariffRule(RuleId, 1, "DURATION", "UNIT_DURATION", 0, null, null, null, 5000, 60, "WHOLE_STARTED_HOUR")]);

    private sealed class StubRepository(ContinuityTariffDefinition definition) : IContinuityTariffRepository
    {
        public Task<ContinuityTariffDefinition?> FindActiveAsync(
            Guid siteId,
            string vehicleTypeCode,
            DateTimeOffset at,
            CancellationToken cancellationToken) =>
            Task.FromResult<ContinuityTariffDefinition?>(
                siteId == definition.SiteId && vehicleTypeCode == definition.VehicleTypeCode ? definition : null);
    }
}
