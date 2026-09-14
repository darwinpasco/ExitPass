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
