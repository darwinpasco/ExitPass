using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using System.Security.Cryptography;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
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
    /// <param name="repository">Canonical service-principal identity and scope repository.</param>
    /// <returns>A task representing middleware execution.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<InternalMtlsOptions> options,
        IInternalClientCertificateAccessor certificateAccessor,
        ICentralPmsRbacRepository repository)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateAccessor);
        ArgumentNullException.ThrowIfNull(repository);

        var endpoint = context.GetEndpoint();
        var isInternalEndpoint = endpoint?.Metadata.GetMetadata<InternalServiceEndpointMetadata>() is not null;
        var acceptsServicePrincipal = endpoint?.Metadata.GetMetadata<ServicePrincipalEndpointMetadata>() is not null;
        var acceptsGovernedAptDevice = endpoint?.Metadata.GetMetadata<GovernedAptDeviceEndpointMetadata>() is not null;
        var mtlsOptions = options.Value;

        if (!isInternalEndpoint && !acceptsServicePrincipal && !acceptsGovernedAptDevice)
        {
            await _next(context);
            return;
        }

        if (isInternalEndpoint && !acceptsServicePrincipal && !mtlsOptions.Enabled)
        {
            await _next(context);
            return;
        }

        var certificate = await certificateAccessor.GetClientCertificateAsync(context);

        if (acceptsServicePrincipal && certificate is null)
        {
            // This is a shared endpoint. A valid H-006 session may still be admitted by the human
            // authentication path; unauthenticated header-only callers fail later at RBAC admission.
            await _next(context);
            return;
        }

        if (!mtlsOptions.Enabled)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "SERVICE_CREDENTIAL_VALIDATION_UNAVAILABLE",
                "Service credential authentication is not configured.");
            return;
        }

        if (certificate is null)
        {
            if (mtlsOptions.RequireClientCertificate)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "INTERNAL_CLIENT_CERTIFICATE_REQUIRED",
                    "A trusted internal client certificate is required.");
                return;
            }

            await _next(context);
            return;
        }

        var staticallyTrusted = IsTrusted(certificate, mtlsOptions.TrustedClientThumbprints);
        if (acceptsGovernedAptDevice)
        {
            if (await TryAuthenticateGovernedAptDeviceAsync(context, certificate, repository))
            {
                await _next(context);
                return;
            }

            if (staticallyTrusted)
            {
                var suppliedIdentity = HumanSessionAuthenticationHandler.ResolveGuid(
                    context.Request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName]);
                var governedRequired = suppliedIdentity.HasValue
                    ? await repository.RequiresGovernedDeviceCertificateAsync(suppliedIdentity.Value, context.RequestAborted)
                    : null;
                if (governedRequired is not true)
                {
                    await _next(context);
                    return;
                }
            }

            await WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "INTERNAL_CLIENT_CERTIFICATE_UNTRUSTED",
                "The internal client certificate is not trusted.");
            return;
        }

        if (!staticallyTrusted)
        {
            await WriteErrorAsync(
                context,
                acceptsServicePrincipal ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden,
                "INTERNAL_CLIENT_CERTIFICATE_UNTRUSTED",
                "The internal client certificate is not trusted.");
            return;
        }

        if (acceptsServicePrincipal &&
            !await TryAuthenticateServicePrincipalAsync(context, certificate, mtlsOptions, repository))
        {
            return;
        }

        await _next(context);
    }

    private static async Task<bool> TryAuthenticateGovernedAptDeviceAsync(
        HttpContext context,
        X509Certificate2 certificate,
        ICentralPmsRbacRepository repository)
    {
        var now = DateTimeOffset.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            return false;

        var fingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var device = await repository.GetGovernedDeviceAuthenticationAsync(
            $"device-mtls-sha256:{fingerprint}", context.RequestAborted);
        if (device is null || device.DeviceType != "ASSISTED_PAYMENT_TERMINAL") return false;

        var claims = new List<Claim>
        {
            new("service_identity_id", device.ServiceIdentityId.ToString("D")),
            new("client_id", device.ServiceIdentityId.ToString("D")),
            new("device_id", device.DeviceId.ToString("D")),
            new("device_type", device.DeviceType),
            new("device_credential_version", device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("exitpass_audience", "APT"),
            new("source_channel", StatutoryDiscountSourceChannels.AssistedPaymentTerminal),
            new("source_application", device.OwningServiceName),
            new("credential_type", "MTLS")
        };
        claims.AddRange(device.SiteIds.Select(siteId => new Claim("site_id", siteId.ToString("D"))));
        claims.AddRange(device.SiteGroupIds.Select(siteGroupId => new Claim("site_group_id", siteGroupId.ToString("D"))));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "GovernedDeviceMtls",
            nameType: "service_identity_id",
            roleType: ClaimTypes.Role));
        return true;
    }

    private static async Task<bool> TryAuthenticateServicePrincipalAsync(
        HttpContext context,
        X509Certificate2 certificate,
        InternalMtlsOptions options,
        ICentralPmsRbacRepository repository)
    {
        var now = DateTimeOffset.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "SERVICE_CREDENTIAL_EXPIRED", "The service credential is not current.");
            return false;
        }

        var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
        var bindings = options.ServicePrincipalCredentials
            .Where(binding => string.Equals(
                NormalizeThumbprint(binding.CertificateThumbprint),
                thumbprint,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (bindings.Length != 1 || string.IsNullOrWhiteSpace(bindings[0].CredentialReference))
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "SERVICE_CREDENTIAL_UNKNOWN", "The service credential is not registered.");
            return false;
        }

        var binding = bindings[0];
        var principalRecord = await repository.GetServicePrincipalAuthenticationAsync(
            binding.CredentialReference,
            context.RequestAborted);

        if (principalRecord is null ||
            !string.Equals(principalRecord.CredentialType, "MTLS_CERTIFICATE_REFERENCE", StringComparison.Ordinal))
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "SERVICE_CREDENTIAL_UNKNOWN", "The service credential is not registered.");
            return false;
        }

        if (!principalRecord.Effective || !principalRecord.CredentialCurrent)
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "SERVICE_CREDENTIAL_EXPIRED_OR_REVOKED", "The service credential is not current.");
            return false;
        }

        if (!string.Equals(principalRecord.IdentityStatus, "ACTIVE", StringComparison.Ordinal))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                "SERVICE_PRINCIPAL_DISABLED", "The service principal is not enabled.");
            return false;
        }

        if (principalRecord.IdentityType is not ("INTERNAL_SERVICE" or "ADAPTER" or "GATEWAY") ||
            !IsCompatibleSource(binding.SourceChannel, principalRecord.OwningServiceName))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                "SERVICE_PRINCIPAL_SOURCE_CHANNEL_DENIED", "The service principal is not authorized for this source channel.");
            return false;
        }

        var claims = new List<Claim>
        {
            new("service_identity_id", principalRecord.ServiceIdentityId.ToString("D")),
            new("client_id", principalRecord.ServiceIdentityId.ToString("D")),
            new("exitpass_audience", binding.Audience.Trim()),
            new("source_channel", StatutoryDiscountSourceChannels.Normalize(binding.SourceChannel)),
            new("source_application", principalRecord.OwningServiceName),
            new("credential_type", "MTLS")
        };

        claims.AddRange(binding.Permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .Select(permission => new Claim(CentralPmsRbacPolicyCatalog.PermissionClaimType, permission.Trim())));
        claims.AddRange(principalRecord.SiteIds.Select(siteId => new Claim("site_id", siteId.ToString("D"))));
        claims.AddRange(principalRecord.SiteGroupIds.Select(siteGroupId => new Claim("site_group_id", siteGroupId.ToString("D"))));

        var serviceIdentity = new ClaimsIdentity(
            claims,
            authenticationType: "InternalMtlsServicePrincipal",
            nameType: "service_identity_id",
            roleType: ClaimTypes.Role);

        var sourceChannel = StatutoryDiscountSourceChannels.Normalize(binding.SourceChannel);
        var humanIdentities = context.User.Identities.Where(identity =>
            identity.IsAuthenticated &&
            string.Equals(identity.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal)).ToArray();
        if (humanIdentities.Length > 1)
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                "HUMAN_SESSION_IDENTITY_AMBIGUOUS",
                "The authenticated human-session identity is ambiguous.");
            return false;
        }

        var humanIdentity = humanIdentities.SingleOrDefault();

        if (string.Equals(sourceChannel, StatutoryDiscountSourceChannels.WebPay, StringComparison.Ordinal) &&
            humanIdentity is not null)
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                "WEBPAY_HUMAN_SESSION_PROHIBITED",
                "WebPay service calls cannot carry human-session cashier attribution.");
            return false;
        }

        if (string.Equals(sourceChannel, StatutoryDiscountSourceChannels.AssistedPaymentTerminal, StringComparison.Ordinal) &&
            humanIdentity is not null)
        {
            var humanAudience = humanIdentity.FindFirst("exitpass_audience")?.Value;
            var humanDeviceServiceIdentityId = HumanSessionAuthenticationHandler.ResolveGuid(
                humanIdentity.FindFirst("device_service_identity_id")?.Value);
            if (!string.Equals(humanAudience, "APT", StringComparison.Ordinal) ||
                humanDeviceServiceIdentityId != principalRecord.ServiceIdentityId)
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                    "APT_HUMAN_DEVICE_BINDING_MISMATCH",
                    "The authenticated APT human session is not bound to the authenticated terminal identity.");
                return false;
            }

            context.User = new ClaimsPrincipal([serviceIdentity, humanIdentity]);
            return true;
        }

        context.User = new ClaimsPrincipal(serviceIdentity);
        return true;
    }

    private static bool IsCompatibleSource(string configuredSourceChannel, string owningServiceName)
    {
        var sourceChannel = StatutoryDiscountSourceChannels.Normalize(configuredSourceChannel);
        return sourceChannel switch
        {
            StatutoryDiscountSourceChannels.WebPay =>
                owningServiceName.Equals("WEBPAY", StringComparison.OrdinalIgnoreCase) ||
                owningServiceName.Equals("PaymentOrchestrator", StringComparison.OrdinalIgnoreCase) ||
                owningServiceName.Equals("PAYMENT_ORCHESTRATOR", StringComparison.OrdinalIgnoreCase),
            StatutoryDiscountSourceChannels.AssistedPaymentTerminal =>
                owningServiceName.Equals("APT", StringComparison.OrdinalIgnoreCase) ||
                owningServiceName.Equals("ASSISTED_PAYMENT_TERMINAL", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(context.Request),
            Retryable = false
        });
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
