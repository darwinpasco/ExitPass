namespace ExitPass.AuditEventService.Api.Configuration;

public sealed class AuditEventServiceOptions
{
    public const string SectionName = "AuditEventService";
    public string SecretMountRoot { get; set; } = string.Empty;
    public AuditEventCallerOptions[] Callers { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Callers.Length == 0 || Callers.Any(caller =>
                caller.ServiceIdentityId == Guid.Empty || string.IsNullOrWhiteSpace(caller.SourceServiceName)) ||
            Callers.Select(caller => caller.ServiceIdentityId).Distinct().Count() != Callers.Length)
            errors.Add("AUDIT_SERVICE_IDENTITY_INVALID");
        if (Callers.Any(caller => !ReadableSecretFile(caller.ApiKeyFile, SecretMountRoot)))
            errors.Add("AUDIT_SERVICE_SECRET_REFERENCE_INVALID");
        if (Callers.Any(caller => caller.AllowedOperations.Length == 0 || caller.AllowedOperations.Any(operation =>
                !AuditEventOperations.All.Contains(operation, StringComparer.Ordinal))))
            errors.Add("AUDIT_SERVICE_PERMISSION_CONFIGURATION_INVALID");
        if (Callers.Any(caller => caller.AllowedSiteIds.Length == 0 ||
                caller.AllowedSiteIds.Any(siteId => siteId == Guid.Empty) ||
                caller.AllowedSiteIds.Distinct().Count() != caller.AllowedSiteIds.Length))
            errors.Add("AUDIT_SERVICE_SITE_SCOPE_CONFIGURATION_INVALID");
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public AuditEventCallerOptions? FindCaller(Guid serviceIdentityId) =>
        Callers.SingleOrDefault(caller => caller.ServiceIdentityId == serviceIdentityId);

    public string ReadApiKey(AuditEventCallerOptions caller)
    {
        if (!ReadableSecretFile(caller.ApiKeyFile, SecretMountRoot))
            throw new InvalidOperationException("AUDIT_SERVICE_SECRET_REFERENCE_INVALID");
        var value = File.ReadAllText(caller.ApiKeyFile).Trim();
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

public sealed class AuditEventCallerOptions
{
    public Guid ServiceIdentityId { get; set; }
    public string SourceServiceName { get; set; } = string.Empty;
    public string ApiKeyFile { get; set; } = string.Empty;
    public string[] AllowedOperations { get; set; } = [];
    public Guid[] AllowedSiteIds { get; set; } = [];

    public bool Allows(string operation) => AllowedOperations.Contains(operation, StringComparer.Ordinal);
    public bool Allows(Guid siteId) => AllowedSiteIds.Contains(siteId);
}

public static class AuditEventOperations
{
    public const string Append = "AUDIT_EVENT_APPEND";
    public const string Read = "AUDIT_EVENT_READ";
    public static readonly string[] All = [Append, Read];
}
