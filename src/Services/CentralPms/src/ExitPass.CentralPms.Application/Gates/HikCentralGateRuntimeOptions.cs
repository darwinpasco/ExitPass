namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Non-secret HikCentral runtime options used to assemble one gate-action runtime snapshot.
/// </summary>
public sealed class HikCentralGateRuntimeOptions
{
    public string? BaseAddress { get; set; }

    public string? ClientKeyIdentifier { get; set; }

    public string? ProfileCode { get; set; }

    public HikCentralGateControlMechanism ControlMechanism { get; set; } =
        HikCentralGateControlMechanism.AccessControlDoorControl;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateBaseAddress(errors);
        ValidateRequiredSafeValue(
            ClientKeyIdentifier,
            errors,
            "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED",
            "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_UNSAFE");
        ValidateRequiredSafeValue(
            ProfileCode,
            errors,
            "HIKCENTRAL_PROFILE_CODE_REQUIRED",
            "HIKCENTRAL_PROFILE_CODE_UNSAFE");

        if (ControlMechanism != HikCentralGateControlMechanism.AccessControlDoorControl)
        {
            errors.Add("HIKCENTRAL_CONTROL_MECHANISM_UNSUPPORTED");
        }

        return errors;
    }

    private void ValidateBaseAddress(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(BaseAddress))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_REQUIRED");
            return;
        }

        if (ContainsLineBreak(BaseAddress) ||
            !Uri.TryCreate(BaseAddress.Trim(), UriKind.Absolute, out var uri))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_INVALID");
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_HOST_REQUIRED");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_CREDENTIALS_UNSUPPORTED");
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_QUERY_UNSUPPORTED");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_FRAGMENT_UNSUPPORTED");
        }

        if (uri.AbsolutePath is not ("" or "/") ||
            uri.AbsolutePath.Contains("/../", StringComparison.Ordinal) ||
            uri.AbsolutePath.Contains("/./", StringComparison.Ordinal) ||
            uri.AbsolutePath.EndsWith("/..", StringComparison.Ordinal) ||
            uri.AbsolutePath.EndsWith("/.", StringComparison.Ordinal))
        {
            errors.Add("HIKCENTRAL_BASE_ADDRESS_PATH_UNSUPPORTED");
        }
    }

    private static void ValidateRequiredSafeValue(
        string? value,
        List<string> errors,
        string requiredCode,
        string unsafeCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(requiredCode);
            return;
        }

        if (value.Any(char.IsControl))
        {
            errors.Add(unsafeCode);
        }
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');
}
