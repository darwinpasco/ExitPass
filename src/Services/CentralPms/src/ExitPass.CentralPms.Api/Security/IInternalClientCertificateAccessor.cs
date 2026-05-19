using System.Security.Cryptography.X509Certificates;

namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Reads the client certificate presented by an internal service caller.
/// </summary>
public interface IInternalClientCertificateAccessor
{
    /// <summary>
    /// Gets the client certificate for the current request, when one was provided by the transport.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>The client certificate, or <c>null</c> when the request did not present one.</returns>
    Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context);
}
