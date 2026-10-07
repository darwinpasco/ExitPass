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
        string? tariffVersionReference = null) =>
        new()
        {
            ParkingSession = Session(fixture, parkingSessionId),
            TariffSnapshot = Tariff(
                fixture,
                tariffSnapshotId,
                parkingSessionId,
                expiresAt,
                tariffVersionReference),
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
        string? tariffVersionReference = null) =>
        TariffSnapshot.Rehydrate(
            tariffSnapshotId,
            parkingSessionId,
            TariffSnapshotSourceType.Base,
            100m,
            0m,
            0m,
            100m,
            "PHP",
            100m,
            tariffVersionReference ?? fixture.ContinuityTariffVersion,
            null,
            DateTimeOffset.UtcNow,
            expiresAt,
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
