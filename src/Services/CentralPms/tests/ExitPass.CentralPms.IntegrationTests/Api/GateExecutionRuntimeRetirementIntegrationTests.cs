using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.Gates;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Proves Central PMS no longer exposes runtime activation for ExitPass-owned physical gate execution.
/// </summary>
public sealed class GateExecutionRuntimeRetirementIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string RetiredExecutionRoute = "/v1/internal/gates/commands/{gateCommandId}/execute";
    private const string IssueExitAuthorizationRoute = "/v1/internal/payment-attempts/{paymentAttemptId:guid}/issue-exit-authorization";
    private const string ConsumeExitAuthorizationRoute = "/v1/gate/authorizations/{exitAuthorizationId:guid}/consume";
    private const string CommandStateRoute = "/v1/internal/gates/authorization-consumptions/{gateAuthorizationConsumptionId:guid}/command-state";
    private readonly CustomWebApplicationFactory _factory;

    public GateExecutionRuntimeRetirementIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ControlledExecutionRoute_IsNotMappedEvenWhenOldSwitchesArePresent()
    {
        using var factory = _factory.WithConfigurationOverrides(new Dictionary<string, string?>
        {
            ["CentralPms:HikCentralControlledGateExecution:Enabled"] = "true",
            ["CentralPms:HikCentralGateIntegration:Enabled"] = "true",
            ["CentralPms:GateCommandDispatchWorker:Enabled"] = "true"
        });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/internal/gates/commands/88000000-0000-0000-0000-000000000001/execute",
            new { confirmation = "OPEN_GATE" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        RoutePatterns(factory).Should().NotContain(RetiredExecutionRoute);
    }

    [Fact]
    public void NormalComposition_DoesNotResolveLiveHikCentralRuntimeOrFakeFallback()
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetService<IHikCentralGateActionAdapter>().Should().BeNull();
        services.GetService<IHikCentralGateSecretSource>().Should().BeNull();
        services.GetService<IHikCentralGateRuntimeMaterialProvider>().Should().BeNull();
        services.GetService<IHikCentralHttpTransport>().Should().BeNull();
        services.GetServices<IHikCentralGateActionAdapter>().Should().BeEmpty();
    }

    [Fact]
    public void DefaultConfiguration_ContainsNoDirectPhysicalExecutionActivationSwitchesOrSecrets()
    {
        using var client = _factory.CreateClient();
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();

        configuration.GetSection("CentralPms:HikCentralGateIntegration").Exists().Should().BeFalse();
        configuration.GetSection("CentralPms:HikCentralControlledGateExecution").Exists().Should().BeFalse();
        configuration.GetSection("CentralPms:GateCommandDispatchWorker").Exists().Should().BeFalse();

        var configurationText = string.Join(
            "\n",
            configuration.AsEnumerable().Select(pair => $"{pair.Key}={pair.Value}"));

        configurationText.Contains("AppSecret", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        configurationText.Contains("ClientKeyIdentifier", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        configurationText.Contains("SecretFilePath", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        configurationText.Contains("OPEN_GATE", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void GateCommandDispatchWorker_IsNotRegisteredAsHostedService()
    {
        using var client = _factory.CreateClient();
        var hostedServices = _factory.Services.GetServices<IHostedService>().ToArray();

        hostedServices.Should().NotContain(service => service is GateCommandDispatchWorker);
        _factory.Services.GetService<IGateCommandDispatchWorkerDelay>().Should().BeNull();
    }

    [Fact]
    public void GateCommandRecoveryWorker_RemainsDisabledByDefaultAndHasNoPhysicalExecutionDependency()
    {
        using var client = _factory.CreateClient();
        var hostedServices = _factory.Services.GetServices<IHostedService>().ToArray();
        var constructorParameters = typeof(GateCommandRecoveryWorker)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        hostedServices.Should().Contain(service => service is GateCommandRecoveryWorker);
        _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GateCommandRecoveryWorkerOptions>>()
            .Value.Enabled.Should().BeFalse();
        constructorParameters.Should().NotContain(typeof(IHikCentralGateActionAdapter));
        constructorParameters.Should().NotContain(typeof(IGateCommandExecutionService));
    }

    [Fact]
    public void AuthorizationConsumptionAndReadOnlyInventoryRoutes_RemainMappedWithInternalMtlsWhereRequired()
    {
        using var client = _factory.CreateClient();
        var routeEndpoints = RouteEndpoints(_factory).ToArray();

        routeEndpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == IssueExitAuthorizationRoute);
        routeEndpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == ConsumeExitAuthorizationRoute);
        routeEndpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == CommandStateRoute);

        Endpoint(routeEndpoints, IssueExitAuthorizationRoute).Metadata
            .GetMetadata<InternalServiceEndpointMetadata>().Should().NotBeNull();
        Endpoint(routeEndpoints, ConsumeExitAuthorizationRoute).Metadata
            .GetMetadata<InternalServiceEndpointMetadata>().Should().NotBeNull();
        Endpoint(routeEndpoints, CommandStateRoute).Metadata
            .GetMetadata<InternalServiceEndpointMetadata>().Should().NotBeNull();
    }

    [Fact]
    public void ExitAuthorizationAndGateFacingConsumptionServices_RemainRegisteredWithoutAdapterCoupling()
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetService<ExitPass.CentralPms.Application.Payments.IIssueExitAuthorizationUseCase>()
            .Should().NotBeNull();
        services.GetService<ExitPass.CentralPms.Application.Payments.IConsumeExitAuthorizationUseCase>()
            .Should().NotBeNull();
        services.GetService<ExitPass.CentralPms.Application.Security.IGateDeviceIdentityValidator>()
            .Should().NotBeNull();
        services.GetService<IGateCommandStateReadRepository>().Should().NotBeNull();
        services.GetService<IHikCentralGateActionAdapter>().Should().BeNull();
    }

    [Fact]
    public void ReadOnlyCommandStateEndpoint_DeclaresNoPhysicalGateOpenedClaim()
    {
        using var client = _factory.CreateClient();
        var commandStateEndpoint = Endpoint(RouteEndpoints(_factory), CommandStateRoute);
        var descriptionMetadata = commandStateEndpoint.Metadata
            .Single(metadata => metadata.GetType().Name.Contains("Description", StringComparison.Ordinal));
        var description = descriptionMetadata.ToString();

        description.Should().NotBeNull();
        description!.Contains("read-only", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        description.Contains("does not imply that a physical gate opened", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    private static IReadOnlyList<string?> RoutePatterns(CustomWebApplicationFactory factory) =>
        RouteEndpoints(factory)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

    private static IEnumerable<RouteEndpoint> RouteEndpoints(CustomWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>();

    private static RouteEndpoint Endpoint(
        IEnumerable<RouteEndpoint> endpoints,
        string route) =>
        endpoints.Single(endpoint => endpoint.RoutePattern.RawText == route);
}
