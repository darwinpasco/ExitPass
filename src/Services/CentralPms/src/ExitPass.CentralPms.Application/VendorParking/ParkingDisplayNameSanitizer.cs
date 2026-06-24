using System.Text.RegularExpressions;

namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Normalizes parker-facing parking location display names.
/// </summary>
public static partial class ParkingDisplayNameSanitizer
{
    /// <summary>
    /// Safe generic site group fallback for parker-facing responses.
    /// </summary>
    public const string GenericSiteGroupName = "Parking Group";

    /// <summary>
    /// Safe generic site fallback for parker-facing responses.
    /// </summary>
    public const string GenericSiteName = "Parking Site";

    /// <summary>
    /// Returns the first friendly site group display name, or a safe generic fallback.
    /// </summary>
    /// <param name="configuredName">Configured Central PMS site group name.</param>
    /// <param name="vendorName">Optional vendor-resolved display name.</param>
    /// <returns>A parker-facing site group name.</returns>
    public static string ResolveSiteGroupName(string? configuredName, string? vendorName = null) =>
        FirstFriendly(configuredName, vendorName) ?? GenericSiteGroupName;

    /// <summary>
    /// Returns the first friendly site display name, or a safe generic fallback.
    /// </summary>
    /// <param name="configuredName">Configured Central PMS site name.</param>
    /// <param name="vendorName">Optional vendor-resolved display name.</param>
    /// <returns>A parker-facing site name.</returns>
    public static string ResolveSiteName(string? configuredName, string? vendorName = null) =>
        FirstFriendly(configuredName, vendorName) ?? GenericSiteName;

    /// <summary>
    /// Detects generated GUID-derived labels that must not be exposed to parkers.
    /// </summary>
    /// <param name="value">Candidate display value.</param>
    /// <returns>True when the value looks generated from a UUID.</returns>
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

    private static string? FirstFriendly(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized is not null && !IsFallbackLooking(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [GeneratedRegex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidWithoutDashesRegex();
}
