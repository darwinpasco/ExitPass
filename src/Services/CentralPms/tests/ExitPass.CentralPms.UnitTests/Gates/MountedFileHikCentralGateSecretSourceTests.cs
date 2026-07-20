using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for the mounted-file HikCentral AppSecret source.
/// </summary>
public sealed class MountedFileHikCentralGateSecretSourceTests
{
    [Fact]
    public async Task GetSecretAsync_WithValidBoundedFile_ReturnsOwnedSecretMaterial()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("hikcentral-appsecret.bin", [1, 2, 3, 4, 5]);
        var source = CreateSource(secretPath, maxSecretBytes: 16);

        using var material = await source.GetSecretAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], material.SecretBytes.ToArray());
        Assert.DoesNotContain("1, 2, 3", material.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("placeholder-secret-not-real")]
    [InlineData(" placeholder-secret-not-real ")]
    [InlineData("placeholder-secret-not-real\n")]
    [InlineData("placeholder-secret-not-real\r\n")]
    public async Task GetSecretAsync_PreservesExactFileBytesWithoutTextConversion(string secretText)
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretBytes = Encoding.UTF8.GetBytes(secretText);
        var secretPath = directory.WriteSecret("exact-secret.bin", secretBytes);
        var source = CreateSource(secretPath, maxSecretBytes: 128);

        using var material = await source.GetSecretAsync(CancellationToken.None);

        Assert.Equal(secretBytes, material.SecretBytes.ToArray());
        Assert.Equal(secretBytes, File.ReadAllBytes(secretPath));
    }

    [Fact]
    public async Task GetSecretAsync_ReadsCurrentFileOnEveryInvocationWithoutCaching()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("rotating-secret.bin", Encoding.UTF8.GetBytes("first-placeholder-secret"));
        var source = CreateSource(secretPath, maxSecretBytes: 128);

        using var first = await source.GetSecretAsync(CancellationToken.None);
        File.WriteAllBytes(secretPath, Encoding.UTF8.GetBytes("second-placeholder-secret"));
        using var second = await source.GetSecretAsync(CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetBytes("first-placeholder-secret"), first.SecretBytes.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes("second-placeholder-secret"), second.SecretBytes.ToArray());
    }

    [Fact]
    public async Task GetSecretAsync_ReturnedMaterialOwnsSeparateBufferAndClearsOnDispose()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretBytes = Encoding.UTF8.GetBytes("owned-placeholder-secret");
        var secretPath = directory.WriteSecret("owned-secret.bin", secretBytes);
        var source = CreateSource(secretPath, maxSecretBytes: 128);

        var material = await source.GetSecretAsync(CancellationToken.None);
        File.WriteAllBytes(secretPath, Encoding.UTF8.GetBytes("changed-placeholder-secret"));

        Assert.Equal(secretBytes, material.SecretBytes.ToArray());

        material.Dispose();
        material.Dispose();

        Assert.True(material.IsDisposed);
        AssertCleared(material, "_secretBytes");
    }

    [Fact]
    public async Task GetSecretAsync_ReleasesFileHandleAndLeavesSourceFileUnchanged()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretBytes = Encoding.UTF8.GetBytes("handle-placeholder-secret");
        var secretPath = directory.WriteSecret("handle-secret.bin", secretBytes);
        var source = CreateSource(secretPath, maxSecretBytes: 128);

        using (await source.GetSecretAsync(CancellationToken.None))
        {
        }

        using var reopened = new FileStream(secretPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var reopenedBytes = new byte[secretBytes.Length];
        Assert.Equal(secretBytes.Length, reopened.Length);
        reopened.ReadExactly(reopenedBytes);
        Assert.Equal(secretBytes, reopenedBytes);
    }

    [Fact]
    public async Task GetSecretAsync_WhenPreCancelled_PerformsNoFileRead()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("cancelled-secret.bin", Encoding.UTF8.GetBytes("cancel-placeholder-secret"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = CreateSource(secretPath, maxSecretBytes: 128);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetSecretAsync(cancellation.Token).AsTask());

        File.Delete(secretPath);
        Assert.False(File.Exists(secretPath));
    }

    [Fact]
    public async Task GetSecretAsync_WhenMissingFile_IsRejectedSafely()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = Path.Combine(directory.Path, "missing-secret.bin");
        var source = CreateSource(secretPath);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_SECRET_FILE_MISSING", exception.ErrorCode);
        Assert.DoesNotContain(secretPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSecretAsync_WhenPathIsDirectory_IsRejectedSafely()
    {
        using var directory = TemporarySecretDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        var source = CreateSource(directory.Path);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_SECRET_FILE_IS_DIRECTORY", exception.ErrorCode);
        Assert.DoesNotContain(directory.Path, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSecretAsync_WhenFileIsEmpty_IsRejectedSafely()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("empty-secret.bin", []);
        var source = CreateSource(secretPath);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_SECRET_FILE_EMPTY", exception.ErrorCode);
    }

    [Fact]
    public async Task GetSecretAsync_WhenFileExceedsConfiguredMaximum_IsRejectedSafely()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("oversized-secret.bin", Encoding.UTF8.GetBytes("too-large-placeholder-secret"));
        var source = CreateSource(secretPath, maxSecretBytes: 4);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_SECRET_FILE_TOO_LARGE", exception.ErrorCode);
    }

    [Theory]
    [InlineData(null, 64, "HIKCENTRAL_SECRET_FILE_PATH_REQUIRED")]
    [InlineData("", 64, "HIKCENTRAL_SECRET_FILE_PATH_REQUIRED")]
    [InlineData("relative-secret.bin", 64, "HIKCENTRAL_SECRET_FILE_PATH_ABSOLUTE_REQUIRED")]
    [InlineData("bad\0path", 64, "HIKCENTRAL_SECRET_FILE_PATH_INVALID")]
    [InlineData("absolute-placeholder", 0, "HIKCENTRAL_SECRET_FILE_MAX_BYTES_INVALID")]
    [InlineData("absolute-placeholder", -1, "HIKCENTRAL_SECRET_FILE_MAX_BYTES_INVALID")]
    [InlineData("absolute-placeholder", HikCentralGateSecretFileOptions.MaximumAllowedSecretBytes + 1, "HIKCENTRAL_SECRET_FILE_MAX_BYTES_UNREASONABLE")]
    public async Task GetSecretAsync_WhenOptionsAreInvalid_IsRejectedBeforeFileAccess(
        string? configuredPath,
        int maxSecretBytes,
        string expectedErrorCode)
    {
        using var directory = TemporarySecretDirectory.Create();
        var path = configuredPath == "absolute-placeholder"
            ? directory.WriteSecret("valid-secret.bin", Encoding.UTF8.GetBytes("placeholder-secret"))
            : configuredPath;
        var source = CreateSource(path, maxSecretBytes);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.DoesNotContain("placeholder-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(path))
        {
            Assert.DoesNotContain(path, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetSecretAsync_WhenReparsePointCanBeCreated_IsRejected()
    {
        using var directory = TemporarySecretDirectory.Create();
        var targetPath = directory.WriteSecret("target-secret.bin", Encoding.UTF8.GetBytes("placeholder-secret"));
        var linkPath = Path.Combine(directory.Path, "link-secret.bin");

        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var source = CreateSource(linkPath);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_SECRET_FILE_REPARSE_POINT_UNSUPPORTED", exception.ErrorCode);
    }

    [Fact]
    public async Task GetSecretAsync_ExceptionMessagesExposeNoSecretOrCompletePath()
    {
        using var directory = TemporarySecretDirectory.Create();
        var secretPath = directory.WriteSecret("secret-path-placeholder.bin", Encoding.UTF8.GetBytes("secret-value-that-must-not-appear"));
        var source = CreateSource(secretPath, maxSecretBytes: 4);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => source.GetSecretAsync(CancellationToken.None).AsTask());

        Assert.DoesNotContain("secret-value-that-must-not-appear", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretPath, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(secretPath), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_DefaultsAreConservativeAndContainNoSecretValue()
    {
        var options = new HikCentralGateSecretFileOptions();
        var properties = typeof(HikCentralGateSecretFileOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(4096, options.MaxSecretBytes);
        Assert.Contains(nameof(HikCentralGateSecretFileOptions.SecretFilePath), properties);
        Assert.DoesNotContain(properties, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("SecretValue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Environment", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Vault", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_DoesNotDeclareForbiddenRuntimeDependenciesOrSecretFields()
    {
        var constructorParameters = typeof(MountedFileHikCentralGateSecretSource)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(MountedFileHikCentralGateSecretSource)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();
        var propertyNames = typeof(HikCentralGateSecretMaterial)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(constructorParameters.Concat(fieldTypes), IsForbiddenRuntimeDependency);
        Assert.DoesNotContain(propertyNames, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("String", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_DoesNotClaimHttpDatabaseAuditCommandWorkerOrPhysicalGateBehavior()
    {
        var memberNames = typeof(MountedFileHikCentralGateSecretSource)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.Name)
            .ToArray();

        Assert.DoesNotContain(memberNames, name => name.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Database", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Worker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memberNames, name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    private static MountedFileHikCentralGateSecretSource CreateSource(
        string? secretFilePath,
        int maxSecretBytes = HikCentralGateSecretFileOptions.DefaultMaxSecretBytes) =>
        new(new HikCentralGateSecretFileOptions
        {
            SecretFilePath = secretFilePath,
            MaxSecretBytes = maxSecretBytes
        });

    private static void AssertCleared(object owner, string fieldName)
    {
        var bytes = (byte[])owner
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

        Assert.All(bytes, value => Assert.Equal(0, value));
    }

    private static bool IsForbiddenRuntimeDependency(Type type)
    {
        if (type == typeof(HikCentralGateSecretFileOptions))
        {
            return false;
        }

        return type.Namespace?.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
               type == typeof(HttpClient) ||
               type.Name.Contains("Environment", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Certificate", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Vault", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Worker", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporarySecretDirectory : IDisposable
    {
        private TemporarySecretDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporarySecretDirectory Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"exitpass-hikcentral-secret-{Guid.NewGuid():N}"));

        public string WriteSecret(string fileName, byte[] bytes)
        {
            Directory.CreateDirectory(Path);
            var filePath = System.IO.Path.Combine(Path, fileName);
            File.WriteAllBytes(filePath, bytes);
            return filePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
