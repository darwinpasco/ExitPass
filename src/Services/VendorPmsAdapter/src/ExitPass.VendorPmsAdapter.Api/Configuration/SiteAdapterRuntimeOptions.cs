namespace ExitPass.VendorPmsAdapter.Api.Configuration;

/// <summary>Fail-closed runtime configuration for one on-premises Site Integration Adapter.</summary>
public sealed class SiteAdapterRuntimeOptions
{
    public const string SectionName = "SiteAdapter";
    public bool Activated { get; set; }
    public Guid SiteId { get; set; }
    public Guid SiteGroupId { get; set; }
    public Guid VendorSystemId { get; set; }
    public Guid AdapterIdentityId { get; set; }
    public Guid AllowedCentralPmsServiceIdentityId { get; set; }
    public string? AdapterEndpointIdentity { get; set; }
    public string? Environment { get; set; }
    public string? ParkingLotIndexCode { get; set; }
    public string? HikCentralBaseUrl { get; set; }
    public string? SecretMountRoot { get; set; }
    public string? HikCentralAppKeyFile { get; set; }
    public string? HikCentralAppSecretFile { get; set; }
    public string? HikCentralUserId { get; set; }
    public string HikCentralApiVersion { get; set; } = "3.1.0";
    public string RequestTimeZoneId { get; set; } = "Asia/Manila";
    public string? CentralPmsApiKeyFile { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; }
    public long MaxRequestBodyBytes { get; set; } = 262_144;
    public bool ConfirmPaymentEnabled { get; set; }
    public bool AllowTaskOwnedHttp { get; set; }

    public IReadOnlyList<string> Validate(string hostEnvironment)
    {
        var errors = new List<string>();
        if (!Activated) { errors.Add("SITE_ADAPTER_DISABLED"); return errors; }
        if (SiteId == Guid.Empty || SiteGroupId == Guid.Empty || VendorSystemId == Guid.Empty ||
            AdapterIdentityId == Guid.Empty || AllowedCentralPmsServiceIdentityId == Guid.Empty)
            errors.Add("SITE_ADAPTER_IDENTITY_INVALID");
        if (string.IsNullOrWhiteSpace(AdapterEndpointIdentity) || string.IsNullOrWhiteSpace(Environment) ||
            string.IsNullOrWhiteSpace(ParkingLotIndexCode)) errors.Add("SITE_ADAPTER_BINDING_INCOMPLETE");

        if (!Uri.TryCreate(HikCentralBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            errors.Add("HIKCENTRAL_BASE_URL_INVALID");
        else if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                 !string.IsNullOrEmpty(uri.Fragment))
            errors.Add("HIKCENTRAL_BASE_URL_INVALID");
        else if (uri.Scheme == "http" && !(AllowTaskOwnedHttp &&
                 string.Equals(hostEnvironment, "IntegrationTest", StringComparison.Ordinal)))
            errors.Add("HIKCENTRAL_HTTPS_REQUIRED");

        if (!ReadableSecretFile(HikCentralAppKeyFile, SecretMountRoot) ||
            !ReadableSecretFile(HikCentralAppSecretFile, SecretMountRoot) ||
            !ReadableSecretFile(CentralPmsApiKeyFile, SecretMountRoot))
            errors.Add("SITE_ADAPTER_SECRET_REFERENCE_INVALID");
        if (string.IsNullOrWhiteSpace(HikCentralUserId) || TimeoutSeconds is < 1 or > 120 || MaxRetries != 0 ||
            MaxRequestBodyBytes is < 1024 or > 1_048_576)
            errors.Add("SITE_ADAPTER_RUNTIME_POLICY_INVALID");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(RequestTimeZoneId); }
        catch (TimeZoneNotFoundException) { errors.Add("SITE_ADAPTER_TIME_ZONE_INVALID"); }
        catch (InvalidTimeZoneException) { errors.Add("SITE_ADAPTER_TIME_ZONE_INVALID"); }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string ReadSecret(string path, string secretMountRoot)
    {
        if (!ReadableSecretFile(path, secretMountRoot))
            throw new InvalidOperationException("SITE_ADAPTER_SECRET_REFERENCE_INVALID");
        var value = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("SITE_ADAPTER_SECRET_REFERENCE_INVALID") : value;
    }

    private static bool ReadableSecretFile(string? path, string? secretMountRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(secretMountRoot) ||
            !Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(secretMountRoot) || !File.Exists(path))
            return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(secretMountRoot));
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
