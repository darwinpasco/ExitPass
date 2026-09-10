using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class SitePosServerBindingResolverTests
{
    private static readonly Guid SiteId = Guid.Parse("41000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherSiteId = Guid.Parse("41000000-0000-4000-8000-000000000002");
    private static readonly Guid PosServerId = Guid.Parse("41000000-0000-4000-8000-000000000003");
    private const string PosServerRef = "SITE-POS-PRIMARY";

    [Fact]
    public void Resolve_CanonicalUuidAndLogicalReference_ReturnsConfiguredBinding()
    {
        var result = Resolver().Resolve(new SitePosServerBindingRequest(SiteId, PosServerId, PosServerRef));

        result.IsSuccess.Should().BeTrue();
        result.Endpoint!.SitePosServerId.Should().Be(PosServerId);
        result.Endpoint.SitePosServerRef.Should().Be(PosServerRef);
    }

    [Fact]
    public void Resolve_OrdinarySiteDiscovery_ReturnsCanonicalIdentityAndReference()
    {
        var result = Resolver().Resolve(new SitePosServerBindingRequest(SiteId));

        result.IsSuccess.Should().BeTrue();
        result.Endpoint!.SiteId.Should().Be(SiteId);
        result.Endpoint.SitePosServerId.Should().Be(PosServerId);
        result.Endpoint.SitePosServerRef.Should().Be(PosServerRef);
    }

    [Fact]
    public void Resolve_MissingBinding_FailsClosed()
    {
        var result = Resolver(endpoints: []).Resolve(new SitePosServerBindingRequest(SiteId));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.Missing);
        result.Endpoint.Should().BeNull();
    }

    [Fact]
    public void Resolve_InactiveBinding_FailsClosed()
    {
        var result = Resolver(endpoint: Endpoint(enabled: false))
            .Resolve(new SitePosServerBindingRequest(SiteId));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.Inactive);
    }

    [Fact]
    public void Resolve_IdentityBelongingToDifferentSite_ReportsSiteMismatch()
    {
        var result = Resolver(endpoint: Endpoint(siteId: OtherSiteId))
            .Resolve(new SitePosServerBindingRequest(SiteId, PosServerId));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.SiteMismatch);
    }

    [Fact]
    public void Resolve_WrongCanonicalPosIdentity_FailsClosed()
    {
        var result = Resolver().Resolve(new SitePosServerBindingRequest(SiteId, Guid.NewGuid()));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.IdentityMismatch);
    }

    [Fact]
    public void Resolve_WrongEndpointReference_FailsClosed()
    {
        var result = Resolver().Resolve(new SitePosServerBindingRequest(SiteId, PosServerId, "SITE-POS-OTHER"));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.ReferenceMismatch);
    }

    [Fact]
    public void Resolve_AmbiguousActiveSiteBindings_FailsClosed()
    {
        var second = Endpoint(posServerId: Guid.NewGuid(), posServerRef: "SITE-POS-SECONDARY");
        var result = Resolver(endpoints: [Endpoint(), second])
            .Resolve(new SitePosServerBindingRequest(SiteId));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.Ambiguous);
    }

    [Fact]
    public void Resolve_IdentityReusedByAnotherSite_FailsAsInconsistent()
    {
        var duplicate = Endpoint(siteId: OtherSiteId, posServerRef: "SITE-POS-OTHER");
        var result = Resolver(endpoints: [Endpoint(), duplicate])
            .Resolve(new SitePosServerBindingRequest(SiteId, PosServerId));

        result.Code.Should().Be(SitePosServerBindingResolutionCodes.Inconsistent);
    }

    [Fact]
    public void Resolve_ReplayReturnsSameRoutingResult()
    {
        var resolver = Resolver();

        var first = resolver.Resolve(new SitePosServerBindingRequest(SiteId, PosServerId));
        var replay = resolver.Resolve(new SitePosServerBindingRequest(SiteId, PosServerId));

        replay.Should().BeEquivalentTo(first);
    }

    private static ConfiguredSitePosServerBindingResolver Resolver(
        SitePosServerEndpointOptions? endpoint = null,
        IReadOnlyList<SitePosServerEndpointOptions>? endpoints = null)
    {
        var options = new FiscalIssuancePosServerIntegrationOptions
        {
            RuntimeEnvironment = "IntegrationTest",
            Endpoints = (endpoints ?? [endpoint ?? Endpoint()]).ToList()
        };
        return new ConfiguredSitePosServerBindingResolver(options);
    }

    private static SitePosServerEndpointOptions Endpoint(
        Guid? siteId = null,
        Guid? posServerId = null,
        string? posServerRef = null,
        bool enabled = true) => new()
    {
        SiteId = siteId ?? SiteId,
        SitePosServerId = posServerId ?? PosServerId,
        SitePosServerRef = posServerRef ?? PosServerRef,
        BaseUrl = "http://site-pos.test/",
        ApiKeyFile = "unused-by-binding-resolution",
        Environment = "IntegrationTest",
        Enabled = enabled
    };
}
