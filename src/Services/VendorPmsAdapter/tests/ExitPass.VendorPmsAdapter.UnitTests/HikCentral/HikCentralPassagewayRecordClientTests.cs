using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.HikCentral;

/// <summary>
/// Unit tests for the read-only HikCentral passageway record projection source client.
/// </summary>
public sealed class HikCentralPassagewayRecordClientTests
{
    [Fact]
    public async Task GetPassagewayRecordsAsync_SendsSignedOfficialRequestShape()
    {
        var handler = new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "0", "msg": "Success", "data": { "total": 0, "list": [] } }
            """));
        var client = CreateClient(handler);
        var correlationId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await client.GetPassagewayRecordsAsync(
            new HikCentralPassagewayRecordRequest(
                "LOT-1",
                DateTimeOffset.Parse("2026-06-11T15:00:00+08:00"),
                DateTimeOffset.Parse("2026-06-11T18:00:00+08:00"),
                2,
                25,
                correlationId),
            CancellationToken.None);

        Assert.Equal(HikCentralPassagewayRecordClient.PassagewayRecordPath, handler.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Equal(correlationId.ToString(), handler.LastRequest?.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("exitpass-adapter", handler.LastRequest?.Headers.GetValues("userId").Single());
        Assert.Equal("test-ak", handler.LastRequest?.Headers.GetValues("X-Ca-Key").Single());
        Assert.NotEmpty(handler.LastRequest!.Headers.GetValues("X-Ca-Signature").Single());

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(2, body.RootElement.GetProperty("pageIndex").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("pageSize").GetInt32());
        var queryInfo = body.RootElement.GetProperty("queryInfo");
        Assert.Equal("LOT-1", queryInfo.GetProperty("parkingLotIndexCode").GetString());
        Assert.Equal("2026-06-11T15:00:00+08:00", queryInfo.GetProperty("beginTime").GetString());
        Assert.Equal("2026-06-11T18:00:00+08:00", queryInfo.GetProperty("endTime").GetString());
        Assert.Equal(-1, queryInfo.GetProperty("directionType").GetInt32());
        Assert.Equal(-1, queryInfo.GetProperty("allowResult").GetInt32());
        Assert.Equal("EnterTime", queryInfo.GetProperty("sortField").GetString());
        Assert.Equal(1, queryInfo.GetProperty("orderType").GetInt32());
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_MapsNestedRecordFields()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "total": 1,
                "list": [
                  {
                    "guid": "REC-1",
                    "parkingLotInfo": { "indexCode": "LOT-1", "name": "Main Lot" },
                    "passagewayInfo": { "indexCode": "PASS-1", "name": "Entry Gate" },
                    "laneInfo": { "indexCode": "LANE-1", "name": "Lane 1", "laneDirection": "ENTRY" },
                    "personInfo": { "cardNum": "3519351207107", "ownerName": "Test User", "ownerPhoneNum": "redacted" },
                    "carInfo": { "plateLicense": "ABC123" },
                    "imageUrl": "https://hikcentral.example/image.jpg",
                    "enterTime": "2026-06-17T11:19:12+08:00",
                    "allowType": "TEMP",
                    "allowResult": "ALLOW"
                  }
                ]
              }
            }
            """)));

        var page = await client.GetPassagewayRecordsAsync(
            new HikCentralPassagewayRecordRequest(
                "LOT-1",
                DateTimeOffset.Parse("2026-06-17T00:00:00+08:00"),
                DateTimeOffset.Parse("2026-06-18T00:00:00+08:00"),
                1,
                50,
                Guid.NewGuid()),
            CancellationToken.None);

        var record = Assert.Single(page.Records);
        Assert.Equal(HttpStatusCode.OK, page.HttpStatusCode);
        Assert.Equal("0", page.Code);
        Assert.Equal(1, page.Total);
        Assert.Equal("REC-1", record.Guid);
        Assert.Equal("LOT-1", record.ParkingLotInfo?.IndexCode);
        Assert.Equal("Main Lot", record.ParkingLotInfo?.Name);
        Assert.Equal("PASS-1", record.PassagewayInfo?.IndexCode);
        Assert.Equal("LANE-1", record.LaneInfo?.IndexCode);
        Assert.Equal("ENTRY", record.LaneInfo?.LaneDirection);
        Assert.Equal("3519351207107", record.PersonInfo?.CardNum);
        Assert.Equal("ABC123", record.CarInfo?.PlateLicense);
        Assert.Equal("2026-06-17T11:19:12+08:00", record.EnterTime);
        Assert.Null(record.ExitTime);
    }

    private static HikCentralPassagewayRecordClient CreateClient(FakeHikCentralHandler handler)
    {
        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"),
            () => DateTimeOffset.FromUnixTimeMilliseconds(1479968678000));
        return new HikCentralPassagewayRecordClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://hikcentral.fake")
            },
            signer);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHikCentralHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
