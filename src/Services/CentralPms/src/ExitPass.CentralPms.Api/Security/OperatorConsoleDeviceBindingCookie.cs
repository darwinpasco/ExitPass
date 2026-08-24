using System.Security.Claims;
using ExitPass.CentralPms.Application.OperatorConsole;

namespace ExitPass.CentralPms.Api.Security;

public static class OperatorConsoleDeviceBindingCookie
{
    public const string CookieName = "__Host-ExitPass-Operator-Device";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(CookieName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static void Issue(HttpResponse response, string proof, DateTimeOffset now)
    {
        response.Cookies.Append(CookieName, proof, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = now.AddHours(12)
        });
    }

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });

    public static void AddClaims(ClaimsPrincipal principal, OperatorConsoleOperatingContext context)
    {
        if (principal.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        Add(identity, "operator_device_binding_id", context.OperatorDeviceBindingId);
        Add(identity, "operator_shift_id", context.OperatorShiftId);
        Add(identity, "operator_effective_site_id", context.SiteId);
        Add(identity, "operator_effective_site_group_id", context.SiteGroupId);
        Add(identity, "authorization_epoch", context.AuthorizationEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(identity, "credential_version", context.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Add(ClaimsIdentity identity, string type, Guid value) => Add(identity, type, value.ToString("D"));

    private static void Add(ClaimsIdentity identity, string type, string value)
    {
        foreach (var existing in identity.FindAll(type).ToArray())
        {
            identity.RemoveClaim(existing);
        }
        identity.AddClaim(new Claim(type, value));
    }
}
