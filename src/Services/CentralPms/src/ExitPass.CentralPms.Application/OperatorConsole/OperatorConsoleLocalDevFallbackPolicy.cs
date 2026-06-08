namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Environment policy for local/dev Operator Console fallback context.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant: fallback headers are never production trust.
/// </summary>
public static class OperatorConsoleLocalDevFallbackPolicy
{
    private static readonly HashSet<string> AllowedFallbackEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Development",
        "Dev",
        "Local",
        "Test",
        "Testing",
        "Sandbox"
    };

    /// <summary>Returns true when local/dev fallback context is allowed for the supplied environment.</summary>
    public static bool IsFallbackAllowed(string? environmentName) =>
        !string.IsNullOrWhiteSpace(environmentName) &&
        AllowedFallbackEnvironments.Contains(environmentName.Trim());

    /// <summary>Returns true when fallback context must be denied for the supplied environment.</summary>
    public static bool ShouldDenyFallback(string? environmentName, bool usesLocalDevFallbackContext) =>
        usesLocalDevFallbackContext && !IsFallbackAllowed(environmentName);
}
