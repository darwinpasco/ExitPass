using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Infrastructure.CentralPms;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.IntegrationTests.GateExit;

/// <summary>
/// Boundary tests for Gate Integration Service calls to the Central PMS consume contract.
/// </summary>
public sealed class GateCentralPmsBoundaryIntegrationTests
{
    private static readonly Guid ExitAuthorizationId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid ServiceIdentityId = Guid.Parse("33333333-3333-4444-5555-666666666666");
    private static readonly Guid CorrelationId = Guid.Parse("44444444-3333-4444-5555-666666666666");
    private static readonly DateTimeOffset ConsumedAt = DateTimeOffset.Parse("2026-05-14T08:00:00Z");

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsReturnsConsumed_SendsCorrectRequestAndOpensGateOnce()
    {
        var fixture = new Fixture();
        fixture.CentralPms.EnqueueJson(
            HttpStatusCode.OK,
            $$"""
            {
              "exitAuthorizationId": "{{ExitAuthorizationId}}",
              "authorizationStatus": "CONSUMED",
              "consumedAt": "{{ConsumedAt:O}}"
            }
            """);

        var result = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.GateOpened);
        Assert.Equal("GATE_OPENED", result.ResultCode);
        Assert.Equal(1, fixture.Hardware.OpenCount);

        var request = Assert.Single(fixture.CentralPms.Requests);
        AssertCentralPmsConsumeRequest(request);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "EXIT_AUTHORIZATION_NOT_FOUND")]
    [InlineData(HttpStatusCode.Conflict, "EXIT_AUTHORIZATION_EXPIRED")]
    [InlineData(HttpStatusCode.Conflict, "EXIT_AUTHORIZATION_ALREADY_CONSUMED")]
    [InlineData(HttpStatusCode.Conflict, "EXIT_AUTHORIZATION_CONSUME_REJECTED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "CENTRAL_PMS_UNAVAILABLE")]
    public async Task ConsumeAuthorization_WhenCentralPmsRejects_SendsCorrectRequestAndDoesNotOpenGate(
        HttpStatusCode statusCode,
        string errorCode)
    {
        var fixture = new Fixture();
        fixture.CentralPms.EnqueueJson(
            statusCode,
            $$"""
            {
              "error_code": "{{errorCode}}",
              "message": "Central PMS rejected consume.",
              "correlationId": "{{CorrelationId}}",
              "retryable": false
            }
            """);

        var result = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.GateOpened);
        Assert.Equal(0, fixture.Hardware.OpenCount);

        var request = Assert.Single(fixture.CentralPms.Requests);
        AssertCentralPmsConsumeRequest(request);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsUnavailable_DoesNotOpenGate()
    {
        var fixture = new Fixture();
        fixture.CentralPms.ThrowOnSend = true;

        var result = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.GateOpened);
        Assert.Equal("CENTRAL_PMS_UNAVAILABLE", result.ResultCode);
        Assert.Equal(0, fixture.Hardware.OpenCount);

        var request = Assert.Single(fixture.CentralPms.Requests);
        AssertCentralPmsConsumeRequest(request);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenDuplicateRequest_DoesNotOpenGateTwice()
    {
        var fixture = new Fixture();
        fixture.CentralPms.EnqueueJson(
            HttpStatusCode.OK,
            $$"""
            {
              "exitAuthorizationId": "{{ExitAuthorizationId}}",
              "authorizationStatus": "CONSUMED",
              "consumedAt": "{{ConsumedAt:O}}"
            }
            """);
        fixture.CentralPms.EnqueueJson(
            HttpStatusCode.Conflict,
            $$"""
            {
              "error_code": "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
              "message": "Exit authorization has already been consumed.",
              "correlationId": "{{CorrelationId}}",
              "retryable": false
            }
            """);

        var first = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);
        var second = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.True(first.GateOpened);
        Assert.False(second.GateOpened);
        Assert.Equal("EXIT_AUTHORIZATION_ALREADY_CONSUMED", second.ResultCode);
        Assert.Equal(1, fixture.Hardware.OpenCount);
        Assert.Equal(2, fixture.CentralPms.Requests.Count);
        Assert.All(fixture.CentralPms.Requests, AssertCentralPmsConsumeRequest);
    }

    private static ConsumeGateExitAuthorizationCommand CreateCommand()
    {
        return new ConsumeGateExitAuthorizationCommand(
            ExitAuthorizationId,
            "exit-gate-boundary-01",
            ServiceIdentityId,
            CorrelationId);
    }

    private static void AssertCentralPmsConsumeRequest(CapturedCentralPmsRequest request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/v1/gate/authorizations/{ExitAuthorizationId}/consume", request.Path);
        Assert.Equal(CorrelationId.ToString(), Assert.Single(request.CorrelationHeader));

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;

        Assert.Equal(ServiceIdentityId, root.GetProperty("requestedByUserId").GetGuid());
        Assert.False(root.TryGetProperty("paymentStatus", out _));
        Assert.False(root.TryGetProperty("paymentFinality", out _));
        Assert.False(root.TryGetProperty("exitAuthorizationStatus", out _));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var httpClient = new HttpClient(CentralPms)
            {
                BaseAddress = new Uri("http://central-pms.test")
            };

            Sut = new ConsumeGateExitAuthorizationHandler(
                new HttpCentralPmsExitAuthorizationClient(httpClient),
                Hardware,
                Recorder);
        }

        public CapturingCentralPmsHandler CentralPms { get; } = new();

        public CapturingBoundaryGateHardwareController Hardware { get; } = new();

        public CapturingBoundaryRecorder Recorder { get; } = new();

        public ConsumeGateExitAuthorizationHandler Sut { get; }
    }

    private sealed class CapturingCentralPmsHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<CapturedCentralPmsRequest> Requests { get; } = new();

        public bool ThrowOnSend { get; set; }

        public void EnqueueJson(HttpStatusCode statusCode, string json)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedCentralPmsRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-Correlation-Id", out var values)
                    ? values.ToArray()
                    : Array.Empty<string>(),
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (ThrowOnSend)
            {
                throw new HttpRequestException("Central PMS unavailable.");
            }

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedCentralPmsRequest(
        HttpMethod Method,
        string Path,
        string[] CorrelationHeader,
        string Body);

    private sealed class CapturingBoundaryGateHardwareController : IGateHardwareController
    {
        public int OpenCount { get; private set; }

        public Task OpenBarrierAsync(
            string gateDeviceId,
            Guid exitAuthorizationId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingBoundaryRecorder : IGateExitAttemptRecorder
    {
        public List<GateExitAttemptRecord> Records { get; } = new();

        public Task RecordAsync(GateExitAttemptRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}

#pragma warning restore CS1591
