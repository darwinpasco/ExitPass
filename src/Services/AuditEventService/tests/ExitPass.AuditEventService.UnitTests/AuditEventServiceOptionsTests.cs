using ExitPass.AuditEventService.Api.Configuration;
using Xunit;

namespace ExitPass.AuditEventService.UnitTests;

public sealed class AuditEventServiceOptionsTests
{
    [Fact]
    public void ValidSecretReferenceAndKnownOperations_PassValidation()
    {
        using var secret = new TemporarySecret();
        var options = CreateOptions(secret);

        Assert.Empty(options.Validate());
        Assert.Equal(TemporarySecret.Value, options.ReadApiKey());
    }

    [Fact]
    public void UnknownOperation_FailsReadinessValidation()
    {
        using var secret = new TemporarySecret();
        var options = CreateOptions(secret);
        options.AllowedOperations = ["AUDIT_EVENT_DELETE"];

        Assert.Contains("AUDIT_SERVICE_PERMISSION_CONFIGURATION_INVALID", options.Validate());
    }

    [Fact]
    public void SecretOutsideConfiguredMount_FailsReadinessValidation()
    {
        using var secret = new TemporarySecret();
        var options = CreateOptions(secret);
        options.SecretMountRoot = Path.Combine(secret.Root, "different-root");

        Assert.Contains("AUDIT_SERVICE_SECRET_REFERENCE_INVALID", options.Validate());
    }

    private static AuditEventServiceOptions CreateOptions(TemporarySecret secret) => new()
    {
        ServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2"),
        SourceServiceName = "CENTRAL_PMS",
        SecretMountRoot = secret.Root,
        ApiKeyFile = secret.Path,
        AllowedOperations = [AuditEventOperations.Append, AuditEventOperations.Read]
    };

    private sealed class TemporarySecret : IDisposable
    {
        public const string Value = "unit-test-key";
        public TemporarySecret()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"exitpass-audit-options-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Path = System.IO.Path.Combine(Root, "api-key");
            File.WriteAllText(Path, Value);
        }

        public string Root { get; }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
