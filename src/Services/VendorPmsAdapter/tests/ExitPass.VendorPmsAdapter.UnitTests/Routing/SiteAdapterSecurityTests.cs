using System.Text.Json;
using ExitPass.VendorPmsAdapter.Api.Configuration;
using ExitPass.VendorPmsAdapter.Api.Security;
using ExitPass.VendorPmsAdapter.Application.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.Routing;

public sealed class SiteAdapterSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "exitpass-site-adapter-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnonymousRequest_FailsWithStableSanitizedError()
    {
        Directory.CreateDirectory(_root);
        var keyPath = WriteSecret("central.key", "task-owned-key");
        var options = Options(keyPath);
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/vendor/identity";
        context.Response.Body = new MemoryStream();
        var called = false;
        var middleware = new CentralPmsServiceAuthenticationMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, options);
        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("SITE_ADAPTER_AUTHENTICATION_REQUIRED", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("task-owned-key", body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongServiceIdentity_CannotUseValidSiteCredential()
    {
        Directory.CreateDirectory(_root);
        var keyPath = WriteSecret("central.key", "task-owned-key");
        var options = Options(keyPath);
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/vendor/identity";
        context.Request.Headers["X-ExitPass-Service-Identity"] = Guid.NewGuid().ToString();
        context.Request.Headers["X-ExitPass-Adapter-Key"] = "task-owned-key";
        context.Response.Body = new MemoryStream();
        var middleware = new CentralPmsServiceAuthenticationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, options);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public void SecretOutsideMountedRoot_IsRejected()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.GetTempFileName();
        try
        {
            File.WriteAllText(outside, "not-readable-through-adapter");
            var options = Options(outside);
            Assert.Contains("SITE_ADAPTER_SECRET_REFERENCE_INVALID", options.Validate("IntegrationTest"));
            Assert.Throws<InvalidOperationException>(() => SiteAdapterRuntimeOptions.ReadSecret(outside, _root));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task BindingError_IsControlledAndContainsCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "corr-123";
        context.Response.Body = new MemoryStream();
        var middleware = new ControlledAdapterExceptionMiddleware(
            _ => throw new SiteAdapterBindingException("SITE_ADAPTER_BINDING_MISMATCH"),
            NullLogger<ControlledAdapterExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("SITE_ADAPTER_BINDING_MISMATCH", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("corr-123", body.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("stack", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private SiteAdapterRuntimeOptions Options(string centralKeyPath) => new()
    {
        Activated = true,
        SiteId = Guid.NewGuid(),
        SiteGroupId = Guid.NewGuid(),
        VendorSystemId = Guid.NewGuid(),
        AdapterIdentityId = Guid.NewGuid(),
        AllowedCentralPmsServiceIdentityId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
        AdapterEndpointIdentity = "adapter-a",
        Environment = "IST",
        ParkingLotIndexCode = "A",
        HikCentralBaseUrl = "http://hikcentral-a",
        SecretMountRoot = _root,
        HikCentralAppKeyFile = centralKeyPath,
        HikCentralAppSecretFile = centralKeyPath,
        CentralPmsApiKeyFile = centralKeyPath,
        HikCentralUserId = "task-owned-user",
        AllowTaskOwnedHttp = true
    };

    private string WriteSecret(string name, string value)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, value);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
