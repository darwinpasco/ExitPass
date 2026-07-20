using ExitPass.CentralPms.Application.Gates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Registers the disabled-by-default live HikCentral gate integration chain.
/// </summary>
public static class HikCentralGateIntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the live HikCentral gate adapter chain only when explicitly enabled and fully configured.
    /// </summary>
    public static IServiceCollection AddHikCentralGateIntegration(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttpClientBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = ReadOptions(configuration);
        return services.AddHikCentralGateIntegration(options, configureHttpClientBuilder);
    }

    /// <summary>
    /// Adds the live HikCentral gate adapter chain only when explicitly enabled and fully configured.
    /// </summary>
    public static IServiceCollection AddHikCentralGateIntegration(
        this IServiceCollection services,
        HikCentralGateIntegrationOptions options,
        Action<IHttpClientBuilder>? configureHttpClientBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);

        if (!options.Enabled)
        {
            return services;
        }

        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid {HikCentralGateIntegrationOptions.SectionName} configuration: {string.Join(", ", validationErrors)}.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options.ToRuntimeOptions());
        services.AddSingleton(options.ToSecretFileOptions());
        services.AddSingleton(options.ToHttpTransportOptions());

        services.AddScoped<IHikCentralGateSecretSource, MountedFileHikCentralGateSecretSource>();
        services.AddSingleton<IHikCentralNonceGenerator, CryptographicHikCentralNonceGenerator>();
        services.AddScoped<IHikCentralGateRuntimeMaterialProvider, HikCentralGateRuntimeMaterialProvider>();
        services.AddScoped<IHikCentralGateActionRequestPlanBuilder, HikCentralGateActionRequestPlanBuilder>();
        services.AddScoped<IHikCentralRequestSigningMaterialBuilder, HikCentralRequestSigningMaterialBuilder>();
        services.AddScoped<IHikCentralRequestSignatureCalculator, HikCentralRequestSignatureCalculator>();
        services.AddScoped<IHikCentralSignedHttpRequestBuilder, HikCentralSignedHttpRequestBuilder>();
        services.AddScoped<IHikCentralGateActionAdapter, HikCentralGateActionAdapter>();

        var httpClientBuilder = services.AddHttpClient<IHikCentralHttpTransport, HikCentralHttpTransport>(
            (serviceProvider, httpClient) =>
            {
                var integrationOptions = serviceProvider.GetRequiredService<HikCentralGateIntegrationOptions>();
                httpClient.Timeout = integrationOptions.EffectiveHttpTimeout();
            });

        configureHttpClientBuilder?.Invoke(httpClientBuilder);

        return services;
    }

    private static HikCentralGateIntegrationOptions ReadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(HikCentralGateIntegrationOptions.SectionName);
        var options = new HikCentralGateIntegrationOptions();
        section.Bind(options);
        return options;
    }
}
