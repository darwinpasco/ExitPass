using System.Diagnostics;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.CentralPms.IntegrationTests.Eventing;

/// <summary>
/// Broker-level integration coverage for the Central PMS RabbitMQ integration event publisher.
/// </summary>
public sealed class RabbitMqPublisherBrokerIntegrationTests
{
    private const string BrokerTestFlagName = "EXITPASS_RUN_RABBITMQ_BROKER_TESTS";
    private const string DefaultExchangeName = "exitpass.central-pms.events";
    private const string EventType = "Test.EventPublished";
    private const string RoutingKey = "central-pms." + EventType;

    private static readonly string[] RequiredEnvironmentVariableNames =
    [
        "RABBITMQ__HOST",
        "RABBITMQ__PORT",
        "RABBITMQ__USERNAME",
        "RABBITMQ__PASSWORD"
    ];

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Creates the RabbitMQ publisher broker integration test fixture.
    /// </summary>
    /// <param name="output">Test output sink used for safe broker gate messages.</param>
    public RabbitMqPublisherBrokerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Publishes a deterministic envelope through the real publisher and verifies it from a bound RabbitMQ queue.
    /// </summary>
    [RabbitMqBrokerFact]
    public async Task RabbitMqPublisher_WhenBrokerEnabled_PublishesEnvelopeToBoundQueue()
    {
        var gate = RabbitMqBrokerTestGate.FromEnvironment();
        if (!gate.ShouldRun)
        {
            _output.WriteLine(gate.SkipReason);
            return;
        }

        using var serviceProvider = BuildServiceProvider(gate.Settings);
        var publisher = serviceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var factory = CreateConnectionFactory(gate.Settings);
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(
            exchange: gate.Settings.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        var queueName = $"exitpass.central-pms.publisher-test.{Guid.NewGuid():N}";
        channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: true,
            arguments: null);
        channel.QueueBind(queueName, gate.Settings.ExchangeName, RoutingKey);

        try
        {
            var envelope = new IntegrationEventEnvelope
            {
                EventId = Guid.Parse("7fd700b7-677a-4a4d-8824-761cf2314c87"),
                EventType = EventType,
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
                CorrelationId = Guid.Parse("e253fefa-f5b3-4bd4-80d5-8baafc4de931"),
                AggregateId = "rabbitmq-publisher-test",
                AggregateType = "CentralPmsBrokerTest",
                SchemaVersion = 1,
                Payload = new
                {
                    message = "broker publish verification",
                    attempt = 1
                }
            };

            await publisher.PublishAsync(envelope, CancellationToken.None);

            var message = GetMessage(channel, queueName, TimeSpan.FromSeconds(10));

            message.Should().NotBeNull("the bound queue should receive the broker-published event");
            message!.BasicProperties.ContentType.Should().Be("application/json");
            message.BasicProperties.Persistent.Should().BeTrue();
            message.BasicProperties.MessageId.Should().Be(envelope.EventId.ToString());
            message.BasicProperties.CorrelationId.Should().Be(envelope.CorrelationId.ToString());
            message.BasicProperties.Type.Should().Be(envelope.EventType);

            using var document = JsonDocument.Parse(message.Body.ToArray());
            var root = document.RootElement;

            root.GetProperty("eventId").GetGuid().Should().Be(envelope.EventId);
            root.GetProperty("eventType").GetString().Should().Be(envelope.EventType);
            root.GetProperty("correlationId").GetGuid().Should().Be(envelope.CorrelationId);
            root.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThan(0);
            root.GetProperty("payload").ValueKind.Should().Be(JsonValueKind.Object);
        }
        finally
        {
            channel.QueueUnbind(queueName, gate.Settings.ExchangeName, RoutingKey);
            channel.QueueDelete(queueName, ifUnused: false, ifEmpty: false);
        }
    }

