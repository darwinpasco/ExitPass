using ExitPass.GateIntegrationService.Application.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class GateAuthorizationConsumedHandoffHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid OriginalSupersededTariffSnapshotId = Guid.Parse("60000000-0000-0000-0000-000000000099");
    private static readonly Guid NoDiscountTariffSnapshotId = Guid.Parse("60000000-0000-0000-0000-000000000002");
    private static readonly Guid GateDeviceId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task HandleAsync_WhenPayloadIsValid_InvokesNoOpAdapterOnceAndRecordsSuccess()
    {
        var fixture = new Fixture();
        var handoff = CreateHandoff(AppliedTariffSnapshotId);

        var result = await fixture.Sut.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(handoff),
            CancellationToken.None);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_PROCESSED", result.ResultCode);
        Assert.True(result.AdapterInvoked);
        Assert.False(result.AlreadyProcessed);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal(ExitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal(GateAuthorizationConsumptionId, result.GateAuthorizationConsumptionId);
        Assert.Equal(AppliedTariffSnapshotId, result.TariffSnapshotId);
        Assert.Equal(1, fixture.Adapter.CallCount);

        var record = Assert.Single(fixture.Recorder.Records);
        Assert.Equal(EventId, record.EventId);
        Assert.Equal(AppliedTariffSnapshotId, record.TariffSnapshotId);
        var command = Assert.Single(fixture.CommandRecorder.Commands);
        Assert.Equal(GateCommandStatus.Succeeded, command.CommandStatus);
        Assert.Equal(AppliedTariffSnapshotId, command.TariffSnapshotId);
    }

    [Fact]
    public async Task HandleAsync_WhenAppliedEffectiveTariffPayloadIsProcessed_PreservesAppliedSnapshot()
    {
        var fixture = new Fixture();
        var handoff = CreateHandoff(AppliedTariffSnapshotId);

        var result = await fixture.Sut.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(handoff),
            CancellationToken.None);

        Assert.Equal(AppliedTariffSnapshotId, result.TariffSnapshotId);
        Assert.NotEqual(OriginalSupersededTariffSnapshotId, result.TariffSnapshotId);
        Assert.Equal(AppliedTariffSnapshotId, fixture.Adapter.LastHandoff?.TariffSnapshotId);
        Assert.Empty(fixture.TariffResolutionRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenNoDiscountPayloadIsProcessed_PreservesNoDiscountSnapshot()
    {
        var fixture = new Fixture();
        var handoff = CreateHandoff(NoDiscountTariffSnapshotId);

        var result = await fixture.Sut.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(handoff),
            CancellationToken.None);

        Assert.Equal(NoDiscountTariffSnapshotId, result.TariffSnapshotId);
        Assert.Equal(NoDiscountTariffSnapshotId, fixture.Adapter.LastHandoff?.TariffSnapshotId);
        Assert.Empty(fixture.TariffResolutionRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenSameEventIsProcessedTwice_IsIdempotent()
    {
        var fixture = new Fixture();
        var handoff = CreateHandoff(AppliedTariffSnapshotId);

        var first = await fixture.Sut.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(handoff),
            CancellationToken.None);
        var second = await fixture.Sut.HandleAsync(
            new ProcessGateAuthorizationConsumedCommand(handoff),
            CancellationToken.None);

        Assert.True(first.AdapterInvoked);
        Assert.False(second.AdapterInvoked);
        Assert.True(second.AlreadyProcessed);
        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED", second.ResultCode);
        Assert.Equal(1, fixture.Adapter.CallCount);
        Assert.Single(fixture.Recorder.Records);
        Assert.Single(fixture.CommandRecorder.Commands);
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFieldCases))]
    public async Task HandleAsync_WhenRequiredFieldIsMissing_RejectsWithoutAdapter(
        GateAuthorizationConsumedHandoff handoff,
        string expectedErrorCode)
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<GateAuthorizationConsumedHandoffException>(() =>
            fixture.Sut.HandleAsync(
                new ProcessGateAuthorizationConsumedCommand(handoff),
                CancellationToken.None));

        Assert.Equal(expectedErrorCode, ex.ErrorCode);
        Assert.Equal(0, fixture.Adapter.CallCount);
        Assert.Empty(fixture.Recorder.Records);
    }

    [Fact]
    public async Task HandleAsync_WhenScopeValidatorRejectsGateDevice_DoesNotInvokeAdapter()
    {
        var fixture = new Fixture
        {
            ScopeResult = GateAuthorizationConsumedScopeValidationResult.Invalid(
                "GATE_DEVICE_NOT_FOUND",
                "Gate device was not found.")
        };

        var ex = await Assert.ThrowsAsync<GateAuthorizationConsumedHandoffException>(() =>
            fixture.Sut.HandleAsync(
                new ProcessGateAuthorizationConsumedCommand(CreateHandoff(AppliedTariffSnapshotId)),
                CancellationToken.None));

        Assert.Equal("GATE_DEVICE_NOT_FOUND", ex.ErrorCode);
        Assert.Equal(0, fixture.Adapter.CallCount);
        var record = Assert.Single(fixture.Recorder.Records);
        Assert.Equal(GateAuthorizationConsumedProcessingStatus.Failed, record.ProcessingStatus);
        Assert.Equal("GATE_DEVICE_NOT_FOUND", record.LastFailureCode);
        Assert.Empty(fixture.CommandRecorder.Commands);
    }

    [Fact]
    public async Task HandleAsync_WhenScopeValidatorRejectsLaneOrSite_DoesNotInvokeAdapter()
    {
        var fixture = new Fixture
        {
            ScopeResult = GateAuthorizationConsumedScopeValidationResult.Invalid(
                "GATE_SCOPE_MISMATCH",
                "Gate device is not assigned to the handoff site or lane.")
        };

        var ex = await Assert.ThrowsAsync<GateAuthorizationConsumedHandoffException>(() =>
            fixture.Sut.HandleAsync(
                new ProcessGateAuthorizationConsumedCommand(CreateHandoff(AppliedTariffSnapshotId)),
                CancellationToken.None));

        Assert.Equal("GATE_SCOPE_MISMATCH", ex.ErrorCode);
        Assert.Equal(0, fixture.Adapter.CallCount);
        var record = Assert.Single(fixture.Recorder.Records);
        Assert.Equal(GateAuthorizationConsumedProcessingStatus.Failed, record.ProcessingStatus);
        Assert.Equal("GATE_SCOPE_MISMATCH", record.LastFailureCode);
        Assert.Empty(fixture.CommandRecorder.Commands);
    }

    [Fact]
    public async Task HandleAsync_WhenAdapterFails_RecordsFailureWithoutProcessedState()
    {
        var fixture = new Fixture
        {
            AdapterFailure = new InvalidOperationException("adapter failed")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.HandleAsync(
                new ProcessGateAuthorizationConsumedCommand(CreateHandoff(AppliedTariffSnapshotId)),
                CancellationToken.None));

        Assert.Equal("adapter failed", ex.Message);
        var record = Assert.Single(fixture.Recorder.Records);
        Assert.Equal(GateAuthorizationConsumedProcessingStatus.Failed, record.ProcessingStatus);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", record.LastFailureCode);
        Assert.Equal(1, fixture.Adapter.CallCount);
        var command = Assert.Single(fixture.CommandRecorder.Commands);
        Assert.Equal(GateCommandStatus.Retryable, command.CommandStatus);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", command.FailureCode);
    }

    public static IEnumerable<object[]> MissingRequiredFieldCases()
    {
        yield return
        [
            CreateHandoff(AppliedTariffSnapshotId) with { EventId = Guid.Empty, SourceEventRef = "" },
            "GATE_HANDOFF_EVENT_ID_REQUIRED"
        ];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { ExitAuthorizationId = Guid.Empty }, "GATE_HANDOFF_EXIT_AUTHORIZATION_ID_REQUIRED"];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { GateAuthorizationConsumptionId = Guid.Empty }, "GATE_HANDOFF_CONSUMPTION_ID_REQUIRED"];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { ParkingSessionId = Guid.Empty }, "GATE_HANDOFF_PARKING_SESSION_ID_REQUIRED"];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { PaymentAttemptId = Guid.Empty }, "GATE_HANDOFF_PAYMENT_ATTEMPT_ID_REQUIRED"];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { TariffSnapshotId = Guid.Empty }, "GATE_HANDOFF_TARIFF_SNAPSHOT_ID_REQUIRED"];
        yield return
        [
            CreateHandoff(AppliedTariffSnapshotId) with { GateDeviceId = null, GateDeviceIdentifier = "" },
            "GATE_HANDOFF_GATE_DEVICE_REQUIRED"
        ];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { ConsumedAtUtc = default }, "GATE_HANDOFF_CONSUMED_AT_REQUIRED"];
        yield return [CreateHandoff(AppliedTariffSnapshotId) with { CorrelationId = Guid.Empty }, "GATE_HANDOFF_CORRELATION_ID_REQUIRED"];
    }

    private static GateAuthorizationConsumedHandoff CreateHandoff(Guid tariffSnapshotId)
    {
        return new GateAuthorizationConsumedHandoff(
            EventId,
            SourceEventRef: $"central-pms://integration-events/{EventId}",
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            tariffSnapshotId,
            GateDeviceId,
            GateDeviceIdentifier: "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Adapter.CurrentFixture = this;
            Sut = new GateAuthorizationConsumedHandoffHandler(
                Adapter,
                CommandRecorder,
                Recorder,
                ScopeValidator);
        }

        public CapturingAdapter Adapter { get; } = new();

        public InMemoryRecorder Recorder { get; } = new();

        public InMemoryCommandRecorder CommandRecorder { get; } = new();

        public List<Guid> TariffResolutionRequests { get; } = new();

        public GateAuthorizationConsumedScopeValidationResult ScopeResult { get; set; } =
            GateAuthorizationConsumedScopeValidationResult.Valid();

        public Exception? AdapterFailure { get; set; }

        public GateAuthorizationConsumedHandoffHandler Sut { get; }

        private ScopeValidatorAdapter ScopeValidator => new(this);
    }

    private sealed class CapturingAdapter : IConsumedAuthorizationGateActionAdapter
    {
        public int CallCount { get; private set; }

        public GateAuthorizationConsumedHandoff? LastHandoff { get; private set; }

        public Task ProcessConsumedAuthorizationAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastHandoff = handoff;
            if (CurrentFixture?.AdapterFailure is { } failure)
            {
                throw failure;
            }

            return Task.CompletedTask;
        }

        public Fixture? CurrentFixture { get; set; }
    }

    private sealed class InMemoryRecorder : IGateAuthorizationConsumedProcessingRecorder
    {
        private readonly Dictionary<Guid, GateAuthorizationConsumedProcessingRecord> _records = new();

        public IReadOnlyCollection<GateAuthorizationConsumedProcessingRecord> Records => _records.Values;

        public Task<GateAuthorizationConsumedProcessingStart> BeginProcessingAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            var eventId = handoff.ProcessingKey();
            _records.TryGetValue(eventId, out var record);
            if (record is not null)
            {
                if (record.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Failed)
                {
                    var retry = record with
                    {
                        ProcessingStatus = GateAuthorizationConsumedProcessingStatus.Processing,
                        ResultCode = "GATE_AUTHORIZATION_CONSUMED_PROCESSING",
                        AttemptCount = record.AttemptCount + 1,
                        LastFailureCode = null,
                        LastFailureReason = null
                    };
                    _records[eventId] = retry;
                    return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
                        retry,
                        CanInvokeAdapter: true,
                        AlreadyProcessed: false,
                        AlreadyInProgress: false));
                }

                var alreadyProcessed = record.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Processed;
                return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
                    record,
                    CanInvokeAdapter: false,
                    AlreadyProcessed: alreadyProcessed,
                    AlreadyInProgress: !alreadyProcessed));
            }

            record = new GateAuthorizationConsumedProcessingRecord(
                eventId,
                handoff.ExitAuthorizationId,
                handoff.GateAuthorizationConsumptionId,
                handoff.TariffSnapshotId,
                "GATE_AUTHORIZATION_CONSUMED_PROCESSING",
                DateTimeOffset.UtcNow,
                GateAuthorizationConsumedProcessingStatus.Processing);
            _records[eventId] = record;
            return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
                record,
                CanInvokeAdapter: true,
                AlreadyProcessed: false,
                AlreadyInProgress: false));
        }

        public Task RecordProcessedAsync(
            GateAuthorizationConsumedProcessingRecord record,
            CancellationToken cancellationToken)
        {
            _records[record.ProcessingKey] = record;
            return Task.CompletedTask;
        }

        public Task RecordFailedAsync(
            GateAuthorizationConsumedHandoff handoff,
            string failureCode,
            string failureReason,
            CancellationToken cancellationToken)
        {
            var eventId = handoff.ProcessingKey();
            _records[eventId] = new GateAuthorizationConsumedProcessingRecord(
                eventId,
                handoff.ExitAuthorizationId,
                handoff.GateAuthorizationConsumptionId,
                handoff.TariffSnapshotId,
                failureCode,
                DateTimeOffset.UtcNow,
                GateAuthorizationConsumedProcessingStatus.Failed,
                LastFailureCode: failureCode,
                LastFailureReason: failureReason);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCommandRecorder : IGateCommandLifecycleRecorder
    {
        private readonly Dictionary<Guid, GateCommandLifecycleRecord> _commands = new();

        public IReadOnlyCollection<GateCommandLifecycleRecord> Commands => _commands.Values;

        public Task<GateCommandLifecycleStart> BeginCommandAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            var processingKey = handoff.ProcessingKey();
            if (_commands.TryGetValue(processingKey, out var existing))
            {
                if (existing.CommandStatus is GateCommandStatus.Failed or GateCommandStatus.Retryable)
                {
                    var retry = existing with
                    {
                        CommandStatus = GateCommandStatus.InProgress,
                        AttemptCount = existing.AttemptCount + 1,
                        StartedAtUtc = DateTimeOffset.UtcNow,
                        CompletedAtUtc = null,
                        FailureCode = null,
                        FailureReason = null
                    };
                    _commands[processingKey] = retry;
                    return Task.FromResult(new GateCommandLifecycleStart(retry, Created: false, CanInvokeAdapter: true));
                }

                return Task.FromResult(new GateCommandLifecycleStart(existing, Created: false, CanInvokeAdapter: false));
            }

            var now = DateTimeOffset.UtcNow;
            var command = new GateCommandLifecycleRecord(
                Guid.NewGuid(),
                processingKey,
                handoff.EventId,
                handoff.ExitAuthorizationId,
                handoff.GateAuthorizationConsumptionId,
                handoff.ParkingSessionId,
                handoff.PaymentAttemptId,
                handoff.TariffSnapshotId,
                handoff.GateDeviceId,
                handoff.GateDeviceIdentifier,
                handoff.LaneId,
                handoff.SiteId,
                handoff.VendorSystemId,
                GateCommandStatus.InProgress,
                AttemptCount: 1,
                RequestedAtUtc: now,
                StartedAtUtc: now,
                CompletedAtUtc: null,
                FailureCode: null,
                FailureReason: null,
                handoff.CorrelationId);
            _commands[processingKey] = command;
            return Task.FromResult(new GateCommandLifecycleStart(command, Created: true, CanInvokeAdapter: true));
        }

        public Task RecordSucceededAsync(
            Guid commandId,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            Update(commandId, command => command with
            {
                CommandStatus = GateCommandStatus.Succeeded,
                CompletedAtUtc = completedAtUtc,
                FailureCode = null,
                FailureReason = null
            });
            return Task.CompletedTask;
        }

        public Task RecordFailedAsync(
            Guid commandId,
            string failureCode,
            string failureReason,
            bool retryable,
            CancellationToken cancellationToken)
        {
            Update(commandId, command => command with
            {
                CommandStatus = retryable ? GateCommandStatus.Retryable : GateCommandStatus.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FailureCode = failureCode,
                FailureReason = failureReason
            });
            return Task.CompletedTask;
        }

        private void Update(Guid commandId, Func<GateCommandLifecycleRecord, GateCommandLifecycleRecord> update)
        {
            var match = _commands.Single(command => command.Value.CommandId == commandId);
            _commands[match.Key] = update(match.Value);
        }
    }

    private sealed class ScopeValidatorAdapter(Fixture fixture) : IGateAuthorizationConsumedScopeValidator
    {
        public Task<GateAuthorizationConsumedScopeValidationResult> ValidateAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(fixture.ScopeResult);
        }
    }
}

internal static class GateAuthorizationConsumedHandoffTestExtensions
{
    public static Guid ProcessingKey(this GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;
}

#pragma warning restore CS1591
