using System.Net;
using ExitPass.VendorPmsAdapter.Application.Routing;
using ExitPass.VendorPmsAdapter.Contracts.Projection;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using ExitPass.VendorPmsAdapter.Infrastructure.Projection;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.Routing;

public sealed class SiteAdapterRoutingTests
{
    private static readonly SiteAdapterBinding Binding = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        Guid.Parse("50000000-0000-0000-0000-000000000001"), "1", "IST", true);

    [Fact]
    public void WrongSiteOrVendor_FailsBeforeProviderCall()
    {
        var guard = new SiteAdapterBindingGuard(Binding);
        var error = Assert.Throws<SiteAdapterBindingException>(() => guard.EnsureCompatible(
            new VendorAdapterRequestContext(Guid.NewGuid(), Binding.SiteGroupId, Binding.VendorSystemId,
                Binding.AdapterIdentityId)));
        Assert.Equal("SITE_ADAPTER_BINDING_MISMATCH", error.ErrorCode);
    }

    [Fact]
    public async Task PassagewaySync_KeepsCardAndSkipsUnknownPlateWithoutRejectingPage()
    {
        var client = new FakeClient([
            Record("R1", "CARD-1", "Unknown"),
            Record("R2", null, "PLATE-2"),
            Record(null, null, null)
        ]);
        var useCase = new HikCentralPassagewaySyncUseCase(client, Binding, new(Binding));
        var response = await useCase.ExecuteAsync(new(new(Binding.SiteId, Binding.SiteGroupId,
            Binding.VendorSystemId, Binding.AdapterIdentityId), "1", DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow, 100, 2, Guid.NewGuid()), default);
        Assert.True(response.Succeeded);
        Assert.Equal(3, response.RecordsSeen);
        Assert.Equal(2, response.RecordsAccepted);
        Assert.Equal(1, response.RecordsSkipped);
        Assert.Equal("CARD-1", response.Records[0].CardReference);
        Assert.Null(response.Records[0].PlateNumber);
        Assert.Equal("PLATE-2", response.Records[1].PlateNumber);
    }

    [Fact]
    public async Task CrossSiteSync_DoesNotCallHikCentral()
    {
        var client = new FakeClient([]);
        var useCase = new HikCentralPassagewaySyncUseCase(client, Binding, new(Binding));
        await Assert.ThrowsAsync<SiteAdapterBindingException>(() => useCase.ExecuteAsync(new(
            new(Guid.NewGuid(), Binding.SiteGroupId, Binding.VendorSystemId, Binding.AdapterIdentityId), "1",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, 100, 2, Guid.NewGuid()), default));
        Assert.Equal(0, client.CallCount);
    }

    private static HikCentralPassagewayRecord Record(string? guid, string? card, string? plate) => new(
        guid, new("1", "Lot", "1", "Lot"), new("P1", "Entry"),
        new("L1", "Lane", "ENTRY", "ENTRY"), new(card, null, null),
        new(plate, EnterTime: "2026-08-20T10:00:00+08:00"), null, null, null, "1", "1");

    private sealed class FakeClient(IReadOnlyList<HikCentralPassagewayRecord> records)
        : IHikCentralPassagewayRecordClient
    {
        public int CallCount { get; private set; }
        public Task<HikCentralPassagewayRecordPage> GetPassagewayRecordsAsync(
            HikCentralPassagewayRecordRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HikCentralPassagewayRecordPage(HttpStatusCode.OK, "0", "ok", 1,
                request.PageSize, records.Count, records));
        }
    }
}
