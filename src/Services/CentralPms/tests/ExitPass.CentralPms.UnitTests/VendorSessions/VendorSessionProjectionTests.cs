using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorSessions;

/// <summary>
/// Tests for HikCentral vendor session projection normalization, upsert, lookup, and authority boundaries.
/// </summary>
public sealed class VendorSessionProjectionTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-06-20T06:00:00Z");

    [Fact]
    public void Normalize_WhenExitTimeAbsent_CreatesActiveTicketProjection()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActiveRecord(guid: "REC-1", cardNum: "3519351207107", plateLicense: null),
            vendorSystemId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            siteId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            siteGroupId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            correlationId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.NotNull(projection);
        Assert.Equal(VendorSessionProjectionStatus.Active, projection!.ProjectionStatus);
        Assert.Equal("3519351207107", projection.CardNum);
        Assert.Null(projection.PlateLicense);
        Assert.Equal("REC-1", projection.VendorRecordGuid);
        Assert.Equal("VENDOR_RECORD_GUID", projection.StableIdentityType);
        Assert.Contains("REC-1", projection.StableIdentityKey, StringComparison.Ordinal);
        Assert.Equal(HikCentralPassagewayProjectionNormalizer.SourceApi, projection.SourceApi);
        Assert.Matches("^[0-9a-f]{64}$", projection.SourcePayloadHash);
    }

    [Fact]
    public void Normalize_WhenExitTimePresent_CreatesExitedProjectionWithOptionalPlate()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActiveRecord(guid: "REC-2", cardNum: "CARD-2", plateLicense: "ABC123", exitTime: "2026-06-17T12:19:12+08:00"),
            vendorSystemId: null,
            siteId: null,
            siteGroupId: null,
            correlationId: Guid.NewGuid(),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.Equal(VendorSessionProjectionStatus.Exited, projection!.ProjectionStatus);
        Assert.Equal("ABC123", projection.PlateLicense);
        Assert.Equal(DateTimeOffset.Parse("2026-06-17T12:19:12+08:00"), projection.ExitTime);
    }

    [Fact]
    public void Normalize_ActualHikCentralShapeWithEmptyExitTime_CreatesActiveTicketProjection()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActualHikCentralRecord(),
            vendorSystemId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            siteId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            siteGroupId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            correlationId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.NotNull(projection);
        Assert.Equal(VendorSessionProjectionStatus.Active, projection!.ProjectionStatus);
        Assert.Equal("5BF30C478FE44C0D8432E549AF9FE0F7", projection.VendorRecordGuid);
        Assert.Equal("1", projection.ParkingLotIndexCode);
        Assert.Equal("TEST SITE", projection.ParkingLotName);
        Assert.Equal("1", projection.PassagewayIndexCode);
        Assert.Equal("ENTRANCE", projection.PassagewayName);
        Assert.Equal("2", projection.LaneIndexCode);
        Assert.Equal("ENTRANCE", projection.LaneName);
        Assert.Equal("1", projection.LaneDirection);
        Assert.Equal("3519278781100", projection.CardNum);
        Assert.Null(projection.PlateLicense);
        Assert.Equal(DateTimeOffset.Parse("2026-06-16T17:30:04+08:00"), projection.EnterTime);
        Assert.Null(projection.ExitTime);
        Assert.Null(projection.ImageUrl);
        Assert.Equal("1", projection.AllowType);
        Assert.Equal("1", projection.AllowResult);
        Assert.Equal("VENDOR_RECORD_GUID", projection.StableIdentityType);
        Assert.Equal("HIKCENTRAL|GUID|5BF30C478FE44C0D8432E549AF9FE0F7", projection.StableIdentityKey);
        Assert.DoesNotContain("UNKNOWN", projection.StableIdentityKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_ActualHikCentralShapeWithExitTime_CreatesExitedProjection()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActualHikCentralRecord(
                cardNum: "3519278781100",
                plateLicense: "ABC123",
                imageUrl: "https://hikcentral.example/image.jpg",
                exitTime: "2026-06-16T18:30:04+08:00"),
            vendorSystemId: null,
            siteId: null,
            siteGroupId: null,
            correlationId: Guid.NewGuid(),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.Equal(VendorSessionProjectionStatus.Exited, projection!.ProjectionStatus);
        Assert.Equal("3519278781100", projection.CardNum);
        Assert.Equal("ABC123", projection.PlateLicense);
        Assert.Equal("https://hikcentral.example/image.jpg", projection.ImageUrl);
        Assert.Equal(DateTimeOffset.Parse("2026-06-16T18:30:04+08:00"), projection.ExitTime);
        Assert.Equal("VENDOR_RECORD_GUID", projection.StableIdentityType);
    }

    [Fact]
    public void Normalize_ActualHikCentralPlateOnlyRecordWithGuid_CreatesProjectionWithoutCardLookup()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActualHikCentralRecord(cardNum: "", plateLicense: "ABC123"),
            vendorSystemId: null,
            siteId: null,
            siteGroupId: null,
            correlationId: Guid.NewGuid(),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.NotNull(projection);
        Assert.Null(projection!.CardNum);
        Assert.Equal("ABC123", projection.PlateLicense);
        Assert.Equal("VENDOR_RECORD_GUID", projection.StableIdentityType);
        Assert.Equal("HIKCENTRAL|GUID|5BF30C478FE44C0D8432E549AF9FE0F7", projection.StableIdentityKey);
    }

    [Fact]
    public void Normalize_WhenGuidMissing_UsesParkingLotCardEnterTimeStableIdentity()
    {
        var normalizer = new HikCentralPassagewayProjectionNormalizer();

        var normalized = normalizer.TryNormalize(
            ActiveRecord(guid: null, cardNum: "CARD-3", plateLicense: "ABC123"),
            vendorSystemId: null,
            siteId: null,
            siteGroupId: null,
            correlationId: Guid.NewGuid(),
            ObservedAt,
            out var projection);

        Assert.True(normalized);
        Assert.Equal("PARKING_LOT_CARD_ENTER_TIME", projection!.StableIdentityType);
        Assert.Contains("LOT-1", projection.StableIdentityKey, StringComparison.Ordinal);
        Assert.Contains("CARD-3", projection.StableIdentityKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncAsync_RepeatedVendorRecord_IsIdempotent()
    {
        var repository = new InMemoryVendorSessionProjectionRepository();
        var client = new FakePassagewayClient([
            ActiveRecord(guid: "REC-IDEMPOTENT", cardNum: "CARD-1", plateLicense: "ABC123")
        ]);
        var service = CreateSyncService(client, repository);
        var command = SyncCommand();

        var first = await service.SyncAsync(command, CancellationToken.None);
        var second = await service.SyncAsync(command, CancellationToken.None);

        Assert.Equal(1, first.RecordsProjected);
        Assert.Equal(1, second.RecordsProjected);
        Assert.Single(repository.Items);
        Assert.Equal(2, repository.UpsertCounts.Single().Value);
    }

    [Fact]
    public async Task SyncAsync_ActualLiveSampleRecord_IsProjectedNotSkipped()
    {
        var repository = new InMemoryVendorSessionProjectionRepository();
        var client = new FakePassagewayClient([
            ActualHikCentralRecord()
        ]);
        var service = CreateSyncService(client, repository);

        var result = await service.SyncAsync(SyncCommand(), CancellationToken.None);

        Assert.Equal(1, result.RecordsSeen);
        Assert.Equal(1, result.RecordsProjected);
        Assert.Equal(0, result.RecordsSkipped);
        var projection = Assert.Single(repository.Items);
        Assert.Equal("3519278781100", projection.CardNum);
        Assert.Equal(VendorSessionProjectionStatus.Active, projection.ProjectionStatus);
        Assert.Equal("VENDOR_RECORD_GUID", projection.StableIdentityType);
    }

    [Fact]
    public async Task SyncAsync_SameCardDifferentEnterTime_WhenGuidMissing_CreatesDistinctProjections()
    {
        var repository = new InMemoryVendorSessionProjectionRepository();
        var client = new FakePassagewayClient([
            ActiveRecord(guid: null, cardNum: "CARD-DUP", plateLicense: null, enterTime: "2026-06-17T11:19:12+08:00"),
            ActiveRecord(guid: null, cardNum: "CARD-DUP", plateLicense: null, enterTime: "2026-06-17T12:19:12+08:00")
        ]);
        var service = CreateSyncService(client, repository);

        await service.SyncAsync(SyncCommand(), CancellationToken.None);

        Assert.Equal(2, repository.Items.Count);
        Assert.All(repository.Items, item => Assert.Equal("CARD-DUP", item.CardNum));
        Assert.Equal(2, repository.Items.Select(item => item.EnterTime).Distinct().Count());
    }

    [Fact]
    public async Task LookupAsync_ByCardWithinParkingLotAndSite_ReturnsProjectionWithFreshnessMetadata()
    {
        var repository = new InMemoryVendorSessionProjectionRepository();
        var siteId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var siteGroupId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        var normalizer = new HikCentralPassagewayProjectionNormalizer();
        Assert.True(normalizer.TryNormalize(
            ActiveRecord(guid: "REC-LOOKUP", cardNum: "CARD-LOOKUP", plateLicense: "ABC123"),
            vendorSystemId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            siteId,
            siteGroupId,
            correlationId: Guid.NewGuid(),
            ObservedAt,
            out var projection));
        await repository.UpsertAsync(projection!, CancellationToken.None);
        var lookup = new VendorSessionProjectionLookupService(repository);

        var result = await lookup.LookupAsync(
            new VendorSessionProjectionLookupQuery(
                CardNum: "CARD-LOOKUP",
                PlateLicense: null,
                siteId,
                siteGroupId,
                ParkingLotIndexCode: "LOT-1",
                RequestedAt: ObservedAt.AddMinutes(5),
                CorrelationId: Guid.Parse("eeeeeeee-0000-0000-0000-000000000001")),
            CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.IsProjectionBased);
        Assert.False(result.IsAuthoritativeForParkingSession);
        Assert.False(result.IsAuthoritativeForTariff);
        Assert.False(result.IsAuthoritativeForPayment);
        Assert.Equal(TimeSpan.FromMinutes(5), result.FreshnessAge);
        Assert.Equal("CARD-LOOKUP", result.Projection?.CardNum);
    }

    [Fact]
    public async Task LookupAsync_WhenVendorUnavailableCanUseProjection_DoesNotReturnAuthoritativeFinality()
    {
        var repository = new InMemoryVendorSessionProjectionRepository();
        var normalizer = new HikCentralPassagewayProjectionNormalizer();
        Assert.True(normalizer.TryNormalize(
            ActiveRecord(guid: "REC-DEGRADED", cardNum: "CARD-DEGRADED", plateLicense: "ABC123"),
            null,
            null,
            null,
            Guid.NewGuid(),
            ObservedAt.AddMinutes(-30),
            out var projection));
        await repository.UpsertAsync(projection!, CancellationToken.None);
        var lookup = new VendorSessionProjectionLookupService(repository);

        var result = await lookup.LookupAsync(
            new VendorSessionProjectionLookupQuery(
                "CARD-DEGRADED",
                PlateLicense: null,
                SiteId: null,
                SiteGroupId: null,
                ParkingLotIndexCode: "LOT-1",
                RequestedAt: ObservedAt,
                CorrelationId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.IsProjectionBased);
        Assert.False(result.IsAuthoritativeForParkingSession);
        Assert.False(result.IsAuthoritativeForTariff);
        Assert.False(result.IsAuthoritativeForPayment);
    }

    [Fact]
    public void FullDdl_IncludesVendorSessionProjectionTableIndexesAndAuthorityComment()
    {
        var ddl = File.ReadAllText(FindRepoFile("ExitPass_Full_Database_Creation_DDL_v1.2.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS sessions.vendor_session_projections", ddl, StringComparison.Ordinal);
        Assert.Contains("uq_vendor_session_projections__stable_identity_key", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_vendor_session_projections__card_num", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_vendor_session_projections__parking_lot_card", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_vendor_session_projections__site_card", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_vendor_session_projections__active_open", ddl, StringComparison.Ordinal);
        Assert.Contains("not parking-session authority, tariff authority, payment finality, or exit authorization", ddl, StringComparison.OrdinalIgnoreCase);
    }

    private static HikCentralVendorSessionProjectionSyncService CreateSyncService(
        IHikCentralPassagewayRecordClient client,
        IVendorSessionProjectionRepository repository)
    {
        return new HikCentralVendorSessionProjectionSyncService(
            client,
            new HikCentralPassagewayProjectionNormalizer(),
            repository,
            new FixedClock(ObservedAt),
            NullLogger<HikCentralVendorSessionProjectionSyncService>.Instance);
    }

    private static SyncVendorSessionProjectionsCommand SyncCommand()
    {
        return new SyncVendorSessionProjectionsCommand(
            VendorSystemId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            SiteGroupId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            ParkingLotIndexCode: "LOT-1",
            BeginTime: DateTimeOffset.Parse("2026-06-17T00:00:00+08:00"),
            EndTime: DateTimeOffset.Parse("2026-06-18T00:00:00+08:00"),
            PageSize: 50,
            MaxPages: 1,
            CorrelationId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"));
    }

    private static HikCentralPassagewayRecord ActiveRecord(
        string? guid,
        string cardNum,
        string? plateLicense,
        string enterTime = "2026-06-17T11:19:12+08:00",
        string? exitTime = null)
    {
        return new HikCentralPassagewayRecord(
            guid,
            new HikCentralNamedIndex("LOT-1", "Main Lot"),
            new HikCentralNamedIndex("PASS-1", "Entry Passageway"),
            new HikCentralLaneInfo("LANE-1", "Lane 1", "ENTRY", null),
            new HikCentralPersonInfo(cardNum, "Test User", null),
            new HikCentralCarInfo(plateLicense),
            ImageUrl: null,
            enterTime,
            exitTime,
            AllowType: "TEMP",
            AllowResult: "ALLOW");
    }

    private static HikCentralPassagewayRecord ActualHikCentralRecord(
        string cardNum = "3519278781100",
        string plateLicense = "Unknown",
        string imageUrl = "",
        string exitTime = "")
    {
        const string jsonTemplate = """
            {
              "guid": "5BF30C478FE44C0D8432E549AF9FE0F7",
              "parkingLotInfo": {
                "parkingLotIndexCode": "1",
                "parkingLotName": "TEST SITE"
              },
              "passagewayInfo": {
                "passagewayIndexCode": "1",
                "passagewayName": "ENTRANCE"
              },
              "laneInfo": {
                "laneIndexCode": "2",
                "laneName": "ENTRANCE",
                "direction": 1
              },
              "personInfo": {
                "cardNum": "__CARD_NUM__",
                "ownerName": "",
                "ownerPhoneNum": ""
              },
              "carInfo": {
                "plateLicense": "__PLATE_LICENSE__",
                "carType": 0,
                "ImageUrl": "__IMAGE_URL__",
                "EnterTime": "2026-06-16T17:30:04+08:00",
                "ExitTime": "__EXIT_TIME__"
              },
              "allowType": 1,
              "allowResult": 1
            }
            """;

        var json = jsonTemplate
            .Replace("__CARD_NUM__", cardNum, StringComparison.Ordinal)
            .Replace("__PLATE_LICENSE__", plateLicense, StringComparison.Ordinal)
            .Replace("__IMAGE_URL__", imageUrl, StringComparison.Ordinal)
            .Replace("__EXIT_TIME__", exitTime, StringComparison.Ordinal);

        return JsonSerializer.Deserialize<HikCentralPassagewayRecord>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            })!;
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from {AppContext.BaseDirectory}.");
    }

    private sealed class FakePassagewayClient(IReadOnlyList<HikCentralPassagewayRecord> records)
        : IHikCentralPassagewayRecordClient
    {
        public Task<HikCentralPassagewayRecordPage> GetPassagewayRecordsAsync(
            HikCentralPassagewayRecordRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HikCentralPassagewayRecordPage(
                System.Net.HttpStatusCode.OK,
                Code: "0",
                Message: "Success",
                request.PageIndex,
                request.PageSize,
                Total: records.Count,
                records));
        }
    }

    private sealed class InMemoryVendorSessionProjectionRepository : IVendorSessionProjectionRepository
    {
        private readonly Dictionary<string, VendorSessionProjection> _items = new(StringComparer.Ordinal);

        public IReadOnlyList<VendorSessionProjection> Items => _items.Values.ToArray();

        public Dictionary<string, int> UpsertCounts { get; } = new(StringComparer.Ordinal);

        public Task<VendorSessionProjection> UpsertAsync(
            VendorSessionProjection projection,
            CancellationToken cancellationToken)
        {
            UpsertCounts[projection.StableIdentityKey] =
                UpsertCounts.TryGetValue(projection.StableIdentityKey, out var count) ? count + 1 : 1;

            if (_items.TryGetValue(projection.StableIdentityKey, out var existing))
            {
                projection = projection with
                {
                    VendorSessionProjectionId = existing.VendorSessionProjectionId,
                    FirstSeenAt = existing.FirstSeenAt < projection.FirstSeenAt ? existing.FirstSeenAt : projection.FirstSeenAt,
                    CreatedAt = existing.CreatedAt
                };
            }

            _items[projection.StableIdentityKey] = projection;
            return Task.FromResult(projection);
        }

        public Task<VendorSessionProjection?> FindLatestAsync(
            VendorSessionProjectionLookupQuery query,
            CancellationToken cancellationToken)
        {
            var projection = _items.Values
                .Where(item => query.SiteId is null || item.SiteId == query.SiteId)
                .Where(item => query.SiteGroupId is null || item.SiteGroupId == query.SiteGroupId)
                .Where(item => query.ParkingLotIndexCode is null || item.ParkingLotIndexCode == query.ParkingLotIndexCode)
                .Where(item => item.ProjectionStatus != VendorSessionProjectionStatus.Invalidated)
                .Where(item => !string.IsNullOrWhiteSpace(query.CardNum)
                    ? item.CardNum == query.CardNum
                    : item.PlateLicense == query.PlateLicense)
                .OrderBy(item => item.ProjectionStatus == VendorSessionProjectionStatus.Active ? 0 : 1)
                .ThenByDescending(item => item.LastRefreshedAt)
                .ThenByDescending(item => item.EnterTime)
                .FirstOrDefault();

            return Task.FromResult(projection);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
