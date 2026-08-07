using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Security;

public interface IHumanAuthenticationOriginValidator
{
    bool IsAllowed(HttpRequest request);
}

public sealed class HumanAuthenticationOriginValidator : IHumanAuthenticationOriginValidator
{
    private readonly HashSet<string> _allowedOrigins;

    public HumanAuthenticationOriginValidator(IOptions<HumanAuthenticationOptions> options)
    {
        _allowedOrigins = options.Value.AllowedWebOrigins
            .Select(value => value.TrimEnd('/'))
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin)) return false;
        var value = origin.ToString().TrimEnd('/');
        if (_allowedOrigins.Contains(value)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
