using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Infrastructure.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.IntegrationTests.GateExit;

public sealed class InMemoryGateAuthorizationConsumedProcessingRecorderTests
{
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("d3000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task RecordProcessedAsync_WhenEventIdIsMissing_UsesConsumptionIdAsProcessingKey()
    {
        var recorder = new InMemoryGateAuthorizationConsumedProcessingRecorder();
        var handoff = CreateHandoff();
        var first = await recorder.BeginProcessingAsync(handoff, CancellationToken.None);

        await recorder.RecordProcessedAsync(
            new GateAuthorizationConsumedProcessingRecord(
                Guid.Empty,
                handoff.ExitAuthorizationId,
                handoff.GateAuthorizationConsumptionId,
                handoff.TariffSnapshotId,
                "GATE_AUTHORIZATION_CONSUMED_PROCESSED",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var second = await recorder.BeginProcessingAsync(handoff, CancellationToken.None);

        Assert.True(first.CanInvokeAdapter);
        Assert.True(second.AlreadyProcessed);
        Assert.False(second.CanInvokeAdapter);
        Assert.Equal(GateAuthorizationConsumptionId, second.Record.ProcessingKey);
        Assert.Single(recorder.Records);
    }

    private static GateAuthorizationConsumedHandoff CreateHandoff()
    {
        return new GateAuthorizationConsumedHandoff(
            EventId: Guid.Empty,
            SourceEventRef: "central-pms://integration-events/d3000000-0000-0000-0000-000000000001",
            ExitAuthorizationId: Guid.Parse("d2000000-0000-0000-0000-000000000001"),
            GateAuthorizationConsumptionId,
            ParkingSessionId: Guid.Parse("d4000000-0000-0000-0000-000000000001"),
            PaymentAttemptId: Guid.Parse("d5000000-0000-0000-0000-000000000001"),
            TariffSnapshotId: Guid.Parse("d6000000-0000-0000-0000-000000000001"),
            GateDeviceId: Guid.Parse("d7000000-0000-0000-0000-000000000001"),
            GateDeviceIdentifier: "exit-gate-01",
            LaneId: Guid.Parse("d8000000-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("d9000000-0000-0000-0000-000000000001"),
            VendorSystemId: Guid.Parse("da000000-0000-0000-0000-000000000001"),
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId: Guid.Parse("db000000-0000-0000-0000-000000000001"));
    }
}

#pragma warning restore CS1591
