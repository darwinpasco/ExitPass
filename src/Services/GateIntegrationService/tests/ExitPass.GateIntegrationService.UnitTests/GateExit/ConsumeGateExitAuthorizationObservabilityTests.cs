using System.Diagnostics;
using ExitPass.GateIntegrationService.Application.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

/// <summary>
/// Tests operational evidence emitted by the Gate Integration Service consume/open workflow.
/// </summary>
public sealed class ConsumeGateExitAuthorizationObservabilityTests
{
    private static readonly Guid ExitAuthorizationId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ServiceIdentityId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private const string GateDeviceId = "exit-gate-observability-01";

    [Fact]
    public async Task ConsumeAuthorization_WhenCentralPmsConsumed_EmitsGateOpenEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.GateIntegrationService.Application.GateExit");
        var fixture = new Fixture();
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Consumed(
            ExitAuthorizationId,
            "CONSUMED",
            DateTimeOffset.Parse("2026-05-14T08:00:00Z")));

        await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        var activity = Assert.Single(
            listener.StoppedActivities,
            x => HasTag(x, "gate_device_id", GateDeviceId) && HasTag(x, "result_code", "GATE_OPENED"));
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "exit_authorization_id", ExitAuthorizationId);
        AssertTag(activity, "gate_device_id", GateDeviceId);
        AssertTag(activity, "service_identity_id", ServiceIdentityId);
        AssertTag(activity, "central_pms_consume_result", "CONSUMED");
        AssertTag(activity, "result_code", "GATE_OPENED");
        AssertTag(activity, "gate_open_attempted", true);
        AssertTag(activity, "gate_open_succeeded", true);
    }

    [Theory]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Unavailable, "CENTRAL_PMS_UNAVAILABLE")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.AlreadyConsumed, "EXIT_AUTHORIZATION_ALREADY_CONSUMED")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.NotFound, "EXIT_AUTHORIZATION_NOT_FOUND")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Expired, "EXIT_AUTHORIZATION_EXPIRED")]
    [InlineData(CentralPmsConsumeAuthorizationStatus.Rejected, "EXIT_AUTHORIZATION_REJECTED")]
    public async Task ConsumeAuthorization_WhenCentralPmsRejects_EmitsFailClosedEvidence(
        CentralPmsConsumeAuthorizationStatus status,
        string expectedResultCode)
    {
        using var listener = new ActivityCapture("ExitPass.GateIntegrationService.Application.GateExit");
        var fixture = new Fixture();
        fixture.CentralPms.Enqueue(CentralPmsConsumeAuthorizationResult.Rejected(
            status,
            ExitAuthorizationId,
            expectedResultCode));

        await fixture.Sut.ExecuteAsync(CreateCommand(), CancellationToken.None);

        var activity = Assert.Single(
            listener.StoppedActivities,
            x => HasTag(x, "gate_device_id", GateDeviceId) && HasTag(x, "result_code", expectedResultCode));
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "exit_authorization_id", ExitAuthorizationId);
        AssertTag(activity, "gate_device_id", GateDeviceId);
        AssertTag(activity, "service_identity_id", ServiceIdentityId);
        AssertTag(activity, "central_pms_consume_result", status.ToString().ToUpperInvariant());
        AssertTag(activity, "result_code", expectedResultCode);
        AssertTag(activity, "gate_open_attempted", false);
        AssertTag(activity, "gate_open_succeeded", false);
        Assert.Equal(0, fixture.Hardware.OpenCount);
    }

    [Fact]
    public async Task ConsumeAuthorization_WhenDeviceIdentityMissing_EmitsInvalidIdentityEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.GateIntegrationService.Application.GateExit");
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Sut.ExecuteAsync(
                new ConsumeGateExitAuthorizationCommand(
                    ExitAuthorizationId,
                    string.Empty,
                    ServiceIdentityId,
                    CorrelationId),
                CancellationToken.None));

        var activity = Assert.Single(listener.StoppedActivities, x => HasTag(x, "result_code", "INVALID_GATE_CONSUME_REQUEST"));
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "exit_authorization_id", ExitAuthorizationId);
        AssertTag(activity, "service_identity_id", ServiceIdentityId);
        AssertTag(activity, "central_pms_consume_result", "NOT_CALLED");
        AssertTag(activity, "gate_open_attempted", false);
        AssertTag(activity, "gate_open_succeeded", false);
        Assert.Equal(0, fixture.Hardware.OpenCount);
    }

    private static ConsumeGateExitAuthorizationCommand CreateCommand()
    {
        return new ConsumeGateExitAuthorizationCommand(
            ExitAuthorizationId,
            GateDeviceId,
            ServiceIdentityId,
            CorrelationId);
    }

    private static void AssertTag(Activity activity, string key, object expected)
    {
        Assert.Equal(expected.ToString(), activity.TagObjects.Single(x => x.Key == key).Value?.ToString());
    }

    private static bool HasTag(Activity activity, string key, object expected)
    {
        return activity.TagObjects.Any(x => x.Key == key && x.Value?.ToString() == expected.ToString());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Sut = new ConsumeGateExitAuthorizationHandler(CentralPms, Hardware, Recorder);
        }

        public FakeCentralPmsClient CentralPms { get; } = new();

        public FakeGateHardwareController Hardware { get; } = new();

        public FakeRecorder Recorder { get; } = new();

        public ConsumeGateExitAuthorizationHandler Sut { get; }
    }

    private sealed class FakeCentralPmsClient : ICentralPmsExitAuthorizationClient
    {
        private readonly Queue<CentralPmsConsumeAuthorizationResult> _results = new();

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
        public Task RecordAsync(GateExitAttemptRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture(string sourceName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Capture
            };

            ActivitySource.AddActivityListener(_listener);
        }

        private readonly object _sync = new();

        private readonly List<Activity> _stoppedActivities = new();

        public IReadOnlyCollection<Activity> StoppedActivities
        {
            get
            {
                lock (_sync)
                {
                    return _stoppedActivities.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void Capture(Activity activity)
        {
            lock (_sync)
            {
                _stoppedActivities.Add(activity);
            }
        }
    }
}

#pragma warning restore CS1591
