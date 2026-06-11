using System.Globalization;

namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Formats HikCentral parking API timestamps without fractional seconds.
/// </summary>
public static class HikCentralParkingTimeFormatter
{
    private const string ParkingTimestampFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    /// <summary>
    /// Formats a timestamp using the HikCentral parking API wire format.
    /// </summary>
    /// <param name="value">Timestamp to format.</param>
    /// <returns>Timestamp formatted as yyyy-MM-ddTHH:mm:sszzz.</returns>
    public static string Format(DateTimeOffset value) =>
        value.ToString(ParkingTimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a nullable timestamp using the HikCentral parking API wire format.
    /// </summary>
    /// <param name="value">Timestamp to format.</param>
    /// <returns>Timestamp formatted as yyyy-MM-ddTHH:mm:sszzz, or null when no timestamp was supplied.</returns>
    public static string? Format(DateTimeOffset? value) =>
        value is null ? null : Format(value.Value);
}
