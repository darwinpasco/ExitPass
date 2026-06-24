using System.Text.RegularExpressions;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

internal static partial class WebPayDisplayNameSanitizer
{
    public const string GenericSiteGroupName = "Parking Group";
    public const string GenericSiteName = "Parking Site";

    public static string ResolveSiteGroupName(string? value) =>
        FirstFriendly(value) ?? GenericSiteGroupName;

    public static string ResolveSiteName(string? value) =>
        FirstFriendly(value) ?? GenericSiteName;

    public static bool IsFallbackLooking(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return false;
        }

        if (Guid.TryParse(normalized, out _))
        {
            return true;
        }

        if (UuidWithoutDashesRegex().IsMatch(normalized))
        {
            return true;
        }

        var lowered = normalized.ToLowerInvariant();
        foreach (var prefix in new[] { "site ", "site group " })
        {
            if (lowered.StartsWith(prefix, StringComparison.Ordinal) &&
                IsFallbackLooking(normalized[prefix.Length..]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FirstFriendly(string? value)
    {
        var normalized = Normalize(value);
        return normalized is not null && !IsFallbackLooking(normalized) ? normalized : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [GeneratedRegex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidWithoutDashesRegex();
}
