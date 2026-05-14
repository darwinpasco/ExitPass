using System.Net;
using System.Net.Http.Json;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Contracts.GateExit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.ContractTests.GateExit;

/// <summary>
/// Contract tests for the Gate Integration Service gate authorization API.
/// </summary>
public sealed class GateExitAuthorizationContractTests
    : IClassFixture<GateIntegrationContractFactory>
{
    private static readonly Guid ExitAuthorizationId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid ServiceIdentityId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");

    private readonly GateIntegrationContractFactory _factory;
    private readonly HttpClient _client;

    public GateExitAuthorizationContractTests(GateIntegrationContractFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsReturnsConsumed_OpensGateOnce()
    {
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.UtcNow));

        using var response = await SendConsumeAsync();
        var body = await response.Content.ReadFromJsonAsync<ConsumeGateExitAuthorizationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.GateOpened);
        Assert.Equal("GATE_OPENED", body.ResultCode);
        Assert.Equal(1, _factory.Hardware.OpenCount);
        Assert.Single(_factory.Recorder.Records);
    }

    [Theory]
    [InlineData(CentralPmsConsumeAuthorizationStatus.NotFound, "EXIT_AUTHORIZATION_NOT_FOUND", HttpStatusCode.NotFound)]
    [InlineData(CentralPmsConsumeAuthorizationStatus.AlreadyConsumed, "EXIT_AUTHORIZATION_ALREADY_CONSUMED", HttpStatusCode.Conflict)]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Expired, "EXIT_AUTHORIZATION_EXPIRED", HttpStatusCode.Conflict)]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Unavailable, "CENTRAL_PMS_UNAVAILABLE", HttpStatusCode.ServiceUnavailable)]
    public async Task ConsumeAuthorization_WhenCentralPmsRejects_DoesNotOpenGate(
        CentralPmsConsumeAuthorizationStatus status,
        string expectedResultCode,
        HttpStatusCode expectedStatusCode)
    {
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            status,
            ExitAuthorizationId,
            expectedResultCode));

        using var response = await SendConsumeAsync();
        var body = await response.Content.ReadFromJsonAsync<ConsumeGateExitAuthorizationResponse>();

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body!.GateOpened);
        Assert.Equal(expectedResultCode, body.ResultCode);
        Assert.Equal(0, _factory.Hardware.OpenCount);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenDeviceIdentityMissing_ReturnsUnauthorizedWithoutConsumeOrGateOpen()
    {
        using var response = await SendConsumeAsync(includeDeviceIdentity: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.CentralPms.CallCount);
        Assert.Equal(0, _factory.Hardware.OpenCount);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenDuplicateRequest_ReplayDoesNotOpenGateTwice()
    {
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.UtcNow));
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            CentralPmsConsumeAuthorizationStatus.AlreadyConsumed,
            ExitAuthorizationId,
            "EXIT_AUTHORIZATION_ALREADY_CONSUMED"));

        using var first = await SendConsumeAsync();
        using var second = await SendConsumeAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, _factory.Hardware.OpenCount);
        Assert.Equal(2, _factory.Recorder.Records.Count);
    }

    private async Task<HttpResponseMessage> SendConsumeAsync(bool includeDeviceIdentity = true)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{ExitAuthorizationId}/consume");

        request.Headers.TryAddWithoutValidation("X-Correlation-Id", CorrelationId.ToString());
        request.Headers.TryAddWithoutValidation("X-Service-Identity-Id", ServiceIdentityId.ToString());

        if (includeDeviceIdentity)
        {
            request.Headers.TryAddWithoutValidation("X-Gate-Device-Id", "exit-gate-01");
        }

        return await _client.SendAsync(request);
    }
}

public sealed class GateIntegrationContractFactory : WebApplicationFactory<Program>
{
    public FakeCentralPmsClient CentralPms { get; } = new();

    public FakeGateHardwareController Hardware { get; } = new();

    public FakeRecorder Recorder { get; } = new();

    public void Reset()
    {
        CentralPms.Reset();
        Hardware.Reset();
        Recorder.Reset();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("ContractTest");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICentralPmsExitAuthorizationClient>();
            services.RemoveAll<IGateHardwareController>();
            services.RemoveAll<IGateExitAttemptRecorder>();

            services.AddSingleton<ICentralPmsExitAuthorizationClient>(CentralPms);
            services.AddSingleton<IGateHardwareController>(Hardware);
            services.AddSingleton<IGateExitAttemptRecorder>(Recorder);
        });
    }
}

public sealed class FakeCentralPmsClient : ICentralPmsExitAuthorizationClient
{
    private readonly Queue<CentralPmsConsumeAuthorizationResult> _results = new();

    public int CallCount { get; private set; }

    public void Enqueue(CentralPmsConsumeAuthorizationResult result)
    {
        _results.Enqueue(result);
    }

    public void Reset()
    {
        _results.Clear();
        CallCount = 0;
    }

    public Task<CentralPmsConsumeAuthorizationResult> ConsumeAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_results.Dequeue());
    }
}

public sealed class FakeGateHardwareController : IGateHardwareController
{
    public int OpenCount { get; private set; }

    public void Reset()
    {
        OpenCount = 0;
    }

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

public sealed class FakeRecorder : IGateExitAttemptRecorder
{
    public List<GateExitAttemptRecord> Records { get; } = new();

    public void Reset()
    {
        Records.Clear();
    }

    public Task RecordAsync(GateExitAttemptRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}
