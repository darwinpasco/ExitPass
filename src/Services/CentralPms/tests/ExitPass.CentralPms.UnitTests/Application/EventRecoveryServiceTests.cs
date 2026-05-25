using ExitPass.CentralPms.Application.Eventing;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for Central PMS event recovery application rules.
/// </summary>
public sealed class EventRecoveryServiceTests
{
    /// <summary>
    /// Verifies replay can be requested for an open dead-letter record.
    /// </summary>
    [Fact]
    public async Task RequestDeadLetterReplay_WhenOpen_UpdatesReplayStatus()
    {
        var deadLetterId = Guid.NewGuid();
        var repository = Substitute.For<IEventRecoveryRepository>();
        repository.GetDeadLetterAsync(Arg.Any<GetDeadLetterQuery>(), Arg.Any<CancellationToken>())
            .Returns(DeadLetter(deadLetterId, "OPEN"));
        repository.RequestDeadLetterReplayAsync(Arg.Any<RequestDeadLetterReplayCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeadLetter(deadLetterId, "REPLAY_REQUESTED"));
        var service = new EventRecoveryService(repository);

        var result = await service.RequestDeadLetterReplayAsync(
            new RequestDeadLetterReplayCommand(deadLetterId, null, null, "OPERATOR_REPLAY", Guid.NewGuid()),
            CancellationToken.None);

        result.DeadLetterStatus.Should().Be("REPLAY_REQUESTED");
    }

    /// <summary>
    /// Verifies replay is rejected for terminal dead-letter status.
    /// </summary>
    [Fact]
    public async Task RequestDeadLetterReplay_WhenResolved_RejectsTerminalStatus()
    {
        var deadLetterId = Guid.NewGuid();
        var repository = Substitute.For<IEventRecoveryRepository>();
        repository.GetDeadLetterAsync(Arg.Any<GetDeadLetterQuery>(), Arg.Any<CancellationToken>())
            .Returns(DeadLetter(deadLetterId, "RESOLVED"));
        var service = new EventRecoveryService(repository);

        var act = () => service.RequestDeadLetterReplayAsync(
            new RequestDeadLetterReplayCommand(deadLetterId, null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DEAD_LETTER_REPLAY_NOT_ALLOWED");
        await repository.DidNotReceive().RequestDeadLetterReplayAsync(
            Arg.Any<RequestDeadLetterReplayCommand>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies replay outcome can only be marked from REPLAY_REQUESTED.
    /// </summary>
    [Fact]
    public async Task MarkDeadLetterReplayOutcome_WhenNotReplayRequested_Rejects()
    {
        var deadLetterId = Guid.NewGuid();
        var repository = Substitute.For<IEventRecoveryRepository>();
        repository.GetDeadLetterAsync(Arg.Any<GetDeadLetterQuery>(), Arg.Any<CancellationToken>())
            .Returns(DeadLetter(deadLetterId, "OPEN"));
        var service = new EventRecoveryService(repository);

        var act = () => service.MarkDeadLetterReplayOutcomeAsync(
            new MarkDeadLetterReplayOutcomeCommand(deadLetterId, "REPLAYED", null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DEAD_LETTER_REPLAY_OUTCOME_NOT_ALLOWED");
    }

    /// <summary>
    /// Verifies unknown consumer checkpoints return deterministic errors.
    /// </summary>
    [Fact]
    public async Task GetConsumerCheckpoint_WhenMissing_ReturnsDeterministicError()
    {
        var repository = Substitute.For<IEventRecoveryRepository>();
        repository.GetConsumerCheckpointAsync(Arg.Any<GetConsumerCheckpointQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConsumerCheckpointRecord>());
        var service = new EventRecoveryService(repository);

        var act = () => service.GetConsumerCheckpointAsync(
            new GetConsumerCheckpointQuery("missing-consumer"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CONSUMER_CHECKPOINT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies conservative checkpoint status updates reject worker-owned statuses.
    /// </summary>
    [Fact]
    public async Task UpdateConsumerCheckpointStatus_WhenTargetLocked_Rejects()
    {
        var repository = Substitute.For<IEventRecoveryRepository>();
        var service = new EventRecoveryService(repository);

        var act = () => service.UpdateConsumerCheckpointStatusAsync(
            new UpdateConsumerCheckpointStatusCommand("consumer", "LOCKED", Guid.NewGuid(), null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CONSUMER_CHECKPOINT_STATUS_UPDATE_NOT_ALLOWED");
    }

    /// <summary>
    /// Verifies list limits are bounded for operational safety.
    /// </summary>
    [Fact]
    public async Task ListDeadLetters_NormalizesLimit()
    {
        var repository = Substitute.For<IEventRecoveryRepository>();
        repository.ListDeadLettersAsync(Arg.Any<ListDeadLettersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DeadLetterRecord>());
        var service = new EventRecoveryService(repository);

        await service.ListDeadLettersAsync(new ListDeadLettersQuery(500, null, null), CancellationToken.None);

        await repository.Received(1).ListDeadLettersAsync(
            Arg.Is<ListDeadLettersQuery>(query => query.Limit == 100),
            Arg.Any<CancellationToken>());
    }

    private static DeadLetterRecord DeadLetter(Guid id, string status) =>
        new(
            id,
            OutboxEventId: null,
            EventPublicationId: null,
            ConsumerName: null,
            DeadLetterType: "RETRY_EXHAUSTED",
            DeadLetterStatus: status,
            FailureReasonCode: "TEST",
            FailureDetailRef: null,
            PayloadHash: null,
            DeadLetteredAt: DateTimeOffset.UtcNow,
            ReplayRequestedAt: null,
            ResolvedAt: null,
            ResolutionReasonCode: null,
            CorrelationId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
}
