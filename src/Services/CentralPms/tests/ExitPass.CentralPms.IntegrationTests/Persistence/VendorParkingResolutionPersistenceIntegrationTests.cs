using System.Reflection;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class VendorParkingResolutionPersistenceIntegrationTests
{
    [Fact]
    public void AuthoritativeQuoteComparison_WhenLiveAmountChanges_ReturnsFalse()
    {
        var existing = ComparisonTariff(70m, DateTimeOffset.UtcNow.AddMinutes(-1));
        var incoming = ComparisonTariff(100m, DateTimeOffset.UtcNow);

        InvokeQuoteComparison(existing, incoming).Should().BeFalse();
    }

    [Fact]
    public void AuthoritativeQuoteComparison_WhenOnlyCalculationWindowChanges_ReturnsTrue()
    {
        var existing = ComparisonTariff(70m, DateTimeOffset.UtcNow.AddMinutes(-1));
        var incoming = ComparisonTariff(70m, DateTimeOffset.UtcNow);

        InvokeQuoteComparison(existing, incoming).Should().BeTrue();
    }

    [Fact]
    public async Task PersistContinuityResolution_WhenQuoteIsStillActive_ReusesSessionAndSnapshot()
    {
        var persistence = CreatePersistence();
        var fixture = CreateFixture("REUSE", DateTimeOffset.UtcNow.AddMinutes(10));

        var first = await persistence.PersistAsync(fixture.Request, CancellationToken.None);
        var replaySessionId = Guid.NewGuid();
        var replay = await persistence.PersistAsync(
            Request(
                fixture,
                replaySessionId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.ExitPassContinuity),
            CancellationToken.None);

        replay.ParkingSessionWasReused.Should().BeTrue();
        replay.TariffSnapshotWasReused.Should().BeTrue();
        replay.ParkingSession.ParkingSessionId.Should().Be(first.ParkingSession.ParkingSessionId);
        replay.TariffSnapshot.TariffSnapshotId.Should().Be(first.TariffSnapshot.TariffSnapshotId);
    }

    [Fact]
    public async Task PersistContinuityResolution_WhenPreviousQuoteExpired_CreatesFreshImmutableSnapshot()
    {
        var persistence = CreatePersistence();
        var fixture = CreateFixture("EXPIRED", DateTimeOffset.UtcNow.AddMinutes(-1));

        var first = await persistence.PersistAsync(fixture.Request, CancellationToken.None);
        var freshTariffId = Guid.NewGuid();
        var replay = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                freshTariffId,
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.ExitPassContinuity),
            CancellationToken.None);

        replay.ParkingSessionWasReused.Should().BeTrue();
        replay.TariffSnapshotWasReused.Should().BeFalse();
        replay.ParkingSession.ParkingSessionId.Should().Be(first.ParkingSession.ParkingSessionId);
        replay.TariffSnapshot.TariffSnapshotId.Should().NotBe(first.TariffSnapshot.TariffSnapshotId);
    }

    [Fact]
    public async Task PersistLiveResolution_WhenContinuityQuoteExists_ReplacesContinuityWithLiveTariff()
    {
        var persistence = CreatePersistence();
        var fixture = CreateFixture("LIVE-RECOVERY", DateTimeOffset.UtcNow.AddMinutes(10));

        var continuity = await persistence.PersistAsync(fixture.Request, CancellationToken.None);
        var liveTariffId = Guid.NewGuid();
        var live = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                liveTariffId,
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                tariffVersionReference: "HIKCENTRAL-LIVE-V1"),
            CancellationToken.None);

        live.ParkingSessionWasReused.Should().BeTrue();
        live.TariffSnapshotWasReused.Should().BeFalse();
        live.ParkingSession.ParkingSessionId.Should().Be(continuity.ParkingSession.ParkingSessionId);
        live.TariffSnapshot.TariffSnapshotId.Should().NotBe(continuity.TariffSnapshot.TariffSnapshotId);
        live.TariffSnapshot.TariffVersionReference.Should().Be("HIKCENTRAL-LIVE-V1");
    }

    [Fact]
    public async Task PersistLiveResolution_WhenActiveQuoteAmountChanges_CreatesFreshImmutableSnapshot()
    {
        var persistence = CreatePersistence();
        var fixture = CreateFixture("LIVE-AMOUNT-CHANGE", DateTimeOffset.UtcNow.AddMinutes(10));
        var first = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                tariffVersionReference: "HIKCENTRAL-LIVE-V1",
                amount: 70m),
            CancellationToken.None);

        var refreshed = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                tariffVersionReference: "HIKCENTRAL-LIVE-V1",
                amount: 100m),
            CancellationToken.None);

        refreshed.ParkingSessionWasReused.Should().BeTrue();
        refreshed.TariffSnapshotWasReused.Should().BeFalse();
        refreshed.TariffSnapshot.TariffSnapshotId.Should().NotBe(first.TariffSnapshot.TariffSnapshotId);
        refreshed.TariffSnapshot.GrossAmount.Should().Be(100m);
        refreshed.TariffSnapshot.NetPayable.Should().Be(100m);
    }

    [Fact]
    public async Task PersistLiveResolution_WhenActiveQuoteIsUnchanged_ReusesSnapshot()
    {
        var persistence = CreatePersistence();
        var fixture = CreateFixture("LIVE-UNCHANGED", DateTimeOffset.UtcNow.AddMinutes(10));
        var first = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                tariffVersionReference: "HIKCENTRAL-LIVE-V1",
                amount: 70m),
            CancellationToken.None);

        var replay = await persistence.PersistAsync(
            Request(
                fixture,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                tariffVersionReference: "HIKCENTRAL-LIVE-V1",
                amount: 70m),
            CancellationToken.None);

        replay.ParkingSessionWasReused.Should().BeTrue();
        replay.TariffSnapshotWasReused.Should().BeTrue();
        replay.TariffSnapshot.TariffSnapshotId.Should().Be(first.TariffSnapshot.TariffSnapshotId);
    }

    private static VendorParkingResolutionPersistence CreatePersistence() =>
        new(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());

    private static Fixture CreateFixture(string suffix, DateTimeOffset expiresAt)
    {
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var parkingSessionId = Guid.NewGuid();
        var fixture = new Fixture(
            siteGroupId,
            siteId,
            $"CONT-{suffix}-{Guid.NewGuid():N}",
            $"PLATE-{Guid.NewGuid():N}"[..16],
            $"VENDOR-{suffix}-{Guid.NewGuid():N}",
            $"EXITPASS-CONTINUITY:SYNTHETIC-{suffix}-V1");

        return fixture with
        {
            Request = Request(
                fixture,
                parkingSessionId,
                Guid.NewGuid(),
                expiresAt,
                VendorParkingTariffOrigin.ExitPassContinuity)
        };
    }

    private static PersistVendorParkingResolutionRequest Request(
        Fixture fixture,
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        DateTimeOffset expiresAt,
        VendorParkingTariffOrigin tariffOrigin,
        string? tariffVersionReference = null,
        decimal amount = 100m) =>
        new()
        {
            ParkingSession = Session(fixture, parkingSessionId),
            TariffSnapshot = Tariff(
                fixture,
                tariffSnapshotId,
                parkingSessionId,
                expiresAt,
                tariffVersionReference,
                amount),
            TariffOrigin = tariffOrigin,
            ReferencesAlreadyExist = false,
            CorrelationId = Guid.NewGuid()
        };

    private static ParkingSession Session(Fixture fixture, Guid parkingSessionId) =>
        ParkingSession.Rehydrate(
            parkingSessionId,
            fixture.SiteGroupId.ToString("D"),
            fixture.SiteId.ToString("D"),
            fixture.VendorSystemCode,
            fixture.VendorSessionReference,
            "TICKET",
            fixture.PlateNumber,
            fixture.TicketReference,
            DateTimeOffset.UtcNow.AddHours(-2),
            ParkingSessionStatus.PaymentRequired);

    private static TariffSnapshot Tariff(
        Fixture fixture,
        Guid tariffSnapshotId,
        Guid parkingSessionId,
        DateTimeOffset expiresAt,
        string? tariffVersionReference = null,
        decimal amount = 100m) =>
        TariffSnapshot.Rehydrate(
            tariffSnapshotId,
            parkingSessionId,
            TariffSnapshotSourceType.Base,
            amount,
            0m,
            0m,
            amount,
            "PHP",
            amount,
            tariffVersionReference ?? fixture.ContinuityTariffVersion,
            null,
            DateTimeOffset.UtcNow,
            expiresAt,
            TariffSnapshotStatus.Active,
            null,
            null);

    private static bool InvokeQuoteComparison(TariffSnapshot existing, TariffSnapshot incoming)
    {
        var method = typeof(VendorParkingResolutionPersistence).GetMethod(
            "HasSameAuthoritativeQuote",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Authoritative quote comparison method was not found.");
        return (bool)(method.Invoke(null, [existing, incoming])
            ?? throw new InvalidOperationException("Authoritative quote comparison returned no result."));
    }

    private static TariffSnapshot ComparisonTariff(decimal amount, DateTimeOffset calculatedAt) =>
        TariffSnapshot.Rehydrate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TariffSnapshotSourceType.Base,
            amount,
            0m,
            0m,
            amount,
            "PHP",
            amount,
            "HIKCENTRAL-LIVE-V1",
            null,
            calculatedAt,
            calculatedAt.AddMinutes(15),
            TariffSnapshotStatus.Active,
            null,
            null);

    private sealed record Fixture(
        Guid SiteGroupId,
        Guid SiteId,
        string TicketReference,
        string PlateNumber,
        string VendorSessionReference,
        string ContinuityTariffVersion)
    {
        public string VendorSystemCode { get; init; } = $"VENDOR-{Guid.NewGuid():N}";
        public PersistVendorParkingResolutionRequest Request { get; init; } = null!;
    }
}
