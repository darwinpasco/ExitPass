using System.Reflection;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
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

    [Fact]
    public async Task PersistLiveResolution_WhenPlateOnlyProjectionMatchesEntry_ReusesCanonicalProjectedSession()
    {
        var persistence = CreatePersistence();
        var entryTime = DateTimeOffset.Parse("2026-10-20T19:19:57+08:00");
        var fixture = CreateFixture("PLATE-PROJECTION", DateTimeOffset.UtcNow.AddMinutes(10), plateOnly: true, entryTime: entryTime);
        var projectedReference = $"record-{Guid.NewGuid():N}";
        fixture = fixture with { VendorSessionReference = projectedReference };

        var canonical = await persistence.PersistAsync(
            Request(fixture, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);
        await InsertProjectionAsync(fixture, projectedReference, entryTime);

        var calculated = fixture with
        {
            VendorSessionReference = $"HIKCENTRAL:{fixture.PlateNumber}:{entryTime:yyyyMMddHHmmss}"
        };
        var replay = await persistence.PersistAsync(
            Request(
                calculated,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                referencesAlreadyExist: true),
            CancellationToken.None);

        replay.ParkingSessionWasReused.Should().BeTrue();
        replay.ParkingSession.ParkingSessionId.Should().Be(canonical.ParkingSession.ParkingSessionId);
        replay.ParkingSession.VendorSessionRef.Should().Be(projectedReference);
    }

    [Fact]
    public async Task PersistLiveResolution_WhenTicketedProjectionMatchesEntry_ReusesCanonicalProjectedSession()
    {
        var persistence = CreatePersistence();
        var entryTime = DateTimeOffset.Parse("2026-10-20T20:19:57+08:00");
        var fixture = CreateFixture("TICKET-PROJECTION", DateTimeOffset.UtcNow.AddMinutes(10), entryTime: entryTime);
        var projectedReference = $"record-{Guid.NewGuid():N}";
        fixture = fixture with { VendorSessionReference = projectedReference };

        var canonical = await persistence.PersistAsync(
            Request(fixture, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);
        await InsertProjectionAsync(fixture, projectedReference, entryTime);

        var calculated = fixture with
        {
            VendorSessionReference = $"HIKCENTRAL:{fixture.TicketReference}:{entryTime:yyyyMMddHHmmss}"
        };
        var replay = await persistence.PersistAsync(
            Request(
                calculated,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                referencesAlreadyExist: true),
            CancellationToken.None);

        replay.ParkingSessionWasReused.Should().BeTrue();
        replay.ParkingSession.ParkingSessionId.Should().Be(canonical.ParkingSession.ParkingSessionId);
    }

    [Fact]
    public async Task PersistLiveResolution_WhenProjectionIdentityIsAmbiguous_RejectsInsteadOfMatchingByPlate()
    {
        var persistence = CreatePersistence();
        var entryTime = DateTimeOffset.Parse("2026-10-20T21:19:57+08:00");
        var fixture = CreateFixture("AMBIGUOUS-PROJECTION", DateTimeOffset.UtcNow.AddMinutes(10), plateOnly: true, entryTime: entryTime);

        await persistence.PersistAsync(
            Request(fixture, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);
        await InsertProjectionAsync(fixture, $"record-{Guid.NewGuid():N}", entryTime);
        await InsertProjectionAsync(fixture, $"record-{Guid.NewGuid():N}", entryTime);

        var action = () => persistence.PersistAsync(
            Request(
                fixture with { VendorSessionReference = $"HIKCENTRAL:{fixture.PlateNumber}:{entryTime:yyyyMMddHHmmss}" },
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                referencesAlreadyExist: true),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<VendorParkingResolutionPersistenceException>();
        exception.Which.ErrorCode.Should().Be("VENDOR_PARKING_PROJECTION_IDENTITY_AMBIGUOUS");
    }

    [Fact]
    public async Task PersistLiveResolution_WhenPlateMatchesDifferentEntry_DoesNotReuseHistoricalSession()
    {
        var persistence = CreatePersistence();
        var currentEntry = DateTimeOffset.Parse("2026-10-20T22:19:57+08:00");
        var fixture = CreateFixture(
            "HISTORICAL-PLATE",
            DateTimeOffset.UtcNow.AddMinutes(10),
            plateOnly: true,
            entryTime: currentEntry);
        var historicalReference = $"record-{Guid.NewGuid():N}";
        var historical = fixture with
        {
            EntryTime = currentEntry.AddDays(-1),
            VendorSessionReference = historicalReference
        };

        var historicalResult = await persistence.PersistAsync(
            Request(historical, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);
        await InsertProjectionAsync(historical, historicalReference, historical.EntryTime);

        var syntheticReference = $"HIKCENTRAL:{fixture.PlateNumber}:{currentEntry:yyyyMMddHHmmss}";
        var current = await persistence.PersistAsync(
            Request(
                fixture with { VendorSessionReference = syntheticReference },
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor,
                referencesAlreadyExist: true),
            CancellationToken.None);

        current.ParkingSessionWasReused.Should().BeFalse();
        current.ParkingSession.ParkingSessionId.Should().NotBe(historicalResult.ParkingSession.ParkingSessionId);
        current.ParkingSession.VendorSessionRef.Should().Be(syntheticReference);
    }

    [Fact]
    public async Task PersistLiveResolution_WhenPlateAndEntryMatchDifferentSite_DoesNotReuseForeignSession()
    {
        var persistence = CreatePersistence();
        var entryTime = DateTimeOffset.Parse("2026-10-20T23:19:57+08:00");
        var local = CreateFixture(
            "LOCAL-SITE",
            DateTimeOffset.UtcNow.AddMinutes(10),
            plateOnly: true,
            entryTime: entryTime);
        var foreignReference = $"record-{Guid.NewGuid():N}";
        var foreign = CreateFixture(
            "FOREIGN-SITE",
            DateTimeOffset.UtcNow.AddMinutes(10),
            plateOnly: true,
            entryTime: entryTime) with
        {
            PlateNumber = local.PlateNumber,
            VendorSessionReference = foreignReference
        };

        var foreignResult = await persistence.PersistAsync(
            Request(foreign, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);
        await InsertProjectionAsync(foreign, foreignReference, entryTime);

        var syntheticReference = $"HIKCENTRAL:{local.PlateNumber}:{entryTime:yyyyMMddHHmmss}";
        var localResult = await persistence.PersistAsync(
            Request(
                local with { VendorSessionReference = syntheticReference },
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(10),
                VendorParkingTariffOrigin.LiveVendor),
            CancellationToken.None);

        localResult.ParkingSessionWasReused.Should().BeFalse();
        localResult.ParkingSession.ParkingSessionId.Should().NotBe(foreignResult.ParkingSession.ParkingSessionId);
        localResult.ParkingSession.VendorSessionRef.Should().Be(syntheticReference);
    }

    private static VendorParkingResolutionPersistence CreatePersistence() =>
        new(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());

    private static Fixture CreateFixture(
        string suffix,
        DateTimeOffset expiresAt,
        bool plateOnly = false,
        DateTimeOffset? entryTime = null)
    {
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var parkingSessionId = Guid.NewGuid();
        var fixture = new Fixture(
            siteGroupId,
            siteId,
            plateOnly ? null : $"CONT-{suffix}-{Guid.NewGuid():N}",
            $"PLATE-{Guid.NewGuid():N}"[..16],
            $"VENDOR-{suffix}-{Guid.NewGuid():N}",
            $"EXITPASS-CONTINUITY:SYNTHETIC-{suffix}-V1",
            entryTime ?? DateTimeOffset.UtcNow.AddHours(-2));

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
        decimal amount = 100m,
        bool referencesAlreadyExist = false) =>
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
            ReferencesAlreadyExist = referencesAlreadyExist,
            CorrelationId = Guid.NewGuid()
        };

    private static ParkingSession Session(Fixture fixture, Guid parkingSessionId) =>
        ParkingSession.Rehydrate(
            parkingSessionId,
            fixture.SiteGroupId.ToString("D"),
            fixture.SiteId.ToString("D"),
            fixture.VendorSystemCode,
            fixture.VendorSessionReference,
            fixture.TicketReference is null ? "PLATE" : "TICKET",
            fixture.PlateNumber,
            fixture.TicketReference,
            fixture.EntryTime,
            ParkingSessionStatus.PaymentRequired);

    private static async Task InsertProjectionAsync(
        Fixture fixture,
        string vendorRecordGuid,
        DateTimeOffset entryTime)
    {
        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO sessions.vendor_session_projections (
                vendor_session_projection_id, vendor_system_id, site_id, site_group_id,
                parking_lot_index_code, vendor_record_guid, card_num, plate_license,
                enter_time, source_api, source_payload_hash, source_event_at,
                stable_identity_type, stable_identity_key, first_seen_at, last_seen_at,
                last_refreshed_at, projection_status, correlation_id, created_by_service_identity_id)
            SELECT
                gen_random_uuid(), vendor.vendor_system_id, @site_id, @site_group_id,
                '1', @vendor_record_guid, @card_num, @plate_number,
                @entry_time, 'integration-test', repeat('a', 64), @entry_time,
                'VENDOR_RECORD_GUID', @stable_identity_key, now(), now(),
                now(), 'ACTIVE', gen_random_uuid(), '12000000-0000-0000-0000-000000000001'
            FROM integration.vendor_systems AS vendor
            WHERE vendor.vendor_code = @vendor_system_code
              AND vendor.environment_code = 'TEST';
            """,
            connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = fixture.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = fixture.SiteGroupId;
        command.Parameters.AddWithValue("vendor_record_guid", vendorRecordGuid);
        command.Parameters.Add("card_num", NpgsqlDbType.Text).Value = (object?)fixture.TicketReference ?? DBNull.Value;
        command.Parameters.AddWithValue("plate_number", fixture.PlateNumber);
        command.Parameters.Add("entry_time", NpgsqlDbType.TimestampTz).Value = entryTime.ToUniversalTime();
        command.Parameters.AddWithValue("stable_identity_key", $"stable-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("vendor_system_code", fixture.VendorSystemCode);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

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
        string? TicketReference,
        string PlateNumber,
        string VendorSessionReference,
        string ContinuityTariffVersion,
        DateTimeOffset EntryTime)
    {
        public string VendorSystemCode { get; init; } = $"VENDOR-{Guid.NewGuid():N}";
        public PersistVendorParkingResolutionRequest Request { get; init; } = null!;
    }
}
