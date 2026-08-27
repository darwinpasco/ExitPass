namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Marks a shared endpoint as accepting a production-authenticated internal service principal.
/// A missing certificate does not preclude an independently authenticated H-006 human caller.
/// </summary>
public sealed class ServicePrincipalEndpointMetadata
{
}
