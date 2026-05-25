using System.Diagnostics;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.CentralPms.IntegrationTests.Eventing;

/// <summary>
/// Live RabbitMQ fixture coverage for Central PMS reconciliation outbox publication.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - RabbitMQ dispatch is operational eventing only and never mutates payment, provider, exit, gate, or settlement truth.
/// - The durable outbox database lifecycle remains authoritative for publication status and evidence.
/// </summary>
public sealed class ReconciliationRabbitMqOutboxDispatcherIntegrationTests
{
    private const string BrokerTestFlagName = "EXITPASS_RABBITMQ_TESTS_ENABLED";
    private const string ExchangeName = "exitpass.central-pms.reconciliation.tests";
    private const string EventType = "ReconciliationItemEvaluated";
    private const string RoutingKey = "central-pms.reconciliation.ReconciliationItemEvaluated";

    private readonly ITestOutputHelper _output;

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Creates the live RabbitMQ reconciliation outbox test fixture.
    /// </summary>
    public ReconciliationRabbitMqOutboxDispatcherIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies one reconciliation outbox row is published to RabbitMQ and marked PUBLISHED with publication evidence.
    /// </summary>
    [Trait("Category", "RabbitMQ")]
    [Trait("Category", "Integration")]
    [RabbitMqReconciliationOutboxFact]
    public async Task ReconciliationOutbox_WithRabbitMqPublisher_PublishesEnvelopeAndRecordsDatabaseEvidence()
    {
        var gate = RabbitMqReconciliationOutboxTestGate.FromEnvironment();
        if (!gate.ShouldRun)
        {
            _output.WriteLine(gate.SkipReason);
            return;
        }

        var outboxEventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var publisherServiceIdentityId = await ResolvePublisherServiceIdentityIdAsync();

        await InsertOutboxEventAsync(outboxEventId, aggregateId, correlationId);

        using var connection = CreateConnection(gate.Settings);
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        var queueName = $"exitpass.central-pms.reconciliation-outbox-test.{Guid.NewGuid():N}";
        channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: true,
            arguments: null);
        channel.QueueBind(queueName, ExchangeName, RoutingKey);

