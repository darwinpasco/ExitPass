using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Enforces opt-in client certificate validation for endpoints marked as internal service traffic.
/// </summary>
public sealed class InternalMtlsMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalMtlsMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware in the request pipeline.</param>
    public InternalMtlsMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Validates the internal caller certificate when mTLS enforcement is enabled.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="options">mTLS options.</param>
    /// <param name="certificateAccessor">Client certificate accessor.</param>
    /// <returns>A task representing middleware execution.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<InternalMtlsOptions> options,
        IInternalClientCertificateAccessor certificateAccessor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateAccessor);

        var endpoint = context.GetEndpoint();
        var isInternalEndpoint = endpoint?.Metadata.GetMetadata<InternalServiceEndpointMetadata>() is not null;
        var mtlsOptions = options.Value;

        if (!isInternalEndpoint || !mtlsOptions.Enabled)
        {
            await _next(context);
            return;
        }

        var certificate = await certificateAccessor.GetClientCertificateAsync(context);

        if (certificate is null)
        {
            if (mtlsOptions.RequireClientCertificate)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    errorCode = "INTERNAL_CLIENT_CERTIFICATE_REQUIRED",
                    message = "A trusted internal client certificate is required."
                });
                return;
            }

            await _next(context);
            return;
        }

        if (!IsTrusted(certificate, mtlsOptions.TrustedClientThumbprints))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = "INTERNAL_CLIENT_CERTIFICATE_UNTRUSTED",
                message = "The internal client certificate is not trusted."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsTrusted(X509Certificate2 certificate, IEnumerable<string> trustedThumbprints)
    {
        var presentedThumbprint = NormalizeThumbprint(certificate.Thumbprint);

        return trustedThumbprints
            .Select(NormalizeThumbprint)
            .Any(thumbprint => string.Equals(thumbprint, presentedThumbprint, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeThumbprint(string? thumbprint)
    {
        return string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
    }
}
