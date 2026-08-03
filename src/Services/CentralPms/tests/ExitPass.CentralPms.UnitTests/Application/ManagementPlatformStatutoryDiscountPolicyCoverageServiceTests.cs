using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformStatutoryDiscountPolicyCoverageServiceTests
{
    private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000101");
    private static readonly Guid SiteTwoId = Guid.Parse("77000000-0000-0000-0000-000000000102");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset EvaluationInstant = DateTimeOffset.Parse("2026-07-30T08:00:00Z");

    [Fact]
    public void PolicyCatalog_MapsManagementPlatformCoveragePolicyToNarrowViewPermission()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPlatformStatutoryDiscountPolicyCoverageValues.PolicyName)
            .Should()
            .Equal(ManagementPlatformStatutoryDiscountPolicyCoverageValues.Permission);

        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPlatformStatutoryDiscountPolicyCoverageValues.PolicyName)
            .Should()
            .NotContain("statutory-discounts.policy.resolve");
    }

    [Fact]
    public void CoverageDtos_DoNotExposeActorOrSecretFields()
    {
        var propertyNames = new[]
        {
            typeof(ManagementPlatformStatutoryDiscountPolicyCoverageResponse),
            typeof(ManagementPlatformStatutoryDiscountPolicyCoverageRowDto)
        }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("Api" + "Key", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("Actor", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(name => name.Contains("Permission", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadCoverageAsync_WhenBothEntitlementsCovered_ReturnsSeparateActiveRows()
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site()))
            .WithCandidates(
            [
                Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen, policyCode: "SC-ACTIVE"),
                Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd, policyCode: "PWD-ACTIVE")
            ]);
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.Success);
        result.Coverage!.CoverageRows.Should().HaveCount(2);
        result.Coverage.CoverageRows.Should().Contain(row =>
            row.EntitlementType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen &&
            row.CoverageClassification == ManagementPlatformStatutoryDiscountPolicyCoverageValues.ActiveCovered &&
            row.AuthoritativeCoverageAvailable);
        result.Coverage.CoverageRows.Should().Contain(row =>
            row.EntitlementType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd &&
            row.PolicyReference == "PWD-ACTIVE");
    }

    [Fact]
    public async Task ReadCoverageAsync_WhenSeniorOnlyCovered_DoesNotCollapsePwdIntoCoverage()
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site()))
            .WithCandidates([Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen)]);
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(), CancellationToken.None);

        result.Coverage!.CoverageRows.Single(row => row.EntitlementType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen)
            .CoverageClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.ActiveCovered);
        result.Coverage.CoverageRows.Single(row => row.EntitlementType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd)
            .CoverageClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicablePolicy);
    }

    [Theory]
    [InlineData("2026-08-01", "", "ACTIVE", ManagementPlatformStatutoryDiscountPolicyCoverageValues.FutureEffective)]
    [InlineData("2026-01-01", "2026-07-01", "ACTIVE", ManagementPlatformStatutoryDiscountPolicyCoverageValues.Expired)]
    [InlineData("2026-01-01", "", "INACTIVE", ManagementPlatformStatutoryDiscountPolicyCoverageValues.Inactive)]
    public async Task ReadCoverageAsync_ClassifiesNonActiveCoverageStates(
        string effectiveFrom,
        string effectiveTo,
        string status,
        string expectedClassification)
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site()))
            .WithCandidates([
                Candidate(
                    ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen,
                    status,
                    DateOnly.Parse(effectiveFrom),
                    string.IsNullOrWhiteSpace(effectiveTo) ? null : DateOnly.Parse(effectiveTo))
            ]);
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(entitlementType: ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen), CancellationToken.None);

        result.Coverage!.CoverageRows.Single().CoverageClassification.Should().Be(expectedClassification);
        result.Coverage.CoverageRows.Single().AuthoritativeCoverageAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ReadCoverageAsync_WhenSiteHasNoJurisdiction_ReturnsNoApplicableOrdinance()
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site(lguCode: null)));
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(entitlementType: ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen), CancellationToken.None);

        result.Coverage!.CoverageRows.Single().CoverageClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicableOrdinance);
        repository.ReadPolicyCandidateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadCoverageAsync_WhenPolicyRecordMissingRequiredFields_ReturnsMalformed()
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site()))
            .WithCandidates([Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd) with { PolicyCode = null }]);
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(entitlementType: ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd), CancellationToken.None);

        result.Coverage!.CoverageRows.Single().CoverageClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.MalformedAuthoritativeRecord);
        result.Coverage.CoverageRows.Single().DataQualityClassification.Should().Be("MALFORMED");
    }

    [Theory]
    [InlineData(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Denied, ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeDenied)]
    [InlineData(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.NotFound, ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeNotFound)]
    [InlineData(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Empty, ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.EmptyGovernedScope)]
    [InlineData(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable, ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.OrdinanceSourceUnavailable)]
    [InlineData(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Malformed, ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.MalformedAuthoritativeData)]
    public async Task ReadCoverageAsync_MapsScopeFailureWithoutPolicyReads(
        ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus scopeStatus,
        ManagementPlatformStatutoryDiscountPolicyCoverageOutcome expectedOutcome)
    {
        var repository = new FakeRepository()
            .WithScope(new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(scopeStatus, null, []));
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(), CancellationToken.None);

        result.Outcome.Should().Be(expectedOutcome);
        repository.ReadPolicyCandidateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReadCoverageAsync_WhenSiteGroupResolved_ReturnsRowsOnlyForGovernedSites()
    {
        var repository = new FakeRepository()
            .WithScope(ResolvedScope(Site(), Site(SiteTwoId, "Second Site")))
            .WithCandidates([
                Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen, siteId: SiteId),
                Candidate(ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd, siteId: SiteTwoId)
            ]);
        var service = CreateService(repository);

        var result = await service.ReadCoverageAsync(Query(
            scopeType: ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSiteGroup,
            entitlementType: null), CancellationToken.None);

        result.Coverage!.CoverageRows.Select(row => row.SiteReference).Should().OnlyContain(id => id == SiteId || id == SiteTwoId);
        result.Coverage.CoverageRows.Should().HaveCount(4);
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageService CreateService(FakeRepository repository) =>
        new(repository, new FixedTimeProvider(EvaluationInstant));

    private static ManagementPlatformStatutoryDiscountPolicyCoverageQuery Query(
        string scopeType = ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite,
        string? entitlementType = null) =>
        new(scopeType, scopeType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite ? SiteId : SiteGroupId, entitlementType, true, Guid.NewGuid(), UserId);

    private static ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult ResolvedScope(params ManagementPlatformStatutoryDiscountPolicyCoverageSite[] sites) =>
        new(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved, "Synthetic Scope", sites);

    private static ManagementPlatformStatutoryDiscountPolicyCoverageSite Site(
        Guid? siteId = null,
        string? siteName = "Synthetic Site",
        string? lguCode = "QUEZON_CITY") =>
        new(siteId ?? SiteId, SiteGroupId, siteName, "Synthetic Group", lguCode);

    private static ManagementPlatformStatutoryDiscountPolicyCoverageCandidate Candidate(
        string entitlementType,
        string status = "ACTIVE",
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        string policyCode = "POLICY-ACTIVE",
        Guid? siteId = null) =>
        new(
            siteId ?? SiteId,
            entitlementType,
            Guid.NewGuid(),
            policyCode,
            "Synthetic statutory policy",
            status,
            "ACTIVE_APPROVED",
            "LOCAL_ORDINANCE",
            "LOCAL_ORDINANCE_APPLIED",
            "QC-ORD-001",
            "QC-ORD-001",
            null,
            effectiveFrom ?? DateOnly.Parse("2026-01-01"),
            effectiveTo,
            "synthetic-policy-v1",
            EvaluationInstant,
            "UNIT_TEST");

    private sealed class FakeRepository : IManagementPlatformStatutoryDiscountPolicyCoverageRepository
    {
        private ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult _scope = ResolvedScope(Site());
        private IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> _candidates = [];

        public int ReadPolicyCandidateCallCount { get; private set; }

        public FakeRepository WithScope(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult scope)
        {
            _scope = scope;
            return this;
        }

        public FakeRepository WithCandidates(IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> candidates)
        {
            _candidates = candidates;
            return this;
        }

        public Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveScopeAsync(
            Guid? actorUserId,
            string scopeType,
            Guid scopeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_scope);

        public Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveServiceSiteScopeAsync(
            Guid siteId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_scope);

        public Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadPolicyCandidatesAsync(
            IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
            IReadOnlyList<string> entitlementTypes,
            bool includeInactive,
            DateOnly evaluationDate,
            CancellationToken cancellationToken)
        {
            ReadPolicyCandidateCallCount++;
            return Task.FromResult(_candidates);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
