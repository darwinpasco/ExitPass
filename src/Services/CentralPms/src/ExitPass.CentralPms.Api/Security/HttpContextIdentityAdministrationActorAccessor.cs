using System.Security.Claims;
using ExitPass.CentralPms.Application.ManagementPlatform;

namespace ExitPass.CentralPms.Api.Security;

public sealed class HttpContextIdentityAdministrationActorAccessor : IIdentityAdministrationActorAccessor
{
    public const string HumanSessionIdClaimType = HumanSessionAuthenticationHandler.InternalHumanSessionIdClaimType;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextIdentityAdministrationActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public IdentityAdministrationActor? Current
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            var sessionValue = principal.FindFirst(HumanSessionIdClaimType)?.Value;

            return Guid.TryParse(userValue, out var userId) && userId != Guid.Empty &&
                   Guid.TryParse(sessionValue, out var sessionId) && sessionId != Guid.Empty
                ? new IdentityAdministrationActor(userId, sessionId)
                : null;
        }
    }
}
