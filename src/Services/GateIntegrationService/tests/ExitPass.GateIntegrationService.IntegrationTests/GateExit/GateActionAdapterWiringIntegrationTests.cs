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

            Assert.IsType<HikCentralConsumedAuthorizationGateActionAdapter>(adapter);
            Assert.IsType<FakeHikCentralGateActionTransport>(transport);
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

            var result = await handler.HandleAsync(
                new ProcessGateAuthorizationConsumedCommand(CreateHandoff()),
                CancellationToken.None);

            Assert.Equal("GATE_AUTHORIZATION_CONSUMED_PROCESSED", result.ResultCode);
            Assert.True(result.AdapterInvoked);
            Assert.Single(transport.Requests);
            Assert.Equal(HikCentralRequestSigner.DoorControlPath, transport.Requests.Single().PathAndQuery);
    }

    [Fact]
    public void HikCentralLiveMode_IsRejectedAtStartup()
    {
        using var factory = CreateFactory("HikCentralLive");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("HikCentral live gate action adapter is not implemented", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMode_IsRejectedAtStartup()
    {
        using var factory = CreateFactory("DefinitelyNotAnAdapter");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Unsupported gate action adapter mode", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string? mode = null,
        bool useInMemoryLifecycle = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");
            if (mode is not null)
            {
                builder.UseSetting("GateActionAdapter:Mode", mode);
            }

            if (useInMemoryLifecycle)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IGateCommandLifecycleRecorder>();
                    services.RemoveAll<IGateAuthorizationConsumedProcessingRecorder>();
                    services.AddSingleton<IGateCommandLifecycleRecorder, InMemoryGateCommandLifecycleRecorder>();
                    services.AddSingleton<IGateAuthorizationConsumedProcessingRecorder, InMemoryGateAuthorizationConsumedProcessingRecorder>();
                });
            }
        });

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
}

#pragma warning restore CS1591
