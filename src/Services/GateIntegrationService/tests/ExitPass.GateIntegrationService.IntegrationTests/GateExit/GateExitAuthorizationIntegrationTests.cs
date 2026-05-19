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

namespace ExitPass.GateIntegrationService.IntegrationTests.GateExit;

/// <summary>
/// Integration tests for the Gate Integration Service consume/open workflow using test host boundaries.
/// </summary>
public sealed class GateExitAuthorizationIntegrationTests
    : IClassFixture<GateIntegrationTestFactory>
{
    private static readonly Guid ExitAuthorizationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ServiceIdentityId = Guid.Parse("66666666-2222-3333-4444-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("77777777-2222-3333-4444-555555555555");

    private readonly GateIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public GateExitAuthorizationIntegrationTests(GateIntegrationTestFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsConsumeSucceeds_RecordsReportableOpenResult()
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
        Assert.Equal(1, _factory.Hardware.OpenCount);

        var record = Assert.Single(_factory.Recorder.Records);
        Assert.True(record.GateOpened);
        Assert.Equal("GATE_OPENED", record.ResultCode);
        Assert.Equal(ExitAuthorizationId, record.ExitAuthorizationId);
        Assert.Equal(ServiceIdentityId, record.ServiceIdentityId);
        Assert.Equal(CorrelationId, record.CorrelationId);
    }

    [Fact]
    public async Task ConsumeAuthorization_DoesNotOpenBarrierBeforeSuccessfulCentralPmsConsume()
    {
        _factory.CentralPms.BlockUntilReleased = true;
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.UtcNow));

        var pendingResponse = SendConsumeAsync();
        await _factory.CentralPms.ConsumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, _factory.Hardware.OpenCount);

        _factory.CentralPms.ReleaseConsume();
        using var response = await pendingResponse;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.Hardware.OpenCount);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsUnavailable_FailsClosedAndRecordsDeniedResult()
    {
        _factory.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            CentralPmsConsumeAuthorizationStatus.Unavailable,
            ExitAuthorizationId,
            "CENTRAL_PMS_UNAVAILABLE"));

        using var response = await SendConsumeAsync();
        var body = await response.Content.ReadFromJsonAsync<ConsumeGateExitAuthorizationResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body!.GateOpened);
        Assert.Equal(0, _factory.Hardware.OpenCount);

        var record = Assert.Single(_factory.Recorder.Records);
        Assert.False(record.GateOpened);
        Assert.Equal("CENTRAL_PMS_UNAVAILABLE", record.ResultCode);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenServiceIdentityInvalid_ReturnsUnauthorizedWithoutConsumeOrGateOpen()
    {
        using var request = CreateConsumeRequest();
        request.Headers.Remove("X-Service-Identity-Id");
        request.Headers.TryAddWithoutValidation("X-Service-Identity-Id", "not-a-guid");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.CentralPms.CallCount);
        Assert.Equal(0, _factory.Hardware.OpenCount);
    }

    private async Task<HttpResponseMessage> SendConsumeAsync()
    {
        using var request = CreateConsumeRequest();
        return await _client.SendAsync(request);
    }

    private static HttpRequestMessage CreateConsumeRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{ExitAuthorizationId}/consume");

        request.Headers.TryAddWithoutValidation("X-Correlation-Id", CorrelationId.ToString());
        request.Headers.TryAddWithoutValidation("X-Service-Identity-Id", ServiceIdentityId.ToString());
        request.Headers.TryAddWithoutValidation("X-Gate-Device-Id", "exit-gate-01");

        return request;
    }
}

public sealed class GateIntegrationTestFactory : WebApplicationFactory<Program>
{
    public BlockingCentralPmsClient CentralPms { get; } = new();

    public CapturingGateHardwareController Hardware { get; } = new();

    public CapturingRecorder Recorder { get; } = new();

    public void Reset()
    {
        CentralPms.Reset();
        Hardware.Reset();
        Recorder.Reset();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");
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

public sealed class BlockingCentralPmsClient : ICentralPmsExitAuthorizationClient
{
    private readonly Queue<CentralPmsConsumeAuthorizationResult> _results = new();
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool BlockUntilReleased { get; set; }

    public int CallCount { get; private set; }

    public TaskCompletionSource ConsumeStarted { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Enqueue(CentralPmsConsumeAuthorizationResult result)
    {
        _results.Enqueue(result);
    }

    public void ReleaseConsume()
    {
        _release.SetResult();
    }

    public void Reset()
    {
        _results.Clear();
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockUntilReleased = false;
        CallCount = 0;
    }

    public async Task<CentralPmsConsumeAuthorizationResult> ConsumeAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        CallCount++;
        ConsumeStarted.TrySetResult();

        if (BlockUntilReleased)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        return _results.Dequeue();
    }
}

public sealed class CapturingGateHardwareController : IGateHardwareController
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

public sealed class CapturingRecorder : IGateExitAttemptRecorder
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
