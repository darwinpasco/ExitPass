using ExitPass.CentralPms.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Security;

public sealed class ProductionFixtureIdentityHeaderGuardMiddlewareTests
{
    [Theory]
    [InlineData("X-ExitPass-User-Id")]
    [InlineData("X-Operator-User-Id")]
    [InlineData("X-ExitPass-Permissions")]
    public async Task Production_rejects_fixture_human_authority_headers(string header)
    {
        var nextCalled = false;
        var middleware = new ProductionFixtureIdentityHeaderGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers[header] = Guid.NewGuid().ToString();
        await middleware.InvokeAsync(context, new Environment("Production"), Options.Create(new CentralPmsRbacOptions { AllowFixtureIdentityHeaders = true }));
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_development_fixture_composition_can_retain_headers()
    {
        var nextCalled = false;
        var middleware = new ProductionFixtureIdentityHeaderGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-ExitPass-User-Id"] = Guid.NewGuid().ToString();
        await middleware.InvokeAsync(context, new Environment("Development"), Options.Create(new CentralPmsRbacOptions { AllowFixtureIdentityHeaders = true }));
        nextCalled.Should().BeTrue();
    }

    private sealed class Environment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
