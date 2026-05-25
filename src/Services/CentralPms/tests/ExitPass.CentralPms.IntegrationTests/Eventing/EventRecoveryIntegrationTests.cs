using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Eventing;

/// <summary>
/// Integration tests for event dead-letter replay and consumer checkpoint recovery.
/// </summary>
public sealed class EventRecoveryIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies dead-letter replay request is deterministic and does not mutate payment truth tables.
    /// </summary>
    [Fact]
    public async Task DeadLetterReplayRequest_MarksReplayRequestedWithoutPaymentTruthMutation()
    {
        var deadLetterId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var before = await ReadTruthCountsAsync();
        await InsertDeadLetterAsync(deadLetterId, correlationId, "OPEN");

        try
        {
            var service = CreateService();

            var requested = await service.RequestDeadLetterReplayAsync(
                new RequestDeadLetterReplayCommand(deadLetterId, null, null, "INTEGRATION_TEST_REPLAY", correlationId),
                CancellationToken.None);

            requested.DeadLetterStatus.Should().Be("REPLAY_REQUESTED");
            requested.ReplayRequestedAt.Should().NotBeNull();

            var after = await ReadTruthCountsAsync();
            after.Should().Be(before);
        }
        finally
        {
            await CleanupDeadLetterAsync(deadLetterId);
        }
    }

    /// <summary>
    /// Verifies terminal dead-letter records reject replay requests.
    /// </summary>
    [Fact]
    public async Task DeadLetterReplayRequest_WhenTerminalStatus_Rejects()
    {
        var deadLetterId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await InsertDeadLetterAsync(deadLetterId, correlationId, "RESOLVED");

        try
        {
            var service = CreateService();

            var act = () => service.RequestDeadLetterReplayAsync(
                new RequestDeadLetterReplayCommand(deadLetterId, null, null, null, correlationId),
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("DEAD_LETTER_REPLAY_NOT_ALLOWED");
        }
        finally
        {
            await CleanupDeadLetterAsync(deadLetterId);
        }
    }

    /// <summary>
    /// Verifies replay outcome can mark a requested replay as replayed.
    /// </summary>
    [Fact]
    public async Task DeadLetterReplayOutcome_WhenReplayRequested_MarksReplayed()
    {
        var deadLetterId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await InsertDeadLetterAsync(deadLetterId, correlationId, "OPEN");

        try
        {
            var service = CreateService();
            await service.RequestDeadLetterReplayAsync(
                new RequestDeadLetterReplayCommand(deadLetterId, null, null, "INTEGRATION_TEST_REPLAY", correlationId),
                CancellationToken.None);

            var replayed = await service.MarkDeadLetterReplayOutcomeAsync(
                new MarkDeadLetterReplayOutcomeCommand(deadLetterId, "REPLAYED", null, null, "INTEGRATION_TEST_REPLAYED", correlationId),
                CancellationToken.None);

            replayed.DeadLetterStatus.Should().Be("REPLAYED");
            replayed.ResolvedAt.Should().NotBeNull();
        }
        finally
        {
            await CleanupDeadLetterAsync(deadLetterId);
        }
    }

    /// <summary>
    /// Verifies consumer checkpoint list/read/update works against the live events schema.
    /// </summary>
    [Fact]
    public async Task ConsumerCheckpoint_ListReadAndPause_Works()
    {
        var checkpointId = Guid.NewGuid();
        var consumerName = $"event-recovery-test-{Guid.NewGuid():N}";
        var serviceIdentityId = await ResolveServiceIdentityIdAsync();
        await InsertConsumerCheckpointAsync(checkpointId, consumerName, serviceIdentityId, "ACTIVE");

        try
        {
            var service = CreateService();

            var checkpoints = await service.ListConsumerCheckpointsAsync(
                new ListConsumerCheckpointsQuery(25, "ACTIVE"),
                CancellationToken.None);
            checkpoints.Should().Contain(checkpoint => checkpoint.ConsumerCheckpointId == checkpointId);

            var checkpoint = await service.GetConsumerCheckpointAsync(
                new GetConsumerCheckpointQuery(consumerName),
                CancellationToken.None);
            checkpoint.CheckpointStatus.Should().Be("ACTIVE");

            var paused = await service.UpdateConsumerCheckpointStatusAsync(
                new UpdateConsumerCheckpointStatusCommand(consumerName, "PAUSED", serviceIdentityId, "INTEGRATION_TEST_PAUSE", Guid.NewGuid()),
                CancellationToken.None);
            paused.CheckpointStatus.Should().Be("PAUSED");
            paused.LockedAt.Should().BeNull();
        }
        finally
        {
            await CleanupConsumerCheckpointAsync(checkpointId);
        }
    }

    private static IEventRecoveryService CreateService() =>
        new EventRecoveryService(new EventRecoveryRepository(ConnectionString));

    private static async Task InsertDeadLetterAsync(Guid deadLetterId, Guid correlationId, string status)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events.dead_letter_records (
                dead_letter_record_id,
                consumer_name,
                dead_letter_type,
                dead_letter_status,
                failure_reason_code,
                failure_detail_ref,
                dead_lettered_at,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @dead_letter_record_id,
                'event-recovery-integration-test',
                'CONSUMER_FAILURE',
                @dead_letter_status::events.dead_letter_status_enum,
                'INTEGRATION_TEST_FAILURE',
                'integration-test://event-recovery',
                now(),
                @correlation_id,
                now(),
                now()
            );
            """;
        command.Parameters.AddWithValue("dead_letter_record_id", deadLetterId);
        command.Parameters.AddWithValue("dead_letter_status", status);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertConsumerCheckpointAsync(
        Guid checkpointId,
        string consumerName,
        Guid serviceIdentityId,
        string status)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events.consumer_checkpoints (
                consumer_checkpoint_id,
                consumer_name,
                checkpoint_status,
                updated_by_service_identity_id,
                created_at,
                updated_at
            )
            VALUES (
                @consumer_checkpoint_id,
                @consumer_name,
                @checkpoint_status::events.consumer_checkpoint_status_enum,
                @service_identity_id,
                now(),
                now()
            );
            """;
        command.Parameters.AddWithValue("consumer_checkpoint_id", checkpointId);
        command.Parameters.AddWithValue("consumer_name", consumerName);
        command.Parameters.AddWithValue("checkpoint_status", status);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ResolveServiceIdentityIdAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_identity_id
            FROM identity.service_identities
            WHERE identity_status::text = 'ACTIVE'
              AND service_identity_code IN ('central-pms', 'CENTRAL_PMS_API')
            ORDER BY CASE service_identity_code WHEN 'central-pms' THEN 0 ELSE 1 END
            LIMIT 1;
            """;
        var value = await command.ExecuteScalarAsync();
        value.Should().BeOfType<Guid>();
        return (Guid)value!;
    }

    private static async Task<TruthCounts> ReadTruthCountsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts) AS payment_attempt_count,
                (SELECT COUNT(*) FROM core.payment_confirmations) AS payment_confirmation_count,
                (SELECT COUNT(*) FROM core.exit_authorizations) AS exit_authorization_count;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new TruthCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task CleanupDeadLetterAsync(Guid deadLetterId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events.dead_letter_records WHERE dead_letter_record_id = @dead_letter_record_id;";
        command.Parameters.AddWithValue("dead_letter_record_id", deadLetterId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupConsumerCheckpointAsync(Guid checkpointId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events.consumer_checkpoints WHERE consumer_checkpoint_id = @consumer_checkpoint_id;";
        command.Parameters.AddWithValue("consumer_checkpoint_id", checkpointId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record TruthCounts(
        long PaymentAttemptCount,
        long PaymentConfirmationCount,
        long ExitAuthorizationCount);
}
