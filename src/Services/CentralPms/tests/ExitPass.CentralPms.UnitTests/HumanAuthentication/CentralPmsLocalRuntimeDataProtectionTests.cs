using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class CentralPmsLocalRuntimeDataProtectionTests
{
    [Fact]
    public void PersistentPitxLauncher_MountsPrivateDataProtectionKeyRingReadWrite()
    {
        var launcher = File.ReadAllText(FindRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Start-CentralPms.ps1"));

        launcher.Should().Contain("$dataProtectionKeyDirectory = Join-Path $privateRoot 'data-protection\\central-pms'");
        launcher.Should().Contain("New-Item -ItemType Directory -Path $dataProtectionKeyDirectory -Force");
        launcher.Should().Contain("source=$dataProtectionKeyDirectory,target=/root/.aspnet/DataProtection-Keys");
        launcher.Should().NotContain("source=$dataProtectionKeyDirectory,target=/root/.aspnet/DataProtection-Keys,readonly");
    }

    [Fact]
    public void LocalRuntimeRunbook_PreservesAntiforgeryAndDocumentsOneTimeStaleCookieCleanup()
    {
        var runbook = File.ReadAllText(FindRepositoryFile(
            "docs", "v1.3", "ExitPass_Local_Runtime_Quickstart.md"));

        runbook.Should().Contain("Data Protection key ring");
        runbook.Should().Contain("clear the existing Central PMS");
        runbook.Should().Contain("cookies once");
        runbook.Should().Contain("Do not disable antiforgery validation");
    }

    [Fact]
    public async Task PersistedKeyRing_ValidatesAntiforgeryTokenAfterApplicationRecreation()
    {
        var keyDirectory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"exitpass-central-pms-data-protection-{Guid.NewGuid():N}"));

        try
        {
            var firstProvider = BuildProvider(keyDirectory);
            var firstContext = CreateHttpsContext(firstProvider);
            var firstAntiforgery = firstProvider.GetRequiredService<IAntiforgery>();
            var tokens = firstAntiforgery.GetAndStoreTokens(firstContext);

            tokens.CookieToken.Should().NotBeNullOrWhiteSpace();
            tokens.RequestToken.Should().NotBeNullOrWhiteSpace();
            keyDirectory.GetFiles("key-*.xml").Should().ContainSingle();
            var firstKeyName = keyDirectory.GetFiles("key-*.xml").Single().Name;

            firstProvider.Dispose();

            var secondProvider = BuildProvider(keyDirectory);
            var secondContext = CreateHttpsContext(secondProvider);
            secondContext.Request.Headers.Cookie = $"__Host-ExitPass-Antiforgery={tokens.CookieToken}";
            secondContext.Request.Headers["X-CSRF-Token"] = tokens.RequestToken;

            var validation = () => secondProvider.GetRequiredService<IAntiforgery>()
                .ValidateRequestAsync(secondContext);

            await validation.Should().NotThrowAsync();
            keyDirectory.GetFiles("key-*.xml").Should().ContainSingle(file => file.Name == firstKeyName);
            secondProvider.Dispose();
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    private static ServiceProvider BuildProvider(DirectoryInfo keyDirectory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName("ExitPass.CentralPms")
            .PersistKeysToFileSystem(keyDirectory);
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-ExitPass-Antiforgery";
            options.HeaderName = "X-CSRF-Token";
        });
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpsContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Scheme = "https";
        context.Response.Body = Stream.Null;
        return context;
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