        try
        {
            var service = CreateService(gate.Settings);

            var result = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(10, publisherServiceIdentityId),
                CancellationToken.None);
            var second = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(10, publisherServiceIdentityId),
                CancellationToken.None);

            result.ClaimedCount.Should().Be(1);
            result.PublishedCount.Should().Be(1);
            result.FailedCount.Should().Be(0);
            second.ClaimedCount.Should().Be(0);

            var message = GetMessage(channel, queueName, TimeSpan.FromSeconds(10));
            message.Should().NotBeNull("the RabbitMQ-bound queue should receive the reconciliation outbox event");
            message!.BasicProperties.ContentType.Should().Be("application/json");
            message.BasicProperties.Persistent.Should().BeTrue();
            message.BasicProperties.MessageId.Should().Be(outboxEventId.ToString());
            message.BasicProperties.CorrelationId.Should().Be(correlationId.ToString());
            message.BasicProperties.Type.Should().Be(EventType);

            using var document = JsonDocument.Parse(message.Body.ToArray());
            var root = document.RootElement;
            root.GetProperty("eventId").GetGuid().Should().Be(outboxEventId);
            root.GetProperty("eventType").GetString().Should().Be(EventType);
            root.GetProperty("correlationId").GetGuid().Should().Be(correlationId);
            root.GetProperty("occurredAtUtc").ValueKind.Should().Be(JsonValueKind.String);
            root.GetProperty("payload").GetProperty("payloadRef").GetString()
                .Should().Be("central-pms://reconciliation-events/rabbitmq-live-test");
            root.GetProperty("payload").GetProperty("payloadContentType").GetString()
                .Should().Be("application/json");

            var state = await ReadOutboxStateAsync(outboxEventId, correlationId);
            state.PublicationStatus.Should().Be("PUBLISHED");
            state.PublishedPublicationCount.Should().Be(1);
            state.FailedPublicationCount.Should().Be(0);
            state.BrokerType.Should().Be("RABBITMQ");
            state.BrokerMessageId.Should().Be(outboxEventId.ToString());
        }
        finally
        {
            channel.QueueUnbind(queueName, ExchangeName, RoutingKey);
            channel.QueueDelete(queueName, ifUnused: false, ifEmpty: false);
            await CleanupAsync(outboxEventId, correlationId);
        }
    }

    /// <summary>
    /// Verifies broker fixture gating remains disabled unless explicitly enabled.
    /// </summary>
    [Fact]
    public void ReconciliationRabbitMqOutbox_WhenBrokerFlagDisabled_SkipsBrokerTest()
    {
        var variables = ValidEnvironmentVariables();
        variables[BrokerTestFlagName] = "false";

        var gate = RabbitMqReconciliationOutboxTestGate.FromVariables(variables);

        gate.ShouldRun.Should().BeFalse();
        gate.SkipReason.Should().Contain(BrokerTestFlagName);
    }

    /// <summary>
    /// Verifies broker fixture defaults match the local Docker RabbitMQ service.
    /// </summary>
    [Fact]
    public void ReconciliationRabbitMqOutbox_WhenBrokerFlagEnabled_UsesLocalDockerDefaults()
    {
        var variables = new Dictionary<string, string?>
        {
            [BrokerTestFlagName] = "true"
        };

        var gate = RabbitMqReconciliationOutboxTestGate.FromVariables(variables);

        gate.ShouldRun.Should().BeTrue();
        gate.Settings.Host.Should().Be("localhost");
        gate.Settings.Port.Should().Be(5672);
        gate.Settings.Username.Should().Be("exitpass");
        gate.Settings.Password.Should().Be("change_me");
        gate.Settings.VirtualHost.Should().Be("/");
        gate.Settings.ExchangeName.Should().Be(ExchangeName);
    }

    private static IReconciliationOutboxDispatcherService CreateService(RabbitMqBrokerSettings settings)
    {
        var repository = new ReconciliationOutboxDispatcherRepository(
            ConnectionString,
            NullLogger<ReconciliationOutboxDispatcherRepository>.Instance);
        var publisher = new RabbitMqReconciliationOutboxEventPublisher(
            new RabbitMqReconciliationOutboxPublisherOptions
            {
                Enabled = true,
                Host = settings.Host,
                Port = settings.Port,
                Username = settings.Username,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                ExchangeName = settings.ExchangeName
            },
            NullLogger<RabbitMqReconciliationOutboxEventPublisher>.Instance);

        return new ReconciliationOutboxDispatcherService(repository, publisher);
    }

    private static IConnection CreateConnection(RabbitMqBrokerSettings settings)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost
        };

        return factory.CreateConnection();
    }

    private static BasicGetResult? GetMessage(IModel channel, string queueName, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < deadline)
        {
            var message = channel.BasicGet(queueName, autoAck: true);
            if (message is not null)
            {
                return message;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }

        return null;
    }

    private static async Task InsertOutboxEventAsync(Guid outboxEventId, Guid aggregateId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events.outbox_events (
                outbox_event_id,
                source_schema,
                source_table,
                event_type,
                event_version,
                aggregate_type,
                aggregate_id,
                routing_key,
                exchange_name,
                payload_ref,
                payload_content_type,
                publication_status,
                available_at,
                retry_count,
                max_retry_count,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @outbox_event_id,
                'reconciliation',
                'reconciliation_items',
                @event_type,
                1,
                'ReconciliationItem',
                @aggregate_id,
                @routing_key,
                @exchange_name,
                'central-pms://reconciliation-events/rabbitmq-live-test',
                'application/json',
                'PENDING',
                now(),
                0,
                10,
                @correlation_id,
                now(),
                now()
            );
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("event_type", EventType);
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        command.Parameters.AddWithValue("routing_key", RoutingKey);
        command.Parameters.AddWithValue("exchange_name", ExchangeName);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ResolvePublisherServiceIdentityIdAsync()
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

    private static async Task<OutboxState> ReadOutboxStateAsync(Guid outboxEventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                oe.publication_status::text AS publication_status,
                (
                    SELECT COUNT(*)
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                      AND ep.publication_status = 'PUBLISHED'
                ) AS published_publication_count,
                (
                    SELECT COUNT(*)
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                      AND ep.publication_status = 'FAILED'
                ) AS failed_publication_count,
                (
                    SELECT ep.broker_type::text
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                    ORDER BY ep.publication_attempt_number DESC
                    LIMIT 1
                ) AS broker_type,
                (
                    SELECT ep.broker_message_id
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                    ORDER BY ep.publication_attempt_number DESC
                    LIMIT 1
                ) AS broker_message_id
            FROM events.outbox_events oe
            WHERE oe.outbox_event_id = @outbox_event_id
              AND oe.correlation_id = @correlation_id;
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new OutboxState(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task CleanupAsync(Guid outboxEventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM events.dead_letter_records WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            DELETE FROM events.event_publications WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            DELETE FROM events.outbox_events WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await command.ExecuteNonQueryAsync();
    }

    private static Dictionary<string, string?> ValidEnvironmentVariables()
    {
        return new Dictionary<string, string?>
        {
            [BrokerTestFlagName] = "true",
            ["RABBITMQ__HOST"] = "localhost",
            ["RABBITMQ__PORT"] = "5672",
            ["RABBITMQ__USERNAME"] = "exitpass",
            ["RABBITMQ__PASSWORD"] = "change_me",
            ["RABBITMQ__VHOST"] = "/",
            ["RABBITMQ__EXCHANGE"] = ExchangeName
        };
    }

    private sealed class RabbitMqReconciliationOutboxFactAttribute : FactAttribute
    {
        public RabbitMqReconciliationOutboxFactAttribute()
        {
            var gate = RabbitMqReconciliationOutboxTestGate.FromEnvironment();
            if (!gate.ShouldRun)
            {
                Skip = gate.SkipReason;
            }
        }
    }

    private sealed class RabbitMqReconciliationOutboxTestGate
    {
        private RabbitMqReconciliationOutboxTestGate(
            bool shouldRun,
            string skipReason,
            RabbitMqBrokerSettings settings)
        {
            ShouldRun = shouldRun;
            SkipReason = skipReason;
            Settings = settings;
        }

        public bool ShouldRun { get; }

        public string SkipReason { get; }

        public RabbitMqBrokerSettings Settings { get; }

        public static RabbitMqReconciliationOutboxTestGate FromEnvironment()
        {
            return FromVariables(Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString()));
        }

        public static RabbitMqReconciliationOutboxTestGate FromVariables(IReadOnlyDictionary<string, string?> variables)
        {
            if (!IsTrue(variables.GetValueOrDefault(BrokerTestFlagName)))
            {
                return Skip("RabbitMQ reconciliation outbox integration test skipped because EXITPASS_RABBITMQ_TESTS_ENABLED is not true.");
            }

            if (!int.TryParse(DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__PORT"), "5672"), out var port))
            {
                return Skip("RabbitMQ reconciliation outbox integration test skipped because RABBITMQ__PORT must be an integer.");
            }

            return new RabbitMqReconciliationOutboxTestGate(
                shouldRun: true,
                skipReason: string.Empty,
                settings: new RabbitMqBrokerSettings(
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__HOST"), "localhost"),
                    port,
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__USERNAME"), "exitpass"),
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__PASSWORD"), "change_me"),
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__VHOST"), "/"),
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__EXCHANGE"), ExchangeName)));
        }

        private static RabbitMqReconciliationOutboxTestGate Skip(string reason)
        {
            return new RabbitMqReconciliationOutboxTestGate(
                shouldRun: false,
                skipReason: reason,
                settings: new RabbitMqBrokerSettings(string.Empty, 5672, string.Empty, string.Empty, "/", ExchangeName));
        }

        private static bool IsTrue(string? value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string DefaultIfBlank(string? value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }
    }

    private sealed record RabbitMqBrokerSettings(
        string Host,
        int Port,
        string Username,
        string Password,
        string VirtualHost,
        string ExchangeName);

    private sealed record OutboxState(
        string PublicationStatus,
        long PublishedPublicationCount,
        long FailedPublicationCount,
        string? BrokerType,
        string? BrokerMessageId);
}
