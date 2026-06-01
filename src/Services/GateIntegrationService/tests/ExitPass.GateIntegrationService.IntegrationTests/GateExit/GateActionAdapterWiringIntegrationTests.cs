using System.Net;
using System.Net.Http.Json;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using ExitPass.GateIntegrationService.Infrastructure.GateExit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.IntegrationTests.GateExit;

public sealed class GateActionAdapterWiringIntegrationTests
{
    private static readonly Guid SourceEventId = Guid.Parse("f1000000-0000-0000-0000-000000000001");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("f2000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("f3000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("f4000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("f5000000-0000-0000-0000-000000000001");
    private static readonly Guid TariffSnapshotId = Guid.Parse("f6000000-0000-0000-0000-000000000001");
    private static readonly Guid GateDeviceId = Guid.Parse("f7000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("f8000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("f9000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("fa000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("fb000000-0000-0000-0000-000000000001");

    [Fact]
    public void DefaultConfiguration_ResolvesNoOpAdapter()
    {
        using var factory = CreateFactory(mode: null);
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService<IConsumedAuthorizationGateActionAdapter>();

        Assert.IsType<NoOpConsumedAuthorizationGateActionAdapter>(adapter);
        Assert.Null(scope.ServiceProvider.GetService<IHikCentralGateActionAuditRecorder>());
    }

    [Fact]
    public void ExplicitNoOpMode_ResolvesNoOpAdapter()
    {
        using var factory = CreateFactory("NoOp");
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService<IConsumedAuthorizationGateActionAdapter>();

        Assert.IsType<NoOpConsumedAuthorizationGateActionAdapter>(adapter);
    }

    [Fact]
    public void HikCentralFakeMode_ResolvesFakeAdapterAndTransport()
    {
        using var factory = CreateFactory("HikCentralFake");
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService<IConsumedAuthorizationGateActionAdapter>();
        var transport = scope.ServiceProvider.GetRequiredService<IHikCentralGateActionTransport>();
        var options = scope.ServiceProvider.GetRequiredService<HikCentralGateActionOptions>();
        var auditRecorder = scope.ServiceProvider.GetRequiredService<IHikCentralGateActionAuditRecorder>();

        Assert.IsType<HikCentralConsumedAuthorizationGateActionAdapter>(adapter);
        Assert.IsType<FakeHikCentralGateActionTransport>(transport);
        Assert.IsType<PostgresHikCentralGateActionAuditRecorder>(auditRecorder);
        Assert.Equal("Fake", options.TransportMode);
        Assert.False(string.IsNullOrWhiteSpace(options.AppKey));
        Assert.False(string.IsNullOrWhiteSpace(options.AppSecret));
    }

    [Fact]
    public async Task HikCentralFakeMode_ProcessesValidHandoffThroughHandlerWithFakeTransport()
    {
        using var factory = CreateFactory("HikCentralFake", useInMemoryLifecycle: true);
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IGateAuthorizationConsumedHandoffHandler>();
        var transport = Assert.IsType<FakeHikCentralGateActionTransport>(
            scope.ServiceProvider.GetRequiredService<IHikCentralGateActionTransport>());
        var auditRecorder = Assert.IsType<InMemoryHikCentralGateActionAuditRecorder>(
            scope.ServiceProvider.GetRequiredService<IHikCentralGateActionAuditRecorder>());

        var result = await handler.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(CreateHandoff()),
            CancellationToken.None);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_PROCESSED", result.ResultCode);
        Assert.True(result.AdapterInvoked);
        Assert.Single(transport.Requests);
        Assert.Single(auditRecorder.Records);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, transport.Requests.Single().PathAndQuery);
    }

    [Fact]
    public void HikCentralLiveMode_IsRejectedAtStartup()
    {
        using var factory = CreateFactory("HikCentralLive");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("HIKCENTRAL_LIVE_TRANSPORT_DISABLED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HikCentralLiveMode_WithHardGateAndValidOptions_RegistersLiveTransport()
    {
        using var factory = CreateFactory(
            "HikCentralLive",
            liveEnabled: true,
            includeLiveOptions: true,
            useFakeLiveHttpClient: true);
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService<IConsumedAuthorizationGateActionAdapter>();
        var transport = scope.ServiceProvider.GetRequiredService<IHikCentralGateActionTransport>();
        var options = scope.ServiceProvider.GetRequiredService<HikCentralGateActionOptions>();
        var auditRecorder = scope.ServiceProvider.GetRequiredService<IHikCentralGateActionAuditRecorder>();

        Assert.IsType<HikCentralConsumedAuthorizationGateActionAdapter>(adapter);
        Assert.IsType<LiveHikCentralGateActionTransport>(transport);
        Assert.IsType<InMemoryHikCentralGateActionAuditRecorder>(auditRecorder);
        Assert.True(options.LiveTransportEnabled);
        Assert.Equal("Live", options.TransportMode);
    }

    [Fact]
    public void HikCentralLiveMode_WithHardGateAndMissingOptions_IsRejectedAtStartup()
    {
        using var factory = CreateFactory("HikCentralLive", liveEnabled: true);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("HIKCENTRAL_BASE_URL_INVALID", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HIKCENTRAL_APP_KEY_REQUIRED", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HIKCENTRAL_APP_SECRET_REQUIRED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMode_IsRejectedAtStartup()
    {
        using var factory = CreateFactory("DefinitelyNotAnAdapter");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Unsupported gate action adapter mode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HikCentralSandboxEndpoint_WhenDefaultDisabled_RejectsWithoutAudit()
    {
        using var factory = CreateFactory(mode: null);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/internal/hikcentral/sandbox/validate-gate-action",
            CreateSandboxRequest());

        var report = await response.Content.ReadFromJsonAsync<HikCentralSandboxValidationReport>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(report);
        Assert.False(report!.Executed);
        Assert.Equal("HIKCENTRAL_SANDBOX_VALIDATION_DISABLED", report.ResultCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HikCentralSandboxEndpoint_WhenExplicitlyEnabled_UsesLiveTransportAndWritesAudit()
    {
        using var factory = CreateFactory(
            "HikCentralLive",
            liveEnabled: true,
            sandboxEnabled: true,
            includeLiveOptions: true,
            useSuccessfulLiveHttpClient: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/internal/hikcentral/sandbox/validate-gate-action",
            CreateSandboxRequest());
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<InMemoryHikCentralGateActionAuditRecorder>();
        var handler = scope.ServiceProvider.GetRequiredService<SuccessfulHikCentralHttpMessageHandler>();
        var report = await response.Content.ReadFromJsonAsync<HikCentralSandboxValidationReport>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(report);
        Assert.True(report!.Executed);
        Assert.True(report.Succeeded);
        Assert.Equal("HIKCENTRAL_GATE_ACTION_SUCCEEDED", report.ResultCode);
        Assert.Equal(1, handler.SendCount);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, handler.PathAndQuery);
        var record = Assert.Single(audit.Records);
        Assert.Equal(report.AuditId, record.AuditId);
        Assert.Equal(report.CorrelationId, record.RequestCorrelationId);
        Assert.DoesNotContain("test-secret", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string? mode = null,
        bool useInMemoryLifecycle = false,
        bool liveEnabled = false,
        bool sandboxEnabled = false,
        bool includeLiveOptions = false,
        bool useFakeLiveHttpClient = false,
        bool useSuccessfulLiveHttpClient = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");
            if (mode is not null)
            {
                builder.UseSetting("GateActionAdapter:Mode", mode);
            }

            if (liveEnabled)
            {
                builder.UseSetting("GateIntegrations:HikCentral:LiveTransportEnabled", "true");
            }

            if (sandboxEnabled)
            {
                builder.UseSetting("GateIntegrations:HikCentral:SandboxValidationEnabled", "true");
            }

            if (includeLiveOptions)
            {
                builder.UseSetting("GateIntegrations:HikCentral:BaseUrl", "https://hikcentral.test");
                builder.UseSetting("GateIntegrations:HikCentral:AppKey", "test-ak");
                builder.UseSetting("GateIntegrations:HikCentral:AppSecret", "test-secret");
                builder.UseSetting("GateIntegrations:HikCentral:RequestTimeoutSeconds", "10");
            }

            if (useInMemoryLifecycle || useFakeLiveHttpClient || useSuccessfulLiveHttpClient)
            {
                builder.ConfigureServices(services =>
                {
                    if (useInMemoryLifecycle)
                    {
                        services.RemoveAll<IGateCommandLifecycleRecorder>();
                        services.RemoveAll<IGateAuthorizationConsumedProcessingRecorder>();
                        services.RemoveAll<IHikCentralGateActionAuditRecorder>();
                        services.AddSingleton<IGateCommandLifecycleRecorder, InMemoryGateCommandLifecycleRecorder>();
                        services.AddSingleton<IGateAuthorizationConsumedProcessingRecorder, InMemoryGateAuthorizationConsumedProcessingRecorder>();
                        services.AddSingleton<InMemoryHikCentralGateActionAuditRecorder>();
                        services.AddSingleton<IHikCentralGateActionAuditRecorder>(provider =>
                            provider.GetRequiredService<InMemoryHikCentralGateActionAuditRecorder>());
                    }

                    if (useFakeLiveHttpClient)
                    {
                        services.RemoveAll<HttpClient>();
                        services.RemoveAll<IHikCentralGateActionAuditRecorder>();
                        services.RemoveAll<IHikCentralSandboxValidationCommandRecorder>();
                        services.AddSingleton(new HttpClient(new NoNetworkHttpMessageHandler())
                        {
                            BaseAddress = new Uri("https://hikcentral.test"),
                            Timeout = Timeout.InfiniteTimeSpan
                        });
                        services.AddSingleton<InMemoryHikCentralGateActionAuditRecorder>();
                        services.AddSingleton<IHikCentralGateActionAuditRecorder>(provider =>
                            provider.GetRequiredService<InMemoryHikCentralGateActionAuditRecorder>());
                        services.AddSingleton<IHikCentralSandboxValidationCommandRecorder, InMemoryHikCentralSandboxValidationCommandRecorder>();
                    }

                    if (useSuccessfulLiveHttpClient)
                    {
                        services.RemoveAll<HttpClient>();
                        services.RemoveAll<IHikCentralGateActionAuditRecorder>();
                        services.RemoveAll<IHikCentralSandboxValidationCommandRecorder>();
                        services.AddSingleton<SuccessfulHikCentralHttpMessageHandler>();
                        services.AddSingleton(provider => new HttpClient(
                            provider.GetRequiredService<SuccessfulHikCentralHttpMessageHandler>())
                        {
                            BaseAddress = new Uri("https://hikcentral.test"),
                            Timeout = Timeout.InfiniteTimeSpan
                        });
                        services.AddSingleton<InMemoryHikCentralGateActionAuditRecorder>();
                        services.AddSingleton<IHikCentralGateActionAuditRecorder>(provider =>
                            provider.GetRequiredService<InMemoryHikCentralGateActionAuditRecorder>());
                        services.AddSingleton<IHikCentralSandboxValidationCommandRecorder, InMemoryHikCentralSandboxValidationCommandRecorder>();
                    }
                });
            }
        });

    private static HikCentralSandboxValidationRequest CreateSandboxRequest() =>
        new(
            "sandbox-door-01",
            HikCentralDoorControlType.Open,
            HikCentralDoorControlDirection.Exit,
            "Controlled integration test validation",
            "integration-test",
            CorrelationId,
            ConfirmLiveAction: true);

    private static GateAuthorizationConsumedHandoff CreateHandoff() =>
        new(
            SourceEventId,
            SourceEventRef: $"central-pms://integration-events/{SourceEventId}",
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            TariffSnapshotId,
            GateDeviceId,
            GateDeviceIdentifier: "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId);

    private sealed class NoNetworkHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The test live HikCentral HTTP client must not be invoked.");
        }
    }

    private sealed class SuccessfulHikCentralHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    code = "0",
                    msg = "Success",
                    data = new[]
                    {
                        new[]
                        {
                            new
                            {
                                doorIndexCode = "sandbox-door-01",
                                controlResultCode = 0,
                                controlResultDesc = "Success"
                            }
                        }
                    }
                })
            });
        }
    }
}

#pragma warning restore CS1591
