using Microsoft.Extensions.Configuration;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// RabbitMQ settings for publishing already-persisted reconciliation outbox events.
/// </summary>
public sealed class RabbitMqReconciliationOutboxPublisherOptions
{
    /// <summary>
    /// Enables RabbitMQ publishing for reconciliation outbox dispatch.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// RabbitMQ host name.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// RabbitMQ port.
    /// </summary>
    public int Port { get; init; } = 5672;

    /// <summary>
    /// RabbitMQ username.
    /// </summary>
    public string Username { get; init; } = "guest";

    /// <summary>
    /// RabbitMQ password.
    /// </summary>
    public string Password { get; init; } = "guest";

    /// <summary>
    /// RabbitMQ virtual host.
    /// </summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>
    /// Default RabbitMQ exchange name when the outbox row does not provide one.
    /// </summary>
    public string ExchangeName { get; init; } = "exitpass.central-pms";

    /// <summary>
    /// Optional fixed routing key override.
    /// </summary>
    public string? RoutingKeyOverride { get; init; }

    /// <summary>
    /// Routing key prefix used when the outbox row does not provide a routing key.
    /// </summary>
    public string RoutingKeyPrefix { get; init; } = "central-pms.reconciliation";

    /// <summary>
    /// Publish confirmation timeout.
    /// </summary>
    public TimeSpan PublishConfirmTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether the RabbitMQ publisher has enough configuration to be used.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Builds options from configuration.
    /// </summary>
    public static RabbitMqReconciliationOutboxPublisherOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var common = configuration.GetSection("Messaging:RabbitMq");
        var section = common.GetSection("ReconciliationOutbox");

        return new RabbitMqReconciliationOutboxPublisherOptions
        {
            Enabled = ParseBool(section["Enabled"]),
            Host = FirstNonBlank(section["Host"], common["Host"], string.Empty),
            Port = ParseInt(section["Port"], common["Port"], 5672),
            Username = FirstNonBlank(section["Username"], common["Username"], "guest"),
            Password = FirstNonBlank(section["Password"], common["Password"], "guest"),
            VirtualHost = FirstNonBlank(section["VirtualHost"], common["VirtualHost"], "/"),
            ExchangeName = FirstNonBlank(section["ExchangeName"], common["ExchangeName"], "exitpass.central-pms"),
            RoutingKeyOverride = BlankToNull(section["RoutingKey"]),
            RoutingKeyPrefix = FirstNonBlank(section["RoutingKeyPrefix"], "central-pms.reconciliation"),
            PublishConfirmTimeout = TimeSpan.FromSeconds(ParseInt(section["PublishConfirmTimeoutSeconds"], "5", 5))
        };
    }

    private static bool ParseBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string? first, string? second, int defaultValue)
    {
        if (int.TryParse(first, out var parsedFirst))
        {
            return parsedFirst;
        }

        if (int.TryParse(second, out var parsedSecond))
        {
            return parsedSecond;
        }

        return defaultValue;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
