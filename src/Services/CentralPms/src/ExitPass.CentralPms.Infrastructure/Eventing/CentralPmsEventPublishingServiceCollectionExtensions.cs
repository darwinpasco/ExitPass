using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton<CentralPmsMetrics>();

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

    /// <summary>
    /// Adds the configured durable integration event publisher for Central PMS.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="connectionString">Main database connection string used for durable outbox persistence.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddCentralPmsEventPublishing(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = RabbitMqIntegrationEventPublisherOptions.FromConfiguration(configuration);
        services.TryAddSingleton<CentralPmsMetrics>();

        if (options.IsConfigured)
        {
            services.AddSingleton(options);
            services.AddSingleton<RabbitMqIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
                new DurableIntegrationEventPublisher(
                    connectionString,
                    serviceProvider.GetRequiredService<RabbitMqIntegrationEventPublisher>(),
                    serviceProvider.GetRequiredService<CentralPmsMetrics>()));
        }
        else
        {
            services.AddSingleton<DisabledIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
                new DurableIntegrationEventPublisher(
                    connectionString,
                    serviceProvider.GetRequiredService<DisabledIntegrationEventPublisher>(),
                    serviceProvider.GetRequiredService<CentralPmsMetrics>()));
        }

        return services;
    }

    /// <summary>
    /// Adds the configured reconciliation outbox event publisher.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddCentralPmsReconciliationOutboxPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = RabbitMqReconciliationOutboxPublisherOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        if (options.IsConfigured)
        {
            services.AddSingleton<IReconciliationOutboxEventPublisher, RabbitMqReconciliationOutboxEventPublisher>();
        }
        else
        {
            services.AddSingleton<IReconciliationOutboxEventPublisher, InProcessReconciliationOutboxEventPublisher>();
        }

        return services;
    }
}
