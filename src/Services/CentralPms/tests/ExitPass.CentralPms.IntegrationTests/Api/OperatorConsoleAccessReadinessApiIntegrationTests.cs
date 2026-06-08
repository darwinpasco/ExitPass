using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console access readiness API route and DTO mapping.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant enforced: readiness evaluation returns machine-readable access dimensions and does not mutate
/// statutory discount, payment, provider, gate, coupon, reconciliation, HikCentral, or WebPay behavior.
/// </summary>
public sealed class OperatorConsoleAccessReadinessApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/access/readiness/evaluate";
    private static readonly Guid OperatorUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid ShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetEntityId = Guid.Parse("b84541dc-4929-4f53-bdcc-22b145dd7c41");
    private static readonly Guid CorrelationId = Guid.Parse("52883917-a776-4656-8d0a-b87087d646b1");

    /// <summary>Verifies the documented readiness route exists.</summary>
    [Fact]
    public void EndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>Verifies the readiness route is discoverable through Swagger/OpenAPI.</summary>
    [Fact]
    public async Task EndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/access/readiness/evaluate");
        swaggerJson.Should().Contain("EvaluateOperatorConsoleAccessReadiness");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>Verifies a complete sandbox readiness context returns an allowed response.</summary>
    [Fact]
    public async Task Evaluate_WhenCompleteSandboxContextPosted_ReturnsAllowedReadiness()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, CompleteRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessEvaluationId.Should().BeNull();
        body.AccessAllowed.Should().BeTrue();
        body.AccessDecision.Should().Be("ALLOWED");
        body.RequestedAction.Should().Be(OperatorConsoleActionCodes.DecideStatutoryDiscount);
        body.ReadinessStatus.Should().Be("READY");
        body.ReadinessDimensions.Should().NotBeEmpty();
        body.ReadinessDimensions.Should().OnlyContain(dimension => dimension.Status == "READY");
        body.DenialReasons.Should().BeEmpty();
        body.OperatorReadiness.Ready.Should().BeTrue();
        body.DeviceReadiness.Ready.Should().BeTrue();
        body.ShiftReadiness.Ready.Should().BeTrue();
        body.SiteReadiness.Ready.Should().BeTrue();
        body.WorkflowReadiness.Ready.Should().BeTrue();
        body.AuditPersisted.Should().BeFalse();
        body.CorrelationId.Should().Be(CorrelationId);
    }

    /// <summary>Verifies missing operator context returns a stable evaluated denial.</summary>
    [Fact]
    public async Task Evaluate_WhenOperatorUserIdMissing_ReturnsOperatorIdMissingDenial()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, CompleteRequest() with { OperatorUserId = null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.OperatorIdMissing);
        body.ReadinessDimensions.Single(dimension => dimension.Dimension == "operator")
            .DenialReasonCodes.Should().Contain(OperatorConsoleDenialReasonCatalog.OperatorIdMissing);
    }

    /// <summary>Verifies missing site context returns stable site denial reasons.</summary>
    [Fact]
    public async Task Evaluate_WhenSiteContextMissing_ReturnsSiteDenialReasons()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            CompleteRequest() with { SiteId = null, SiteGroupId = null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(new[]
            {
                OperatorConsoleDenialReasonCatalog.SiteIdMissing,
                OperatorConsoleDenialReasonCatalog.SiteGroupIdMissing
            });
        body.SiteReadiness.Ready.Should().BeFalse();
    }

    /// <summary>Verifies production fallback-only trust returns a stable denial reason.</summary>
    [Fact]
    public async Task Evaluate_WhenProductionFallbackContextPosted_ReturnsProductionFallbackDenial()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            CompleteRequest() with
            {
                DevModeContext = new OperatorConsoleAccessReadinessDevModeContextDto(
                    UsesLocalDevFallbackContext: true,
                    EnvironmentName: "Production")
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.ReadinessStatus.Should().Be("BLOCKED");
        body.Retryable.Should().BeFalse();
        body.NextOperatorAction.Should().Be("Use production device enrollment, active shift, and site readiness records before continuing.");
        body.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
        body.DenialReasons.Single(reason => reason.Code == OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction)
            .Retryable.Should().BeFalse();
        body.ReadinessDimensions.Single(dimension => dimension.Dimension == "localDevBoundary")
            .DenialReasonCodes.Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
    }

    /// <summary>Verifies production fails closed when operator-console readiness tables are not available.</summary>
    [Fact]
    public async Task Evaluate_WhenProductionRepositoryCapabilityMissing_ReturnsProductionTrustDenial()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            CompleteRequest() with
            {
                DevModeContext = new OperatorConsoleAccessReadinessDevModeContextDto(
                    UsesLocalDevFallbackContext: false,
                    EnvironmentName: "Production")
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
    }


    /// <summary>Verifies local/dev fallback remains usable for controlled non-production validation.</summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Sandbox")]
    public async Task Evaluate_WhenNonProductionFallbackContextPosted_DoesNotReturnProductionFallbackDenial(string environmentName)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            CompleteRequest() with
            {
                DevModeContext = new OperatorConsoleAccessReadinessDevModeContextDto(
                    UsesLocalDevFallbackContext: true,
                    EnvironmentName: environmentName)
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DenialReasons.Select(reason => reason.Code)
            .Should().NotContain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
        body.ReadinessDimensions.Single(dimension => dimension.Dimension == "localDevBoundary")
            .Status.Should().Be("READY");
    }

    /// <summary>Verifies invalid request shape returns the standard error envelope.</summary>
    [Fact]
    public async Task Evaluate_WhenRequestedActionMissing_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, CompleteRequest() with { RequestedAction = null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_ACCESS_READINESS_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    private static OperatorConsoleAccessReadinessRequest CompleteRequest() =>
        new(
            OperatorUserId,
            DeviceBindingId,
            ShiftId,
            SiteId,
            SiteGroupId,
            OperatorConsoleActionCodes.DecideStatutoryDiscount,
            TargetEntityType: "STATUTORY_DISCOUNT_VALIDATION",
            TargetEntityId,
            WorkflowState: "PENDING_OPERATOR_REVIEW",
            CorrelationId,
            IdempotencyKey: "operator-console-readiness-test",
            ClientContext: new OperatorConsoleAccessReadinessClientContextDto(
                UiModule: "statutory-discount",
                ScreenState: "evidence-satisfied"),
            DevModeContext: new OperatorConsoleAccessReadinessDevModeContextDto(
                UsesLocalDevFallbackContext: false,
                EnvironmentName: "Development"));
}
