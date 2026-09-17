using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Security;

public sealed class CentralPmsRbacApplicationBoundaryTests
{
    [Theory]
    [InlineData("OPERATOR_CONSOLE", StatusCodes.Status403Forbidden, false)]
    [InlineData("MANAGEMENT_PLATFORM", StatusCodes.Status200OK, true)]
    public async Task StatutoryApprovalPermission_IsEnforcedAtItsManagementPlatformSurface(
        string audience,
        int expectedStatus,
        bool expectedNext)
    {
        var nextCalled = false;
        var middleware = new CentralPmsRbacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.RecordDeniedAsync(
                Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new ReconciliationPolicyMetadata("OperatorConsoleStatutoryDiscountDecisionApprove")),
            "statutory-approval"));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim("exitpass_audience", audience),
            new Claim(CentralPmsRbacPolicyCatalog.PermissionClaimType, "statutory-discounts.decision.approve")
        ], "test"));

        await middleware.InvokeAsync(
            context,
            Options.Create(new CentralPmsRbacOptions()),
            repository,
            environment,
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        context.Response.StatusCode.Should().Be(expectedStatus);
        nextCalled.Should().Be(expectedNext);
    }
}
