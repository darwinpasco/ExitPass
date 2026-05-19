using System.Security.Cryptography.X509Certificates;

namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Reads the internal caller certificate from the ASP.NET Core connection.
/// </summary>
public sealed class HttpContextInternalClientCertificateAccessor : IInternalClientCertificateAccessor
{
    /// <inheritdoc />
    public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Connection.GetClientCertificateAsync();
    }
}
