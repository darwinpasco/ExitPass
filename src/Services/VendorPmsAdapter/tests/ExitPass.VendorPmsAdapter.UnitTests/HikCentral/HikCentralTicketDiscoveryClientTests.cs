using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.HikCentral;

public sealed class HikCentralTicketDiscoveryClientTests
{
    private const string TicketNumber = "3518855073102";
    private const string SecondTicketNumber = "3518855085105";
    private const string ObservedHistoricalCardNum = "3518835144105";
    private const string ParkingLotIndexCode = "1";

    [Fact]
    public void HikCentralParkingTimeFormatter_FormatsWithoutFractionalSeconds()
    {
        var value = DateTimeOffset.Parse("2026-06-11T15:00:00.1234567+08:00");

        var formatted = HikCentralParkingTimeFormatter.Format(value);

        Assert.Equal("2026-06-11T15:00:00+08:00", formatted);
    }

    [Fact]
    public void DiagnosticEndpointCatalog_IncludesOnlyReadOnlyEndpointsForLiveDiagnostics()
    {
        Assert.All(
            HikCentralDiagnosticEndpointCatalog.LiveDiagnosticEndpoints,
            endpoint =>
            {
                Assert.True(endpoint.IsReadOnly);
                Assert.True(endpoint.SafeForLiveDiagnostics);
            });
    }

