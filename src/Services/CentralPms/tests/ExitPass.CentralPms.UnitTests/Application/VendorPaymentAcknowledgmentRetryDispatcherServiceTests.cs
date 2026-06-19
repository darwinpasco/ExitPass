using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for bounded Vendor PMS acknowledgment retry dispatch.
/// </summary>
public sealed class VendorPaymentAcknowledgmentRetryDispatcherServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-19T01:00:00Z");
    private static readonly Guid AcknowledgmentId1 = Guid.Parse("279c0000-0000-0000-0000-000000000001");
    private static readonly Guid AcknowledgmentId2 = Guid.Parse("279c0000-0000-0000-0000-000000000002");

    [Fact]
    public async Task DispatchDueAsync_WhenNoDueRetryPendingRows_DoesNothing()
    {
        var repository = new FakeAcknowledgmentRepository();
        var workflow = new FakeAcknowledgmentWorkflow();
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        result.DueCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        workflow.ProcessCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenCurrentRecordHasFutureNextRetryAt_SkipsRecord()
    {
        var record = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending, nextRetryAt: Now.AddMinutes(1));
        var repository = new FakeAcknowledgmentRepository(record);
        repository.DueRecords.Add(record);
        var workflow = new FakeAcknowledgmentWorkflow();
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        result.SkippedCount.Should().Be(1);
        result.Items.Single().FinalStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.RetryPending);
        workflow.ProcessCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenDueRetrySucceeds_RecordBecomesConfirmed()
    {
        var record = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var repository = new FakeAcknowledgmentRepository(record);
        repository.DueRecords.Add(record);
        var workflow = new FakeAcknowledgmentWorkflow
        {
            OnProcess = command => repository.Records[AcknowledgmentId1] =
                repository.Records[AcknowledgmentId1] with
                {
                    AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.Confirmed,
                    VendorCode = "0",
                    VendorMessage = "Vendor PMS confirmed paid-state acknowledgment.",
                    ConfirmedFeeMinorUnits = 5000,
                    VendorConfirmedAt = Now,
                    AttemptCount = 2,
                    LastAttemptedAt = Now,
                    NextRetryAt = null
                }
        };
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        workflow.ProcessCalls.Should().Be(1);
        result.ConfirmedCount.Should().Be(1);
        repository.Records[AcknowledgmentId1].AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.Confirmed);
        repository.Records[AcknowledgmentId1].VendorCode.Should().Be("0");
        repository.Records[AcknowledgmentId1].ConfirmedFeeMinorUnits.Should().Be(5000);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenDueRetryFails_RecordRemainsRetryPending()
    {
        var record = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var repository = new FakeAcknowledgmentRepository(record);
        repository.DueRecords.Add(record);
        var workflow = new FakeAcknowledgmentWorkflow
        {
            OnProcess = command => repository.Records[AcknowledgmentId1] =
                repository.Records[AcknowledgmentId1] with
                {
                    AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.RetryPending,
                    VendorCode = "VENDOR_UNAVAILABLE",
                    VendorMessage = "Vendor PMS confirmation returned UnavailableRetryable.",
                    AttemptCount = 2,
                    LastAttemptedAt = Now,
                    NextRetryAt = Now.AddMinutes(5)
                }
        };
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        workflow.ProcessCalls.Should().Be(1);
        result.FailedCount.Should().Be(1);
        repository.Records[AcknowledgmentId1].AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.RetryPending);
        repository.Records[AcknowledgmentId1].VendorCode.Should().Be("VENDOR_UNAVAILABLE");
    }

    [Fact]
    public async Task DispatchDueAsync_WhenRecordAlreadyConfirmed_SkipsAndDoesNotCallWorkflow()
    {
        var selected = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var confirmed = selected with { AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.Confirmed };
        var repository = new FakeAcknowledgmentRepository(confirmed);
        repository.DueRecords.Add(selected);
        var workflow = new FakeAcknowledgmentWorkflow();
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        result.SkippedCount.Should().Be(1);
        workflow.ProcessCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenRecordSkippedDisabled_SkipsAndDoesNotCallWorkflow()
    {
        var selected = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var skipped = selected with { AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.SkippedDisabled };
        var repository = new FakeAcknowledgmentRepository(skipped);
        repository.DueRecords.Add(selected);
        var workflow = new FakeAcknowledgmentWorkflow();
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        result.SkippedCount.Should().Be(1);
        workflow.ProcessCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenConfirmGuardDisabledWorkflowMarksSkippedDisabled()
    {
        var record = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var repository = new FakeAcknowledgmentRepository(record);
        repository.DueRecords.Add(record);
        var workflow = new FakeAcknowledgmentWorkflow
        {
            OnProcess = command => repository.Records[AcknowledgmentId1] =
                repository.Records[AcknowledgmentId1] with
                {
                    AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.SkippedDisabled,
                    VendorCode = "CONFIRM_DISABLED",
                    VendorMessage = "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false."
                }
        };
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        workflow.ProcessCalls.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        repository.Records[AcknowledgmentId1].AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.SkippedDisabled);
        repository.Records[AcknowledgmentId1].VendorCode.Should().Be("CONFIRM_DISABLED");
    }

    [Fact]
    public async Task DispatchDueAsync_WhenOneRetryThrows_ContinuesProcessingRemainingDueRecords()
    {
        var first = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var second = Record(AcknowledgmentId2, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var repository = new FakeAcknowledgmentRepository(first, second);
        repository.DueRecords.Add(first);
        repository.DueRecords.Add(second);
        var workflow = new FakeAcknowledgmentWorkflow
        {
            OnProcess = command =>
            {
                if (command.PaymentAttemptId == first.PaymentAttemptId)
                {
                    throw new InvalidOperationException("Simulated dispatch failure.");
                }

                repository.Records[AcknowledgmentId2] =
                    repository.Records[AcknowledgmentId2] with { AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.Confirmed };
            }
        };
        var sut = CreateSut(repository, workflow);

        var result = await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        workflow.ProcessCalls.Should().Be(2);
        result.FailedCount.Should().Be(1);
        result.ConfirmedCount.Should().Be(1);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenCancellationRequestedBeforeDispatch_StopsBeforeQuery()
    {
        var repository = new FakeAcknowledgmentRepository();
        var workflow = new FakeAcknowledgmentWorkflow();
        var sut = CreateSut(repository, workflow);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), cts.Token));

        repository.FindDueCalls.Should().Be(0);
        workflow.ProcessCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenRetryThrows_DoesNotLogSecretsOrRawAuthMaterial()
    {
        var record = Record(AcknowledgmentId1, VendorPaymentAcknowledgmentStatuses.RetryPending);
        var repository = new FakeAcknowledgmentRepository(record);
        repository.DueRecords.Add(record);
        var workflow = new FakeAcknowledgmentWorkflow
        {
            OnProcess = command => throw new InvalidOperationException("HIKCENTRAL_APP_SECRET=secret Authorization=Bearer abc signature=xyz")
        };
        var logger = new CapturingLogger<VendorPaymentAcknowledgmentRetryDispatcherService>();
        var sut = CreateSut(repository, workflow, logger);

        await sut.DispatchDueAsync(new DispatchVendorPaymentAcknowledgmentRetriesCommand(25), CancellationToken.None);

        var loggedText = string.Join(Environment.NewLine, logger.Messages);
        loggedText.Should().NotContain("secret");
        loggedText.Should().NotContain("Authorization");
        loggedText.Should().NotContain("signature");
        loggedText.Should().NotContain("HIKCENTRAL_APP_SECRET");
    }

    private static VendorPaymentAcknowledgmentRetryDispatcherService CreateSut(
        FakeAcknowledgmentRepository repository,
        FakeAcknowledgmentWorkflow workflow,
        ILogger<VendorPaymentAcknowledgmentRetryDispatcherService>? logger = null) =>
        new(
            repository,
            workflow,
            new FakeClock(),
            logger ?? new CapturingLogger<VendorPaymentAcknowledgmentRetryDispatcherService>());

    private static VendorPaymentAcknowledgmentRecord Record(
        Guid acknowledgmentId,
        string status,
        DateTimeOffset? nextRetryAt = null) =>
        new(
            acknowledgmentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "HIKCENTRAL",
            "HIK:CARD-279",
            "TICKET-279",
            "CARD-279",
            status,
            null,
            null,
            5000,
            "PHP",
            null,
            null,
            1,
            Now.AddMinutes(-5),
            nextRetryAt,
            $"vendor-ack-{acknowledgmentId:N}",
            Guid.Parse("279c0000-0000-0000-0000-0000000000aa"),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5));

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeAcknowledgmentWorkflow : IVendorPaymentAcknowledgmentWorkflow
    {
        public int ProcessCalls { get; private set; }

        public Action<VendorPaymentAcknowledgmentWorkflowCommand>? OnProcess { get; init; }

        public Task ProcessAsync(
            VendorPaymentAcknowledgmentWorkflowCommand command,
            CancellationToken cancellationToken)
        {
            ProcessCalls++;
            OnProcess?.Invoke(command);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAcknowledgmentRepository : IVendorPaymentAcknowledgmentRepository
    {
        public FakeAcknowledgmentRepository(params VendorPaymentAcknowledgmentRecord[] records)
        {
            Records = records.ToDictionary(record => record.VendorPaymentAcknowledgmentId);
        }

        public Dictionary<Guid, VendorPaymentAcknowledgmentRecord> Records { get; }

        public List<VendorPaymentAcknowledgmentRecord> DueRecords { get; } = [];

        public int FindDueCalls { get; private set; }

        public Task<IReadOnlyList<VendorPaymentAcknowledgmentRecord>> FindDueRetryPendingAsync(
            DateTimeOffset utcNow,
            int limit,
            CancellationToken cancellationToken)
        {
            FindDueCalls++;
            return Task.FromResult<IReadOnlyList<VendorPaymentAcknowledgmentRecord>>(DueRecords.Take(limit).ToArray());
        }

        public Task<VendorPaymentAcknowledgmentSearchResult> SearchAsync(
            SearchVendorPaymentAcknowledgmentsQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VendorPaymentAcknowledgmentSearchResult(
                Records.Values.ToArray(),
                new VendorPaymentAcknowledgmentStatusBucketCounts(0, 0, 0, 0, 0, 0),
                query.PageIndex,
                query.PageSize,
                HasMore: false));

        public Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
            Guid vendorPaymentAcknowledgmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Records.TryGetValue(vendorPaymentAcknowledgmentId, out var record) ? record : null);

        public Task<VendorPaymentAcknowledgmentRecord> MarkFailedAsync(
            MarkVendorPaymentAcknowledgmentFailedCommand command,
            CancellationToken cancellationToken)
        {
            var current = Records[command.VendorPaymentAcknowledgmentId];
            var updated = current with
            {
                AcknowledgmentStatus = command.NextRetryAt is null
                    ? VendorPaymentAcknowledgmentStatuses.Failed
                    : VendorPaymentAcknowledgmentStatuses.RetryPending,
                VendorCode = command.VendorCode,
                VendorMessage = command.VendorMessage,
                LastAttemptedAt = command.LastAttemptedAt,
                NextRetryAt = command.NextRetryAt,
                AttemptCount = current.AttemptCount + 1,
                UpdatedAt = command.UpdatedAt
            };
            Records[command.VendorPaymentAcknowledgmentId] = updated;
            return Task.FromResult(updated);
        }

        public Task<VendorPaymentAcknowledgmentBasis?> LoadBasisAsync(
            Guid paymentAttemptId,
            Guid paymentConfirmationId,
            Guid parkingSessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VendorPaymentAcknowledgmentRecord> CreatePendingAsync(
            CreateVendorPaymentAcknowledgmentCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VendorPaymentAcknowledgmentRecord> MarkConfirmedAsync(
            MarkVendorPaymentAcknowledgmentConfirmedCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VendorPaymentAcknowledgmentRecord> MarkSkippedDisabledAsync(
            MarkVendorPaymentAcknowledgmentSkippedDisabledCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VendorPaymentAcknowledgmentRecord?> ReadByPaymentConfirmationAsync(
            Guid paymentConfirmationId,
            string vendorSystemCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VendorPaymentAcknowledgmentRecord?> ReadLatestByPaymentAttemptAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
