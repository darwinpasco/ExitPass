using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.Auditing;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorParking.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

/// <summary>
/// Registers the Central PMS Vendor PMS Adapter selection path.
/// </summary>
public static class CentralPmsVendorPmsAdapterRegistration
{
    /// <summary>
    /// Adds the configured provider-neutral Vendor PMS parking resolution client.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured adapter is unsupported or invalid.</exception>
    public static IServiceCollection AddCentralPmsVendorPmsAdapter(
        this IServiceCollection services,
        IConfiguration configuration,
        string mainDatabaseConnectionString,
        string runtimeEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CentralPmsVendorPmsAdapterOptions
        {
            Provider = configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:Provider"]
                ?? string.Empty,
            Environment = configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:Environment"] ?? string.Empty,
            CentralPmsServiceIdentityId = Guid.TryParse(
                configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:CentralPmsServiceIdentityId"], out var serviceId)
                    ? serviceId : Guid.Empty,
            AdapterSecretMountRoot = configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:AdapterSecretMountRoot"] ?? string.Empty,
            AllowTaskOwnedHttp = configuration.GetValue<bool>(
                $"{CentralPmsVendorPmsAdapterOptions.SectionName}:AllowTaskOwnedHttp")
        };

        return options.NormalizedProvider() switch
        {
            CentralPmsVendorPmsAdapterOptions.MockProvider when IsTestOnlyEnvironment(runtimeEnvironment) =>
                AddMockVendorPmsAdapter(services),
            CentralPmsVendorPmsAdapterOptions.MockProvider => throw new InvalidOperationException(
                "MOCK_VENDOR_PMS_PROVIDER_NOT_ALLOWED_IN_THIS_ENVIRONMENT"),
            CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider => AddSiteVendorPmsAdapter(
                services, mainDatabaseConnectionString, options),
            _ => throw new InvalidOperationException(
                $"Unsupported Central PMS Vendor PMS adapter provider '{options.Provider}'.")
        };
    }

    private static bool IsTestOnlyEnvironment(string value) =>
        value.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("IntegrationTest", StringComparison.OrdinalIgnoreCase);

    private static IServiceCollection AddMockVendorPmsAdapter(IServiceCollection services)
    {
        services.AddScoped<IVendorPmsParkingResolutionClient, MockVendorPmsParkingResolutionClient>();
        services.AddScoped<IVendorSessionProjectionSyncService, DisabledVendorSessionProjectionSyncService>();
        return services;
    }

    private static IServiceCollection AddSiteVendorPmsAdapter(
        IServiceCollection services, string connectionString, CentralPmsVendorPmsAdapterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Environment) || options.CentralPmsServiceIdentityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(options.AdapterSecretMountRoot))
            throw new InvalidOperationException("SITE_ADAPTER_ROUTING_CONFIGURATION_INVALID");
        services.AddSingleton<ISiteVendorAdapterRouteRegistry>(
            new PostgresSiteVendorAdapterRouteRegistry(
                connectionString, options.Environment, options.CentralPmsServiceIdentityId));
        services.AddSingleton<ISiteAdapterCredentialResolver>(
            new MountedFileSiteAdapterCredentialResolver(options.AdapterSecretMountRoot));
        services.AddHttpClient(nameof(SiteVendorAdapterHttpClient), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IVendorPmsParkingResolutionClient>(serviceProvider =>
            new SiteVendorAdapterHttpClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SiteVendorAdapterHttpClient)),
                serviceProvider.GetRequiredService<ISiteVendorAdapterRouteRegistry>(),
                serviceProvider.GetRequiredService<ISiteAdapterCredentialResolver>(),
                options.CentralPmsServiceIdentityId,
                options.AllowTaskOwnedHttp));
        services.AddScoped<IVendorSessionProjectionSyncService>(serviceProvider =>
            new SiteVendorAdapterProjectionSyncService(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SiteVendorAdapterHttpClient)),
                serviceProvider.GetRequiredService<ISiteVendorAdapterRouteRegistry>(),
                serviceProvider.GetRequiredService<ISiteAdapterCredentialResolver>(),
                serviceProvider.GetRequiredService<IVendorSessionProjectionRepository>(),
                serviceProvider.GetRequiredService<ExitPass.CentralPms.Domain.Common.ISystemClock>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SiteVendorAdapterProjectionSyncService>>(),
                options.CentralPmsServiceIdentityId,
                options.AllowTaskOwnedHttp,
                serviceProvider.GetRequiredService<IAuditEventPublisher>()));
        return services;
    }
}
