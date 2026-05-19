using Microsoft.Extensions.Configuration;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

internal sealed class RabbitMqIntegrationEventPublisherOptions
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 5672;

    public string Username { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string VirtualHost { get; init; } = "/";

    public string ExchangeName { get; init; } = "exitpass.central-pms.events";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public static RabbitMqIntegrationEventPublisherOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Messaging:RabbitMq");

        return new RabbitMqIntegrationEventPublisherOptions
        {
            Host = section["Host"] ?? string.Empty,
            Port = int.TryParse(section["Port"], out var port) ? port : 5672,
            Username = section["Username"] ?? "guest",
            Password = section["Password"] ?? "guest",
            VirtualHost = section["VirtualHost"] ?? "/",
            ExchangeName = section["ExchangeName"] ?? "exitpass.central-pms.events"
        };
    }
}
