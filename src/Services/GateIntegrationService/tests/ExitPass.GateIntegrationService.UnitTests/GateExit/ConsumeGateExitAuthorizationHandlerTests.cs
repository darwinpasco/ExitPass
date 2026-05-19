using ExitPass.GateIntegrationService.Application.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

/// <summary>
/// Unit tests for gate exit authorization consume/open behavior.
/// </summary>
public sealed class ConsumeGateExitAuthorizationHandlerTests
{
    private static readonly Guid ExitAuthorizationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ServiceIdentityId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsReturnsConsumed_OpensGateOnce()
    {
        var fixture = new Fixture();
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.UtcNow));

        var result = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.GateOpened);
        Assert.Equal("GATE_OPENED", result.ResultCode);
        Assert.Equal(1, fixture.Hardware.OpenCount);
        Assert.Single(fixture.Recorder.Records);
        Assert.True(fixture.Recorder.Records[0].GateOpened);
    }

    [Theory]
    [InlineData(CentralPmsConsumeAuthorizationStatus.NotFound, "EXIT_AUTHORIZATION_NOT_FOUND")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.AlreadyConsumed, "EXIT_AUTHORIZATION_ALREADY_CONSUMED")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Expired, "EXIT_AUTHORIZATION_EXPIRED")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Unavailable, "CENTRAL_PMS_UNAVAILABLE")]
    public async Task ConsumeAuthorization_WhenCentralPmsRejects_DoesNotOpenGate(
        CentralPmsConsumeAuthorizationStatus status,
        string expectedResultCode)
    {
        var fixture = new Fixture();
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            status,
            ExitAuthorizationId,
            expectedResultCode));

        var result = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.GateOpened);
        Assert.Equal(expectedResultCode, result.ResultCode);
        Assert.Equal(0, fixture.Hardware.OpenCount);
        Assert.Single(fixture.Recorder.Records);
        Assert.False(fixture.Recorder.Records[0].GateOpened);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ConsumeAuthorization_WhenDeviceIdentityMissing_RejectsBeforeConsumeOrGateOpen(string gateDeviceId)
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Sut.ExecuteAsync(CreateCommand(gateDeviceId: gateDeviceId), CancellationToken.None));

        Assert.Equal(0, fixture.CentralPms.CallCount);
        Assert.Equal(0, fixture.Hardware.OpenCount);
        Assert.Empty(fixture.Recorder.Records);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenServiceIdentityMissing_RejectsBeforeConsumeOrGateOpen()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Sut.ExecuteAsync(CreateCommand(serviceIdentityId: Guid.Empty), CancellationToken.None));

        Assert.Equal(0, fixture.CentralPms.CallCount);
        Assert.Equal(0, fixture.Hardware.OpenCount);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenDuplicateRequest_ReplayDoesNotOpenGateTwice()
    {
        var fixture = new Fixture();
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.UtcNow));
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            CentralPmsConsumeAuthorizationStatus.AlreadyConsumed,
            ExitAuthorizationId,
            "EXIT_AUTHORIZATION_ALREADY_CONSUMED"));

        var first = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);
        var second = await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.True(first.GateOpened);
        Assert.False(second.GateOpened);
        Assert.Equal("EXIT_AUTHORIZATION_ALREADY_CONSUMED", second.ResultCode);
        Assert.Equal(1, fixture.Hardware.OpenCount);
        Assert.Equal(2, fixture.Recorder.Records.Count);
    }

    private static ConsumeGateExitAuthorizationCommand CreateCommand(
        string gateDeviceId = "exit-gate-01",
        Guid? serviceIdentityId = null)
    {
        return new ConsumeGateExitAuthorizationCommand(
            ExitAuthorizationId,
            gateDeviceId,
            serviceIdentityId ?? ServiceIdentityId,
            CorrelationId);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Sut = new ConsumeGateExitAuthorizationHandler(
                CentralPms,
                Hardware,
                Recorder);
        }

        public FakeCentralPmsClient CentralPms { get; } = new();

        public FakeGateHardwareController Hardware { get; } = new();

        public FakeRecorder Recorder { get; } = new();

        public ConsumeGateExitAuthorizationHandler Sut { get; }
    }

    private sealed class FakeCentralPmsClient : ICentralPmsExitAuthorizationClient
    {
        private readonly Queue<CentralPmsConsumeAuthorizationResult> _results = new();

        public int CallCount { get; private set; }

        public void Enqueue(CentralPmsConsumeAuthorizationResult result)
        {
            _results.Enqueue(result);
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

    private sealed class FakeGateHardwareController : IGateHardwareController
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

    private sealed class FakeRecorder : IGateExitAttemptRecorder
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
