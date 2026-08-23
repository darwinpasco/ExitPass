namespace ExitPass.AuditEventService.Api.Configuration;

public sealed class AuditEventServiceOptions
{
    public const string SectionName = "AuditEventService";
    public Guid ServiceIdentityId { get; set; }
    public string SourceServiceName { get; set; } = string.Empty;
    public string SecretMountRoot { get; set; } = string.Empty;
    public string ApiKeyFile { get; set; } = string.Empty;
    public string[] AllowedOperations { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ServiceIdentityId == Guid.Empty || string.IsNullOrWhiteSpace(SourceServiceName))
            errors.Add("AUDIT_SERVICE_IDENTITY_INVALID");
        if (!ReadableSecretFile(ApiKeyFile, SecretMountRoot))
            errors.Add("AUDIT_SERVICE_SECRET_REFERENCE_INVALID");
        if (AllowedOperations.Length == 0 || AllowedOperations.Any(operation =>
                !AuditEventOperations.All.Contains(operation, StringComparer.Ordinal)))
            errors.Add("AUDIT_SERVICE_PERMISSION_CONFIGURATION_INVALID");
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public bool Allows(string operation) => AllowedOperations.Contains(operation, StringComparer.Ordinal);

    public string ReadApiKey()
    {
        if (!ReadableSecretFile(ApiKeyFile, SecretMountRoot))
            throw new InvalidOperationException("AUDIT_SERVICE_SECRET_REFERENCE_INVALID");
        var value = File.ReadAllText(ApiKeyFile).Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("AUDIT_SERVICE_SECRET_REFERENCE_INVALID")
            : value;
    }

    private static bool ReadableSecretFile(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root) ||
            !Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(root) || !File.Exists(path))
            return false;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public static class AuditEventOperations
{
    public const string Append = "AUDIT_EVENT_APPEND";
    public const string Read = "AUDIT_EVENT_READ";
    public static readonly string[] All = [Append, Read];
}
