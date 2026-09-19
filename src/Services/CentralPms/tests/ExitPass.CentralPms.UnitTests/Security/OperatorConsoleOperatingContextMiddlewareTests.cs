using System.Security.Claims;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Security;

public sealed class OperatorConsoleOperatingContextMiddlewareTests
{
    [Fact]
    public async Task Device_binding_establishment_post_without_bound_context_is_not_intercepted()
    {
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        var nextCalled = false;
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = AuthenticatedContext(
            Guid.NewGuid(),
            HttpMethods.Post,
            "/v1/operator-console/device-binding/establish");

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue();
        service.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Protected_operator_console_request_without_bound_context_remains_denied()
    {
        var sessionId = Guid.NewGuid();
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        service.ValidateSessionAsync(sessionId, null, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => OperatorConsoleOperatingContextResult.Failure(
                OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired,
                call.ArgAt<Guid>(2)));
        var nextCalled = false;
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = AuthenticatedContext(sessionId, HttpMethods.Get, "/v1/operator-console/statutory-discounts/reviews");

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        response!.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired);
    }

    [Theory]
    [InlineData("POST", "/v1/ops/operator-console/sessions/lookup")]
    [InlineData("GET", "/v1/ops/operator-console/fiscal-issuance/lookup")]
    [InlineData("GET", "/v1/ops/operator-console/fiscal-issuance/references/3d63d347-dadf-4e9f-94fb-644eeb90207a")]
    [InlineData("GET", "/v1/ops/operator-console/statutory-discounts/drafts")]
    [InlineData("GET", "/v1/ops/operator-console/statutory-discounts/drafts/3d63d347-dadf-4e9f-94fb-644eeb90207a")]
    [InlineData("GET", "/v1/ops/operator-console/statutory-discounts/3d63d347-dadf-4e9f-94fb-644eeb90207a/evidence")]
    [InlineData("POST", "/v1/ops/operator-console/statutory-discounts/resolve-policy")]
    public async Task Explicit_read_support_endpoint_bypasses_operating_context_validation(string method, string path)
    {
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        var nextCalled = false;
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = AuthenticatedContext(Guid.NewGuid(), method, path);
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(OperatorConsoleOperatingContextRequirementMetadata.NotRequired),
            "read-support"));

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue();
        service.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Controlled_write_without_bypass_metadata_still_requires_operating_context()
    {
        var sessionId = Guid.NewGuid();
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        service.ValidateSessionAsync(sessionId, null, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => OperatorConsoleOperatingContextResult.Failure(
                OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired,
                call.ArgAt<Guid>(2)));
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            _ => Task.CompletedTask,
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = AuthenticatedContext(
            sessionId,
            HttpMethods.Post,
            "/v1/ops/operator-console/statutory-discounts/draft");

        await middleware.InvokeAsync(context, service);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        await service.Received(1).ValidateSessionAsync(
            sessionId,
            null,
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unauthenticated_read_support_request_reaches_downstream_authentication_without_readiness_authority()
    {
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/v1/ops/operator-console/statutory-discounts/drafts";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(OperatorConsoleOperatingContextRequirementMetadata.NotRequired),
            "read-support"));

        await middleware.InvokeAsync(context, service);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        service.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Shift_management_exclusion_remains_unchanged()
    {
        var service = Substitute.For<IOperatorConsoleOperatingContextService>();
        var nextCalled = false;
        var middleware = new OperatorConsoleOperatingContextMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<OperatorConsoleOperatingContextMiddleware>.Instance);
        var context = AuthenticatedContext(
            Guid.NewGuid(),
            HttpMethods.Get,
            "/v1/operator-console/shift-management/shifts");

        await middleware.InvokeAsync(context, service);

        nextCalled.Should().BeTrue();
        service.ReceivedCalls().Should().BeEmpty();
    }

    private static DefaultHttpContext AuthenticatedContext(Guid sessionId, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(HumanSessionAuthenticationHandler.InternalHumanSessionIdClaimType, sessionId.ToString("D"))],
            HumanSessionAuthenticationHandler.SchemeName));
        return context;
    }
}