    [Fact]
    public void DiagnosticEndpointCatalog_ExcludesParkingFeeConfirmFromLiveDiagnostics()
    {
        Assert.DoesNotContain(
            HikCentralTicketDiscoveryClient.ParkingFeeConfirmPath,
            HikCentralDiagnosticEndpointCatalog.LiveDiagnosticEndpoints.Select(endpoint => endpoint.Endpoint),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains(
            HikCentralDiagnosticEndpointCatalog.ReferenceInventory,
            endpoint => endpoint.Endpoint == HikCentralTicketDiscoveryClient.ParkingFeeConfirmPath &&
                        !endpoint.IsReadOnly &&
                        !endpoint.SafeForLiveDiagnostics);
    }

    [Fact]
    public async Task DiscoverTicket_WhenCardNumSucceeds_ReturnsFeeDetails()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => JsonResponse("""
                {
                  "code": "0",
                  "msg": "Success",
                  "data": {
                    "plateLicense": "TEST123",
                    "parkingInTime": "2026-06-11T08:15:00+08:00",
                    "parkingDuration": "3600",
                    "fee": "80.00"
                  }
                }
                """),
            _ => JsonResponse("""{ "code": "0", "msg": "unused", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.True(result.CardNumAccepted);
        Assert.Equal("cardNum", result.DiscoveredIdentifierType);
        Assert.Equal(TicketNumber, result.DiscoveredIdentifierValue);
        Assert.Equal("80.00", result.Fee);
        Assert.Equal("2026-06-11T08:15:00+08:00", result.ParkingInTime);
        Assert.Equal("3600", result.ParkingDuration);
        Assert.Equal("TEST123", result.PlateLicense);
        Assert.Equal([HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath], handler.Paths);
    }

    [Fact]
    public async Task DiscoverTicket_WhenCardNumFailsAndPassagewayContainsTicket_ReturnsCandidate()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => JsonResponse($$"""
                {
                  "code": "0",
                  "msg": "Success",
                  "data": {
                    "list": [
                      {
                        "ticketNo": "{{TicketNumber}}",
                        "passagewayIndexCode": "PASS-1",
                        "laneIndexCode": "LANE-2",
                        "plateLicense": "ABC123"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse("""{ "code": "0", "msg": "unused", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.False(result.CardNumAccepted);
        Assert.Equal("ticketNo", result.DiscoveredIdentifierType);
        Assert.Equal(TicketNumber, result.DiscoveredIdentifierValue);
        Assert.Equal(HikCentralTicketDiscoveryClient.PassagewayRecordPath, result.EndpointSource);
        Assert.Equal("PASS-1", result.PassagewayIndexCode);
        Assert.Equal("LANE-2", result.LaneIndexCode);
        Assert.Equal("ABC123", result.PlateLicense);
        var passagewaySummary = Assert.Single(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.PassagewayRecordPath);
        Assert.Equal(1, passagewaySummary.ItemCount);
        Assert.True(passagewaySummary.TicketMatched);
        Assert.Equal(TicketNumber, passagewaySummary.MatchedTicketValue);
        Assert.Equal("ticketNo", passagewaySummary.MatchedTicketField);
        Assert.Empty(passagewaySummary.ObservedOtherLookupValues);
        Assert.Equal("returned records with matching current ticket identifier", passagewaySummary.Outcome);
        Assert.Contains(passagewaySummary.SanitizedRecordSamples, sample => sample.Contains($"ticketNo={TicketNumber}", StringComparison.Ordinal));
        Assert.Contains(passagewaySummary.SanitizedRecordSamples, sample => sample.Contains("plateLicense=ABC123", StringComparison.Ordinal));
        Assert.Contains(HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath, handler.Paths);
        Assert.DoesNotContain(HikCentralTicketDiscoveryClient.CrossRecordsPagePath, handler.Paths);
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.CrossRecordsPagePath &&
                       summary.Outcome == "skipped, missing HIKCENTRAL_TEST_CAMERA_INDEX_CODE");
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath &&
                       summary.Outcome == "skipped, missing HIKCENTRAL_TEST_FLOOR_INDEX_CODE");
    }

    [Fact]
    public async Task DiscoverTicket_WhenCardNumFailsAndParkingSpaceContainsTicket_ReturnsCandidate()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }"""),
            HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath => JsonResponse($$"""
                {
                  "code": "0",
                  "msg": "Success",
                  "data": [
                    {
                      "parkingSpaceSerial": "prefix-{{TicketNumber}}-suffix",
                      "parkingInTime": "2026-06-11T09:30:00+08:00",
                      "parkingDuration": "1200"
                    }
                  ]
                }
                """),
            _ => JsonResponse("""{ "code": "0", "msg": "unused", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.False(result.CardNumAccepted);
        Assert.Equal("parkingSpaceSerial", result.DiscoveredIdentifierType);
        Assert.Equal($"prefix-{TicketNumber}-suffix", result.DiscoveredIdentifierValue);
        Assert.Equal(HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath, result.EndpointSource);
        Assert.Equal("2026-06-11T09:30:00+08:00", result.ParkingInTime);
        Assert.Equal("1200", result.ParkingDuration);
        var parkingSpaceSummary = Assert.Single(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath);
        Assert.True(parkingSpaceSummary.TicketMatched);
        Assert.Equal($"prefix-{TicketNumber}-suffix", parkingSpaceSummary.MatchedTicketValue);
        Assert.Equal("parkingSpaceSerial", parkingSpaceSummary.MatchedTicketField);
        Assert.Equal("returned records with matching current ticket identifier", parkingSpaceSummary.Outcome);
        Assert.Contains(
            parkingSpaceSummary.SanitizedRecordSamples,
            sample => sample.Contains($"parkingSpaceSerial=prefix-{TicketNumber}-suffix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverTicket_WhenNoCandidateFound_ReturnsNoCandidateConclusion()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => JsonResponse("""
                {
                  "code": "0",
                  "msg": "Success",
                  "data": {
                    "list": [
                      {
                        "ticketNo": "different-ticket",
                        "plateLicense": "NOHIT1"
                      }
                    ]
                  }
                }
                """),
            HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }"""),
            _ => JsonResponse("""{ "code": "1", "msg": "unknown/internal request error", "data": null }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.False(result.CardNumAccepted);
        Assert.Null(result.DiscoveredIdentifierType);
        Assert.Null(result.DiscoveredIdentifierValue);
        Assert.Contains("no matching", result.Conclusion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(HikCentralTicketDiscoveryClient.PassagewayRecordPath, handler.Paths);
        Assert.Contains(HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath, handler.Paths);
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.PassagewayRecordPath &&
                       summary.ItemCount == 1 &&
                       summary.Outcome == "returned records with no matching ticket identifier" &&
                       !summary.TicketMatched &&
                       summary.SanitizedRecordSamples.Any(sample => sample.Contains("ticketNo=different-ticket", StringComparison.Ordinal)));
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath &&
                       summary.ItemCount == 0 &&
                       summary.Outcome == "returned empty");
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.CrossRecordsPagePath &&
                       summary.Outcome == "skipped, missing HIKCENTRAL_TEST_CAMERA_INDEX_CODE");
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath &&
                       summary.Outcome == "skipped, missing HIKCENTRAL_TEST_FLOOR_INDEX_CODE");
    }

    [Fact]
    public async Task DiscoverTicket_WhenControlLookupValueAppearsForCurrentTicket_DoesNotMarkTicketMatched()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => HistoricalCardNumRecord(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(
            Request(additionalLookupValues: [ObservedHistoricalCardNum]),
            CancellationToken.None);

        var summary = Assert.Single(
            result.EndpointSummaries,
            item => item.EndpointPath == HikCentralTicketDiscoveryClient.PassagewayRecordPath);
        Assert.False(summary.TicketMatched);
        Assert.Null(summary.MatchedTicketValue);
        Assert.Null(summary.MatchedTicketField);
        Assert.Contains(ObservedHistoricalCardNum, summary.ObservedOtherLookupValues);
        Assert.Equal("returned records, but only non-ticket lookup values observed", summary.Outcome);
        Assert.Null(result.DiscoveredIdentifierType);
        Assert.Null(result.DiscoveredIdentifierValue);
    }

    [Fact]
    public async Task DiscoverTicket_WhenControlLookupValueAppearsForSecondTicket_DoesNotMarkTicketMatched()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => HistoricalCardNumRecord(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(
            new HikCentralTicketDiscoveryRequest(
                SecondTicketNumber,
                ParkingLotIndexCode,
                DateTimeOffset.Parse("2026-06-11T00:00:00+08:00"),
                DateTimeOffset.Parse("2026-06-11T23:59:59+08:00"),
                AdditionalLookupValues: [ObservedHistoricalCardNum]),
            CancellationToken.None);

        var summary = Assert.Single(
            result.EndpointSummaries,
            item => item.EndpointPath == HikCentralTicketDiscoveryClient.PassagewayRecordPath);
        Assert.False(summary.TicketMatched);
        Assert.Null(summary.MatchedTicketValue);
        Assert.Null(summary.MatchedTicketField);
        Assert.Contains(ObservedHistoricalCardNum, summary.ObservedOtherLookupValues);
        Assert.Equal("returned records, but only non-ticket lookup values observed", summary.Outcome);
        Assert.Null(result.DiscoveredIdentifierType);
        Assert.Null(result.DiscoveredIdentifierValue);
    }

    [Fact]
    public async Task DiscoverTicket_WhenCardNumFails_SendsDocumentedParkingRecordRequestBodies()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        await client.DiscoverTicketAsync(
            new HikCentralTicketDiscoveryRequest(
                TicketNumber,
                ParkingLotIndexCode,
                DateTimeOffset.Parse("2026-06-11T15:00:00.1234567+08:00"),
                DateTimeOffset.Parse("2026-06-11T18:00:00.7654321+08:00")),
            CancellationToken.None);

        AssertParkingRecordBody(handler.SingleBody(HikCentralTicketDiscoveryClient.PassagewayRecordPath));
        AssertParkingRecordBody(handler.SingleBody(HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath));
    }

    [Fact]
    public async Task DiscoverTicket_WhenCameraIndexMissing_SkipsCrossRecordsPage()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain(HikCentralTicketDiscoveryClient.CrossRecordsPagePath, handler.Paths);
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.CrossRecordsPagePath &&
                       summary.HttpStatusCode == 0 &&
                       summary.HikCentralMessage == "skipped, missing HIKCENTRAL_TEST_CAMERA_INDEX_CODE");
    }

    [Fact]
    public async Task DiscoverTicket_WhenFloorIndexMissing_SkipsFloorParkingSpaceStatus()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain(HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath, handler.Paths);
        Assert.Contains(
            result.EndpointSummaries,
            summary => summary.EndpointPath == HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath &&
                       summary.HttpStatusCode == 0 &&
                       summary.HikCentralMessage == "skipped, missing HIKCENTRAL_TEST_FLOOR_INDEX_CODE");
    }

    [Fact]
    public async Task DiscoverTicket_WhenFloorIndexPresent_SendsFloorParkingSpaceStatusBody()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        await client.DiscoverTicketAsync(
            new HikCentralTicketDiscoveryRequest(
                TicketNumber,
                ParkingLotIndexCode,
                DateTimeOffset.Parse("2026-06-11T15:00:00.1234567+08:00"),
                DateTimeOffset.Parse("2026-06-11T18:00:00.7654321+08:00"),
                FloorIndexCode: "FLOOR-1"),
            CancellationToken.None);

        var body = ParseBody(handler.SingleBody(HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath));
        Assert.Equal("FLOOR-1", body.RootElement.GetProperty("floorIndexCode").GetString());
    }

    [Fact]
    public async Task DiscoverTicket_WhenCameraIndexPresent_SendsCrossRecordsPageBody()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        await client.DiscoverTicketAsync(
            new HikCentralTicketDiscoveryRequest(
                TicketNumber,
                ParkingLotIndexCode,
                DateTimeOffset.Parse("2026-06-11T15:00:00.1234567+08:00"),
                DateTimeOffset.Parse("2026-06-11T18:00:00.7654321+08:00"),
                "CAM-1"),
            CancellationToken.None);

        var body = ParseBody(handler.SingleBody(HikCentralTicketDiscoveryClient.CrossRecordsPagePath));
        Assert.Equal("CAM-1", body.RootElement.GetProperty("cameraIndexCode").GetString());
        Assert.Equal("2026-06-11T15:00:00+08:00", body.RootElement.GetProperty("startTime").GetString());
        Assert.Equal("2026-06-11T18:00:00+08:00", body.RootElement.GetProperty("endTime").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("pageNo").GetInt32());
        Assert.Equal(50, body.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task DiscoverTicket_SanitizedSamplesIncludeNestedTicketCardAndSessionLikeFields()
    {
        var handler = new FakeHikCentralHandler(path => path switch
        {
            HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath => NotFoundCalculate(),
            HikCentralTicketDiscoveryClient.PassagewayRecordPath => JsonResponse("""
                {
                  "code": "0",
                  "msg": "Success",
                  "data": {
                    "list": [
                      {
                        "carInfo": {
                          "plateNo": "ABC123",
                          "vehicleColor": "blue"
                        },
                        "personInfo": {
                          "cardNum": "3518835144105",
                          "personName": "Local Test"
                        },
                        "parkingLotInfo": {
                          "parkingLotName": "TEST SITE"
                        },
                        "passagewayInfo": {
                          "passagewayIndexCode": "PASS-1"
                        },
                        "laneInfo": {
                          "laneIndexCode": "LANE-1"
                        },
                        "guid": "GUID-1",
                        "occurTime": "2026-06-11T15:00:00+08:00",
                        "feeOrderNo": "ORDER-1"
                      }
                    ]
                  }
                }
                """),
            _ => JsonResponse("""{ "code": "0", "msg": "Success", "data": [] }""")
        });
        var client = CreateClient(handler);

        var result = await client.DiscoverTicketAsync(
            Request(additionalLookupValues: [ObservedHistoricalCardNum]),
            CancellationToken.None);

        var summary = Assert.Single(
            result.EndpointSummaries,
            item => item.EndpointPath == HikCentralTicketDiscoveryClient.PassagewayRecordPath);
        var sample = Assert.Single(summary.SanitizedRecordSamples);
        Assert.Contains("carInfo.plateNo=ABC123", sample, StringComparison.Ordinal);
        Assert.Contains("personInfo.cardNum=3518835144105", sample, StringComparison.Ordinal);
        Assert.Contains("parkingLotInfo.parkingLotName=TEST SITE", sample, StringComparison.Ordinal);
        Assert.Contains("passagewayInfo.passagewayIndexCode=PASS-1", sample, StringComparison.Ordinal);
        Assert.Contains("laneInfo.laneIndexCode=LANE-1", sample, StringComparison.Ordinal);
        Assert.Contains("guid=GUID-1", sample, StringComparison.Ordinal);
        Assert.Contains("occurTime=2026-06-11T15:00:00+08:00", sample, StringComparison.Ordinal);
        Assert.Contains("feeOrderNo=ORDER-1", sample, StringComparison.Ordinal);
        Assert.False(summary.TicketMatched);
        Assert.Contains(ObservedHistoricalCardNum, summary.ObservedOtherLookupValues);
        Assert.Equal("returned records, but only non-ticket lookup values observed", summary.Outcome);
    }

    [Fact]
    public async Task DiscoverTicket_NeverCallsParkingFeeConfirm()
    {
        var handler = new FakeHikCentralHandler(_ => JsonResponse("""
            { "code": "128", "msg": "The request resource does not exist. [vehicle is not exist]", "data": [] }
            """));
        var client = CreateClient(handler);

        await client.DiscoverTicketAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain(
            "/artemis/api/vehicle/v1/parkingfee/confirm",
            handler.Paths,
            StringComparer.OrdinalIgnoreCase);
    }

    private static HikCentralTicketDiscoveryRequest Request(IReadOnlyList<string>? additionalLookupValues = null) =>
        new(
            TicketNumber,
            ParkingLotIndexCode,
            DateTimeOffset.Parse("2026-06-11T00:00:00+08:00"),
            DateTimeOffset.Parse("2026-06-11T23:59:59+08:00"),
            AdditionalLookupValues: additionalLookupValues);

    private static void AssertParkingRecordBody(string json)
    {
        using var body = ParseBody(json);
        Assert.Equal(1, body.RootElement.GetProperty("pageIndex").GetInt32());
        Assert.Equal(50, body.RootElement.GetProperty("pageSize").GetInt32());

        var queryInfo = body.RootElement.GetProperty("queryInfo");
        Assert.Equal(ParkingLotIndexCode, queryInfo.GetProperty("parkingLotIndexCode").GetString());
        Assert.Equal("2026-06-11T15:00:00+08:00", queryInfo.GetProperty("beginTime").GetString());
        Assert.Equal("2026-06-11T18:00:00+08:00", queryInfo.GetProperty("endTime").GetString());
        Assert.False(body.RootElement.TryGetProperty("pageNo", out _));
    }

    private static JsonDocument ParseBody(string json) =>
        JsonDocument.Parse(json);

    private static HikCentralTicketDiscoveryClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("http://127.0.0.1:9019")
            },
            new HikCentralRequestSigner(
                new HikCentralCredentialOptions("test-ak", "test-secret"),
                () => DateTimeOffset.FromUnixTimeMilliseconds(1479968678000)));

    private static HttpResponseMessage NotFoundCalculate() =>
        JsonResponse("""{ "code": "128", "msg": "The request resource does not exist. [vehicle is not exist]", "data": null }""");

    private static HttpResponseMessage HistoricalCardNumRecord() =>
        JsonResponse($$"""
            {
              "code": "0",
              "msg": "Success",
              "data": {
                "list": [
                  {
                    "personInfo": {
                      "cardNum": "{{ObservedHistoricalCardNum}}"
                    }
                  }
                ]
              }
            }
            """);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHikCentralHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _responseFactory;

        public FakeHikCentralHandler(Func<string, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<string> Paths { get; } = [];

        public Dictionary<string, List<string>> RequestBodiesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string SingleBody(string path) =>
            Assert.Single(RequestBodiesByPath[path]);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? request.RequestUri?.OriginalString ?? string.Empty;
            Paths.Add(path);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (!RequestBodiesByPath.TryGetValue(path, out var bodies))
            {
                bodies = [];
                RequestBodiesByPath[path] = bodies;
            }

            bodies.Add(body);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.True(request.Headers.Contains("X-Ca-Key"));
            Assert.True(request.Headers.Contains("X-Ca-Timestamp"));
            Assert.True(request.Headers.Contains("X-Ca-Signature"));
            Assert.False(request.Content?.Headers.Contains("Content-MD5"));
            return _responseFactory(path);
        }
    }
}
