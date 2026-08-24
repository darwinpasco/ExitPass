namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Enforces the PHP-only monetary boundary for Operator Console contracts.
/// </summary>
public static class OperatorConsolePhpCurrency
{
    public const string Code = "PHP";

    /// <summary>
    /// Requires exact PHP currency whenever a monetary value or currency value is present.
    /// </summary>
    public static string? RequireForAmounts(string? currencyCode, params long?[] minorUnitValues)
    {
        var hasMonetaryValue = minorUnitValues.Any(value => value.HasValue);
        if (currencyCode is null && !hasMonetaryValue)
        {
            return null;
        }

        if (!string.Equals(currencyCode, Code, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Operator Console monetary data requires currency code PHP.");
        }

        return Code;
    }
}