    /// <summary>
    /// Verifies that broker coverage is gated off unless explicitly enabled.
    /// </summary>
    [Fact]
    public void RabbitMqPublisher_WhenBrokerFlagDisabled_SkipsBrokerTest()
    {
        var variables = ValidEnvironmentVariables();
        variables[BrokerTestFlagName] = "false";

        var gate = RabbitMqBrokerTestGate.FromVariables(variables);

        gate.ShouldRun.Should().BeFalse();
        gate.SkipReason.Should().Contain(BrokerTestFlagName);
    }

    /// <summary>
    /// Verifies that missing broker credentials produce a safe skip message.
    /// </summary>
    [Fact]
    public void RabbitMqPublisher_WhenRequiredEnvMissing_SkipsWithSafeMessage()
    {
        var variables = ValidEnvironmentVariables();
        variables.Remove("RABBITMQ__PASSWORD");

        var gate = RabbitMqBrokerTestGate.FromVariables(variables);

        gate.ShouldRun.Should().BeFalse();
        gate.SkipReason.Should().Contain("RABBITMQ__PASSWORD");
        gate.SkipReason.Should().NotContain("change_me");
    }

    private static ServiceProvider BuildServiceProvider(RabbitMqBrokerSettings settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:Host"] = settings.Host,
                ["Messaging:RabbitMq:Port"] = settings.Port.ToString(),
                ["Messaging:RabbitMq:Username"] = settings.Username,
                ["Messaging:RabbitMq:Password"] = settings.Password,
                ["Messaging:RabbitMq:VirtualHost"] = settings.VirtualHost,
                ["Messaging:RabbitMq:ExchangeName"] = settings.ExchangeName
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddCentralPmsEventPublishing(configuration);

        return services.BuildServiceProvider();
    }

    private static ConnectionFactory CreateConnectionFactory(RabbitMqBrokerSettings settings)
    {
        return new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost
        };
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
            ["RABBITMQ__EXCHANGE"] = DefaultExchangeName
        };
    }

    private sealed class RabbitMqBrokerFactAttribute : FactAttribute
    {
        public RabbitMqBrokerFactAttribute()
        {
            var gate = RabbitMqBrokerTestGate.FromEnvironment();
            if (!gate.ShouldRun)
            {
                Skip = gate.SkipReason;
            }
        }
    }

    private sealed class RabbitMqBrokerTestGate
    {
        private RabbitMqBrokerTestGate(
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

        public static RabbitMqBrokerTestGate FromEnvironment()
        {
            return FromVariables(Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString()));
        }

        public static RabbitMqBrokerTestGate FromVariables(IReadOnlyDictionary<string, string?> variables)
        {
            if (!IsTrue(variables.GetValueOrDefault(BrokerTestFlagName)))
            {
                return Skip("RabbitMQ broker integration test skipped because EXITPASS_RUN_RABBITMQ_BROKER_TESTS is not true.");
            }

            var missingNames = RequiredEnvironmentVariableNames
                .Where(name => string.IsNullOrWhiteSpace(variables.GetValueOrDefault(name)))
                .ToArray();

            if (missingNames.Length > 0)
            {
                return Skip("RabbitMQ broker integration test skipped because required environment variables are missing: " + string.Join(", ", missingNames) + ".");
            }

            if (!int.TryParse(variables["RABBITMQ__PORT"], out var port))
            {
                return Skip("RabbitMQ broker integration test skipped because RABBITMQ__PORT must be an integer.");
            }

            return new RabbitMqBrokerTestGate(
                shouldRun: true,
                skipReason: string.Empty,
                settings: new RabbitMqBrokerSettings(
                    variables["RABBITMQ__HOST"]!,
                    port,
                    variables["RABBITMQ__USERNAME"]!,
                    variables["RABBITMQ__PASSWORD"]!,
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__VHOST"), "/"),
                    DefaultIfBlank(variables.GetValueOrDefault("RABBITMQ__EXCHANGE"), DefaultExchangeName)));
        }

        private static RabbitMqBrokerTestGate Skip(string reason)
        {
            return new RabbitMqBrokerTestGate(
                shouldRun: false,
                skipReason: reason,
                settings: new RabbitMqBrokerSettings(string.Empty, 5672, string.Empty, string.Empty, "/", DefaultExchangeName));
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
}
