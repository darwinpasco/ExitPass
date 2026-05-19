using ExitPass.CentralPms.Application.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// Registers Central PMS integration event publishing infrastructure.
/// </summary>
public static class CentralPmsEventPublishingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the configured integration event publisher for Central PMS.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddCentralPmsEventPublishing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = RabbitMqIntegrationEventPublisherOptions.FromConfiguration(configuration);

        if (options.IsConfigured)
        {
            services.AddSingleton(options);
            services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();
        }
        else
        {
            services.AddSingleton<IIntegrationEventPublisher, DisabledIntegrationEventPublisher>();
        }

        return services;
    }
}
