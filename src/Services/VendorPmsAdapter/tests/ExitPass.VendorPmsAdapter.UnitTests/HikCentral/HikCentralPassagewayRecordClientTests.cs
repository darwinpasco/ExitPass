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
    public async Task GetPassagewayRecordsAsync_UsesConfiguredHikCentralRequestTimeZone()
    {
        var handler = new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "0", "msg": "Success", "data": { "total": 0, "list": [] } }
            """));
        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"));
        var client = new HikCentralPassagewayRecordClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://hikcentral.fake") },
            signer,
            requestTimeZoneId: "Asia/Manila");

        await client.GetPassagewayRecordsAsync(
            new HikCentralPassagewayRecordRequest(
                "LOT-1",
                DateTimeOffset.Parse("2026-08-14T01:30:00Z"),
                DateTimeOffset.Parse("2026-08-14T02:30:00Z"),
                1,
                100,
                Guid.NewGuid()),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var queryInfo = body.RootElement.GetProperty("queryInfo");
        Assert.Equal("2026-08-14T09:30:00+08:00", queryInfo.GetProperty("beginTime").GetString());
        Assert.Equal("2026-08-14T10:30:00+08:00", queryInfo.GetProperty("endTime").GetString());
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_MapsControlCharacterEncodedTotal()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "0", "msg": "Success", "data": { "total": "\u000b", "list": [] } }
            """)));

        var page = await client.GetPassagewayRecordsAsync(Request(), CancellationToken.None);

        Assert.Equal(11, page.Total);
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

    [Fact]
    public async Task GetPassagewayRecordsAsync_MapsActualHikCentralPassagewayShape()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse("""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "total": 1,
                "list": [
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
                      "cardNum": "3519278781100",
                      "ownerName": "",
                      "ownerPhoneNum": ""
                    },
                    "carInfo": {
                      "plateLicense": "Unknown",
                      "carType": 0,
                      "ImageUrl": "",
                      "EnterTime": "2026-06-16T17:30:04+08:00",
                      "ExitTime": ""
                    },
                    "allowType": 1,
                    "allowResult": 1
                  }
                ]
              }
            }
            """)));

        var page = await client.GetPassagewayRecordsAsync(
            new HikCentralPassagewayRecordRequest(
                "1",
                DateTimeOffset.Parse("2026-06-16T00:00:00+08:00"),
                DateTimeOffset.Parse("2026-06-17T00:00:00+08:00"),
                1,
                50,
                Guid.NewGuid()),
            CancellationToken.None);

        var record = Assert.Single(page.Records);
        Assert.Equal("5BF30C478FE44C0D8432E549AF9FE0F7", record.Guid);
        Assert.Equal("1", record.ParkingLotInfo?.ParkingLotIndexCode);
        Assert.Equal("TEST SITE", record.ParkingLotInfo?.ParkingLotName);
        Assert.Equal("1", record.PassagewayInfo?.PassagewayIndexCode);
        Assert.Equal("ENTRANCE", record.PassagewayInfo?.PassagewayName);
        Assert.Equal("2", record.LaneInfo?.LaneIndexCode);
        Assert.Equal("ENTRANCE", record.LaneInfo?.LaneName);
        Assert.Equal("1", record.LaneInfo?.Direction);
        Assert.Equal("3519278781100", record.PersonInfo?.CardNum);
        Assert.Equal("Unknown", record.CarInfo?.PlateLicense);
        Assert.Equal("0", record.CarInfo?.CarType);
        Assert.Equal("", record.CarInfo?.ImageUrl);
        Assert.Equal("2026-06-16T17:30:04+08:00", record.CarInfo?.EnterTime);
        Assert.Equal("", record.CarInfo?.ExitTime);
        Assert.Equal("1", record.AllowType);
        Assert.Equal("1", record.AllowResult);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_GenuineZeroRows_ReturnsSuccessfulEmptyPage()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse(
            """{ "code": "0", "msg": "Success", "data": { "total": 0, "list": [] } }""")));

        var page = await client.GetPassagewayRecordsAsync(Request(), CancellationToken.None);

        Assert.Equal("0", page.Code);
        Assert.Equal(0, page.Total);
        Assert.Empty(page.Records);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_DeployedBlankTotalWithoutCollection_ReturnsSuccessfulEmptyPage()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse(
            """{ "code": "0", "msg": "Success", "data": { "total": "", "pageIndex": 1, "pageSize": 100 } }""")));

        var page = await client.GetPassagewayRecordsAsync(Request(), CancellationToken.None);

        Assert.Equal("0", page.Code);
        Assert.Equal(0, page.Total);
        Assert.Empty(page.Records);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_ZeroTotalWithoutCollection_IsTheOnlyCollectionOmissionAccepted()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse(
            """{ "code": "0", "msg": "Success", "data": { "total": 1, "pageIndex": 1, "pageSize": 100 } }""")));

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_MALFORMED_RESPONSE", error.Classification);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "HIKCENTRAL_ACCESS_DENIED", false)]
    [InlineData(HttpStatusCode.Forbidden, "HIKCENTRAL_ACCESS_DENIED", false)]
    [InlineData(HttpStatusCode.BadGateway, "HIKCENTRAL_HTTP_FAILURE", true)]
    public async Task GetPassagewayRecordsAsync_NonSuccessHttp_FailsSafely(
        HttpStatusCode statusCode,
        string classification,
        bool retryable)
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => new HttpResponseMessage(statusCode)));

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal(classification, error.Classification);
        Assert.Equal(retryable, error.Retryable);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{ \"code\": \"0\" }")]
    [InlineData("{ \"code\": \"0\", \"data\": {} }")]
    public async Task GetPassagewayRecordsAsync_MalformedOrIncompleteResponse_FailsClosed(string body)
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse(body)));

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_MALFORMED_RESPONSE", error.Classification);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_ApplicationError_FailsClosedWithoutRawMessage()
    {
        const string secretShapedMessage = "credential=test-secret";
        var client = CreateClient(new FakeHikCentralHandler(_ => JsonResponse(
            $$"""{ "code": "1001", "msg": "{{secretShapedMessage}}", "data": { "list": [] } }""")));

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_APPLICATION_FAILURE", error.Classification);
        Assert.False(error.Retryable);
        Assert.DoesNotContain(secretShapedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_TransportFailure_FailsSafely()
    {
        var client = CreateClient(new FakeHikCentralHandler(_ => throw new HttpRequestException("secret transport detail")));

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_TRANSPORT_FAILURE", error.Classification);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPassagewayRecordsAsync_Timeout_FailsSafely()
    {
        var signer = new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"));
        var client = new HikCentralPassagewayRecordClient(
            new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://hikcentral.fake") },
            signer);

        var error = await Assert.ThrowsAsync<HikCentralPassagewayException>(
            () => client.GetPassagewayRecordsAsync(Request(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_TIMEOUT", error.Classification);
        Assert.True(error.Retryable);
    }

    private static HikCentralPassagewayRecordRequest Request() =>
        new(
            "LOT-1",
            DateTimeOffset.Parse("2026-06-17T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-17T01:00:00Z"),
            1,
            50,
            Guid.NewGuid());

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

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("synthetic timeout"));
    }
}
