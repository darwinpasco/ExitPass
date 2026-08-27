namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Extension methods for marking Central PMS endpoint groups as internal service endpoints.
/// </summary>
public static class InternalServiceEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Marks the endpoint builder as internal service-to-service traffic.
    /// </summary>
    /// <typeparam name="TBuilder">Endpoint convention builder type.</typeparam>
    /// <param name="builder">Endpoint convention builder.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static TBuilder RequireInternalServiceMtls<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new InternalServiceEndpointMetadata());
        return builder;
    }

    /// <summary>
    /// Marks a shared endpoint as admitting verified mTLS service principals in addition to its
    /// independently authenticated human audience.
    /// </summary>
    /// <typeparam name="TBuilder">Endpoint convention builder type.</typeparam>
    /// <param name="builder">Endpoint convention builder.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static TBuilder AcceptAuthenticatedServicePrincipal<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new ServicePrincipalEndpointMetadata());
        return builder;
    }
}
