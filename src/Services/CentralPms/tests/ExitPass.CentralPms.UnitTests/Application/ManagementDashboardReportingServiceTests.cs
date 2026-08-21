using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementDashboardReportingServiceTests
{
    private static readonly Guid UserId = Guid.Parse("93100000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93100000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93100000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("93100000-0000-0000-0000-000000000201");
    private static readonly Guid CorrelationId = Guid.Parse("93100000-0000-0000-0000-000000000301");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T08:00:00Z");

    [Fact]
    public async Task Catalog_UsesStableIdsAndDoesNotAdvertiseDeferredReportsAsOperational()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var result = await service.GetCatalogAsync(Actor(), CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.Success);
        result.Value!.ContractVersion.Should().Be(ManagementDashboardReportingValues.ContractVersion);
        result.Value.Reports.Select(report => report.ReportId).Should().Equal(
            ManagementDashboardReportingValues.OperationalOverviewReportId,
            ManagementDashboardReportingValues.PaymentReconciliationReportId,
            ManagementDashboardReportingValues.FiscalExceptionReportId,
            ManagementDashboardReportingValues.ManagementActivityReportId);
        result.Value.Reports.Single(report => report.ReportId == ManagementDashboardReportingValues.OperationalOverviewReportId)
            .Availability.Should().Be(ManagementDashboardReportingValues.Partial);
        result.Value.Reports.Where(report => report.ReportId != ManagementDashboardReportingValues.OperationalOverviewReportId)
            .Should().OnlyContain(report => report.Availability == ManagementDashboardReportingValues.Unavailable);
        repository.Audits.Should().ContainSingle(audit => audit.EventType == "MANAGEMENT_DASHBOARD_CATALOG_READ" && audit.Result == "SUCCESS");
    }

    [Fact]
    public async Task Catalog_WhenFeatureDisabled_FailsClosedWithoutReadingOrAuditing()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository, enabled: false);

        var result = await service.GetCatalogAsync(Actor(), CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.FeatureDisabled);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.FeatureDisabled);
        repository.ScopeReadCount.Should().Be(0);
        repository.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Catalog_WhenAuthorizationEpochIsStale_ReturnsSessionInvalid()
    {
        var repository = new FakeRepository
        {
            ActorValidation = ManagementDashboardActorValidationStatus.Invalid
        };
        var service = CreateService(repository);

        var result = await service.GetCatalogAsync(Actor(), CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.SessionInvalid);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.SessionInvalid);
        repository.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GLOBAL")]
    [InlineData("PORTFOLIO")]
    public async Task Overview_RequiresExplicitSiteOrSiteGroupScope(string? scopeType)
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(
            Actor(),
            new ManagementDashboardOperationalOverviewQuery(scopeType, SiteId, CorrelationId),
            CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.InvalidScope);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.InvalidScopeType);
        repository.ScopeReadCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Overview_RequiresExplicitNonEmptyScopeReference(string? reference)
    {
        var service = CreateService(new FakeRepository());
        Guid? scopeReference = reference is null ? null : Guid.Parse(reference);

        var result = await service.GetOperationalOverviewAsync(
            Actor(),
            new ManagementDashboardOperationalOverviewQuery("SITE", scopeReference, CorrelationId),
            CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.InvalidScope);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.InvalidScopeReference);
    }

    [Fact]
    public async Task Overview_WhenScopeDenied_AuditsSafeDenialAndDoesNotReadProjection()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Denied, null)
        };
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.ScopeNotFoundOrDenied);
        repository.ProjectionReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit =>
            audit.Result == "DENIED" && audit.ReasonCode == "SCOPE_DENIED");
    }

    [Fact]
    public async Task Overview_WhenProjectionSourceUnavailable_ReturnsPartialWithoutFabricatedZeroMetrics()
    {
        var repository = new FakeRepository
        {
            ProjectionResult = new ManagementDashboardProjectionReadResult(
                ManagementDashboardProjectionReadStatus.Unavailable,
                [])
        };
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.Success);
        result.Value!.Availability.Should().Be(ManagementDashboardReportingValues.Partial);
        result.Value.Sections.Single(section => section.SectionId == "site-operational-status")
            .Availability.Should().Be(ManagementDashboardReportingValues.Available);
        var unavailable = result.Value.Sections.Where(section => section.SourceAuthority == "CENTRAL_PMS_VENDOR_SESSION_PROJECTION").ToArray();
        unavailable.Should().OnlyContain(section => section.Availability == ManagementDashboardReportingValues.Unavailable);
        unavailable.Should().OnlyContain(section => section.Metrics.Count == 0);
        unavailable.SelectMany(section => section.Warnings).Should().Contain(ManagementDashboardReportingValues.ProjectionSourceUnavailable);
        repository.Audits.Should().ContainSingle(audit => audit.EventType == "MANAGEMENT_DASHBOARD_SOURCE_UNAVAILABLE");
    }

    [Fact]
    public async Task Overview_WhenNoProjectionTargetsConfigured_ReturnsNotApplicableInsteadOfZeroSuccess()
    {
        var service = CreateService(new FakeRepository
        {
            ProjectionResult = new ManagementDashboardProjectionReadResult(ManagementDashboardProjectionReadStatus.Resolved, [])
        });

        var result = await service.GetOperationalOverviewAsync(Actor(), Query(), CancellationToken.None);

        result.Value!.Sections.Where(section => section.SourceAuthority == "CENTRAL_PMS_VENDOR_SESSION_PROJECTION")
            .Should().OnlyContain(section =>
                section.Availability == ManagementDashboardReportingValues.NotApplicable &&
                section.Metrics.Count == 0);
    }

    [Fact]
    public async Task Overview_ReturnsSourceFreshnessAndStaleClassification()
    {
        var repository = new FakeRepository
        {
            ProjectionResult = new ManagementDashboardProjectionReadResult(
                ManagementDashboardProjectionReadStatus.Resolved,
                [
                    new ManagementDashboardProjectionTargetSnapshot(
                        true,
                        "HEALTHY",
                        Now.AddMinutes(-30),
                        Now.AddMinutes(-30),
                        Now.AddMinutes(-30),
                        7)
                ])
        };
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(Actor(), Query(), CancellationToken.None);

        result.Value!.Freshness.Should().Be(ManagementDashboardReportingValues.Stale);
        result.Value.DataAsOf.Should().NotBeNull();
        result.Value.Sections.Should().OnlyContain(section => !string.IsNullOrWhiteSpace(section.SourceAuthority));
        result.Value.Sections.Single(section => section.SectionId == "vendor-projection-freshness")
            .Metrics.Should().Contain(metric => metric.MetricId == "active-projections" && metric.Value == 7);
        result.Value.Warnings.Should().Contain(ManagementDashboardReportingValues.ProjectionStale);
    }

    [Fact]
    public async Task Overview_SiteGroupScopeRemainsServerResolvedAndAudited()
    {
        var scope = Scope(ManagementDashboardReportingValues.ScopeSiteGroup, SiteGroupId, "Synthetic Group");
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Resolved, scope)
        };
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(
            Actor(),
            new ManagementDashboardOperationalOverviewQuery("site-group", SiteGroupId, CorrelationId),
            CancellationToken.None);

        result.Value!.RequestedScope.ScopeType.Should().Be(ManagementDashboardReportingValues.ScopeSiteGroup);
        result.Value.EffectiveScope.Should().BeEquivalentTo(new ManagementDashboardScope("SITE_GROUP", SiteGroupId, "Synthetic Group"));
        repository.Audits.Should().ContainSingle(audit =>
            audit.ScopeType == "SITE_GROUP" && audit.ScopeReference == SiteGroupId && audit.Result == "SUCCESS");
    }

    [Fact]
    public async Task Overview_WhenScopeSourceFails_AuditsQueryFailureAndReturnsUnavailable()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.SourceUnavailable, null)
        };
        var service = CreateService(repository);

        var result = await service.GetOperationalOverviewAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementDashboardReportingOutcome.SourceUnavailable);
        result.Retryable.Should().BeTrue();
        repository.Audits.Should().ContainSingle(audit => audit.ReasonCode == "QUERY_FAILED" && audit.Result == "FAILED");
    }

    [Fact]
    public void DashboardPolicies_UseExistingDedicatedReadPermissions()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementDashboardReportingValues.CatalogPolicy)
            .Should().Equal(ManagementDashboardReportingValues.CatalogPermission);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementDashboardReportingValues.OverviewPolicy)
            .Should().Equal(ManagementDashboardReportingValues.OverviewPermission);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementDashboardReportingValues.OverviewPolicy)
            .Should().NotContain(["reports.export", "reconciliation.manage", "user.manage"]);
    }

    private static ManagementDashboardReportingService CreateService(FakeRepository repository, bool enabled = true) =>
        new(
            repository,
            new ManagementDashboardReportingOptions
            {
                Enabled = enabled,
                ProjectionStaleAfterMinutes = 15
            },
            new FixedTimeProvider(Now));

    private static ManagementDashboardActor Actor() => new(UserId, SessionId);

    private static ManagementDashboardOperationalOverviewQuery Query() =>
        new(ManagementDashboardReportingValues.ScopeSite, SiteId, CorrelationId);

    private static ManagementDashboardScopeSnapshot Scope(string type = "SITE", Guid? reference = null, string name = "Synthetic Site") =>
        new(
            type,
            reference ?? SiteId,
            name,
            Now.AddMinutes(-1),
            [new ManagementDashboardSiteSnapshot(SiteId, "ACTIVE", true, Now.AddMinutes(-1))]);

    private sealed class FakeRepository : IManagementDashboardReportingRepository
    {
        public ManagementDashboardActorValidationStatus ActorValidation { get; init; } =
            ManagementDashboardActorValidationStatus.Valid;

        public ManagementDashboardScopeReadResult ScopeResult { get; init; } =
            new(ManagementDashboardScopeReadStatus.Resolved, Scope());

        public ManagementDashboardProjectionReadResult ProjectionResult { get; init; } =
            new(
                ManagementDashboardProjectionReadStatus.Resolved,
                [new ManagementDashboardProjectionTargetSnapshot(true, "HEALTHY", Now, Now, Now, 3)]);

        public int ScopeReadCount { get; private set; }
        public int ProjectionReadCount { get; private set; }
        public List<ManagementDashboardAuditRecord> Audits { get; } = [];

        public Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
            ManagementDashboardActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult(ActorValidation);

        public Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
            ManagementDashboardActor actor,
            string scopeType,
            Guid scopeReference,
            CancellationToken cancellationToken)
        {
            ScopeReadCount++;
            return Task.FromResult(ScopeResult);
        }

        public Task<ManagementDashboardProjectionReadResult> ReadProjectionHealthAsync(
            ManagementDashboardScopeSnapshot scope,
            CancellationToken cancellationToken)
        {
            ProjectionReadCount++;
            return Task.FromResult(ProjectionResult);
        }

        public Task RecordAuditAsync(ManagementDashboardAuditRecord record, CancellationToken cancellationToken)
        {
            Audits.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
