namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Sanitizes HikCentral diagnostics so credentials and signatures are not exposed in logs.
/// </summary>
public static class HikCentralLogSanitizer
{
    /// <summary>
    /// Placeholder used for sensitive values.
    /// </summary>
    public const string Redacted = "[REDACTED]";

    /// <summary>
    /// Redacts the configured app secret and signatures from a diagnostic string.
    /// </summary>
    public static string Redact(
        string? message,
        string? appSecret,
        IEnumerable<string?> signatures,
        bool allowSignatureDebug = false)
    {
        var sanitized = message ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(appSecret))
        {
            sanitized = sanitized.Replace(appSecret, Redacted, StringComparison.Ordinal);
        }

        if (!allowSignatureDebug)
        {
            foreach (var signature in signatures)
            {
                if (!string.IsNullOrWhiteSpace(signature))
                {
                    sanitized = sanitized.Replace(signature, Redacted, StringComparison.Ordinal);
                }
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Returns a signature value only when explicit local debug mode is enabled.
    /// </summary>
    public static string SignatureForDiagnostics(string? signature, bool allowSignatureDebug) =>
        allowSignatureDebug && !string.IsNullOrWhiteSpace(signature)
            ? signature
            : Redacted;
}
