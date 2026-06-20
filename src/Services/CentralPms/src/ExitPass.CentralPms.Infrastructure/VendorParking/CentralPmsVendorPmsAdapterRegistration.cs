using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CentralPmsVendorPmsAdapterOptions
        {
            Provider = configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:Provider"]
                ?? CentralPmsVendorPmsAdapterOptions.MockProvider
        };

        return options.NormalizedProvider() switch
        {
            CentralPmsVendorPmsAdapterOptions.MockProvider => AddMockVendorPmsAdapter(services),
            CentralPmsVendorPmsAdapterOptions.HikCentralProvider => AddHikCentralVendorPmsAdapter(services, configuration),
            _ => throw new InvalidOperationException(
                $"Unsupported Central PMS Vendor PMS adapter provider '{options.Provider}'.")
        };
    }

    private static IServiceCollection AddMockVendorPmsAdapter(IServiceCollection services)
    {
        services.AddScoped<IVendorPmsParkingResolutionClient, MockVendorPmsParkingResolutionClient>();
        services.AddScoped<IVendorSessionProjectionSyncService, DisabledVendorSessionProjectionSyncService>();
        return services;
    }

    private static IServiceCollection AddHikCentralVendorPmsAdapter(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var hikCentralOptions = ReadHikCentralOptions(configuration);

        var validationErrors = hikCentralOptions.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid HikCentral Vendor PMS Adapter configuration: {string.Join(", ", validationErrors)}.");
        }

        services.AddSingleton(hikCentralOptions);
        services.AddSingleton<IHikCentralRequestSigner>(_ =>
            new HikCentralRequestSigner(
                new HikCentralCredentialOptions(hikCentralOptions.AppKey!, hikCentralOptions.AppSecret!)));

        services.AddSingleton<IVendorParkingDataClient>(serviceProvider =>
        {
            return new HikCentralParkingClient(
                new HttpClient
                {
                    BaseAddress = new Uri(hikCentralOptions.BaseUrl!, UriKind.Absolute),
                    Timeout = TimeSpan.FromSeconds(20)
                },
                serviceProvider.GetRequiredService<IHikCentralRequestSigner>(),
                hikCentralOptions.UserId ?? "exitpass-adapter");
        });
        services.AddSingleton<IHikCentralPassagewayRecordClient>(serviceProvider =>
            new HikCentralPassagewayRecordClient(
                new HttpClient
                {
                    BaseAddress = new Uri(hikCentralOptions.BaseUrl!, UriKind.Absolute),
                    Timeout = TimeSpan.FromSeconds(20)
                },
                serviceProvider.GetRequiredService<IHikCentralRequestSigner>(),
                hikCentralOptions.UserId ?? "exitpass-adapter",
                serviceProvider.GetService<ILogger<HikCentralPassagewayRecordClient>>()));
        services.AddSingleton<HikCentralPassagewayProjectionNormalizer>();
        services.AddScoped<IVendorSessionProjectionSyncService, HikCentralVendorSessionProjectionSyncService>();

        services.AddScoped<IVendorPmsParkingResolutionClient, HikCentralVendorPmsParkingResolutionClient>();
        return services;
    }

    private static HikCentralOptions ReadHikCentralOptions(IConfiguration configuration)
    {
        var sectionName = $"{CentralPmsVendorPmsAdapterOptions.SectionName}:HikCentral";

        return new HikCentralOptions
        {
            Enabled = true,
            BaseUrl = configuration[$"{sectionName}:BaseUrl"],
            AppKey = configuration[$"{sectionName}:AppKey"],
            AppSecret = configuration[$"{sectionName}:AppSecret"],
            UserId = configuration[$"{sectionName}:UserId"] ?? "exitpass-adapter"
        };
    }
}
