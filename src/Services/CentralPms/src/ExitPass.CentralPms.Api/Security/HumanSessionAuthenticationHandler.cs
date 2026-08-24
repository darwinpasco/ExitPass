using System.Security.Claims;
using System.Text.Encodings.Web;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Security;

public sealed class HumanSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ExitPassHumanSession";
    public const string InternalHumanSessionIdClaimType = "exitpass_human_session_id";
    private readonly IHumanAuthenticationService _service;
    private readonly IHumanSessionTokenService _tokens;
    private readonly HumanAuthenticationOptions _humanOptions;

    public HumanSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHumanAuthenticationService service,
        IHumanSessionTokenService tokens,
        IOptions<HumanAuthenticationOptions> humanOptions)
        : base(options, logger, encoder)
    {
        _service = service;
        _tokens = tokens;
        _humanOptions = humanOptions.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ResolveToken(Request, _humanOptions);
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();
        var correlationId = ResolveCorrelationId(Request);
        var deviceServiceIdentityId = ResolveGuid(Request.Headers["X-ExitPass-Service-Identity-Id"]);
        var expectedAudience = ResolveExpectedAudience(Request.Path);
        var context = BuildContext(Request, correlationId, deviceServiceIdentityId, _humanOptions, _tokens);
        var result = await _service.ResolveSessionAsync(token, expectedAudience, deviceServiceIdentityId, context, true, Context.RequestAborted);
        if (!result.Response.Authenticated || result.Response.Session is null)
        {
            return AuthenticateResult.Fail(result.Response.ErrorCode ?? "Human session is invalid.");
        }

        var session = result.Response.Session;
        var principal = CreatePrincipal(session, result.InternalHumanSessionId);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    public static ClaimsPrincipal CreatePrincipal(HumanSessionDto session, Guid? internalHumanSessionId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserReference.ToString("D")),
            new(ClaimTypes.Name, session.Username),
            new("user_id", session.UserReference.ToString("D")),
            new("human_session_id", session.SessionReference.ToString("D")),
            new("exitpass_audience", session.Audience),
            new("authentication_assurance", session.Assurance),
            new("privileged_account", session.PrivilegedAccount ? "true" : "false"),
            new("password_change_required", session.PasswordChangeRequired ? "true" : "false"),
            new("mfa_satisfied", session.MfaSatisfied ? "true" : "false")
        };
        if (session.DeviceServiceIdentityReference.HasValue)
        {
            claims.Add(new Claim("device_service_identity_id", session.DeviceServiceIdentityReference.Value.ToString("D")));
        }
        if (session.OperatorDeviceBindingReference.HasValue)
        {
            claims.Add(new Claim("operator_device_binding_id", session.OperatorDeviceBindingReference.Value.ToString("D")));
        }
        if (session.OperatorShiftReference.HasValue)
        {
            claims.Add(new Claim("operator_shift_id", session.OperatorShiftReference.Value.ToString("D")));
        }
        if (session.EffectiveSiteReference.HasValue)
        {
            claims.Add(new Claim("operator_effective_site_id", session.EffectiveSiteReference.Value.ToString("D")));
        }
        if (session.EffectiveSiteGroupReference.HasValue)
        {
            claims.Add(new Claim("operator_effective_site_group_id", session.EffectiveSiteGroupReference.Value.ToString("D")));
        }
        if (session.AuthorizationEpoch.HasValue)
        {
            claims.Add(new Claim("authorization_epoch", session.AuthorizationEpoch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (session.CredentialVersion.HasValue)
        {
            claims.Add(new Claim("credential_version", session.CredentialVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (internalHumanSessionId.HasValue)
        {
            claims.Add(new Claim(InternalHumanSessionIdClaimType, internalHumanSessionId.Value.ToString("D")));
        }
        foreach (var permission in session.Permissions)
        {
            claims.Add(new Claim(CentralPmsRbacPolicyCatalog.PermissionClaimType, permission));
        }
        foreach (var siteReference in session.SiteReferences)
        {
            claims.Add(new Claim("site_id", siteReference.ToString("D")));
        }
        foreach (var siteGroupReference in session.SiteGroupReferences)
        {
            claims.Add(new Claim("site_group_id", siteGroupReference.ToString("D")));
        }
        claims.Add(new Claim("has_global_scope", session.HasGlobalScope ? "true" : "false"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    }

    public static string? ResolveToken(HttpRequest request, HumanAuthenticationOptions options)
    {
        if (request.Cookies.TryGetValue(options.CookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie)) return cookie;
        if (request.Headers.TryGetValue("Authorization", out var authorization))
        {
            var value = authorization.ToString();
            var prefix = options.AptSessionAuthorizationScheme + " ";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value[prefix.Length..].Trim();
        }
        return null;
    }

    public static HumanAuthenticationContext BuildContext(HttpRequest request, Guid correlationId, Guid? deviceServiceIdentityId, HumanAuthenticationOptions options, IHumanSessionTokenService tokens) =>
        new(correlationId, options.CentralPmsServiceIdentityId,
            string.IsNullOrWhiteSpace(request.HttpContext.Connection.RemoteIpAddress?.ToString()) ? null : tokens.HashPrivacyValue(request.HttpContext.Connection.RemoteIpAddress!.ToString()),
            string.IsNullOrWhiteSpace(request.Headers.UserAgent.ToString()) ? null : tokens.HashPrivacyValue(request.Headers.UserAgent.ToString()),
            deviceServiceIdentityId, null);

    public static Guid ResolveCorrelationId(HttpRequest request) =>
        Guid.TryParse(request.Headers["X-Correlation-Id"], out var value) && value != Guid.Empty ? value : Guid.NewGuid();

    public static Guid? ResolveGuid(string? value) => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static string? ResolveExpectedAudience(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/v1/apt/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/v1/terminal-cash-payments/", StringComparison.OrdinalIgnoreCase)) return HumanSessionAudiences.Apt;
        if (value.StartsWith("/v1/operator-console/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/v1/ops/operator-console/", StringComparison.OrdinalIgnoreCase)) return HumanSessionAudiences.OperatorConsole;
        if (value.StartsWith("/v1/management-platform/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/v1/ops/management-platform/", StringComparison.OrdinalIgnoreCase)) return HumanSessionAudiences.ManagementPlatform;
        return null;
    }
}
