using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementFiscalExceptionReportingServiceTests
{
    private static readonly Guid UserId = Guid.Parse("93500000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93500000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93500000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("93500000-0000-0000-0000-000000000201");
    private static readonly Guid CorrelationId = Guid.Parse("93500000-0000-0000-0000-000000000301");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-22T08:00:00Z");

    [Fact]
    public async Task Summary_NormalizesLifecycleAndSupportedExceptionsWithoutTreatingPendingAsException()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved(
            [
                Record("PENDING_FISCAL_ISSUANCE", "PHP", 2, 200m),
                Record("FISCAL_ISSUANCE_RECORDED", "PHP", 1, 100m),
                Record("FISCAL_ISSUANCE_FAILED_SERVICE", "PHP", 1, 50m),
                Record("FISCAL_ISSUANCE_CONFLICT", "PHP", 1, 12.34m),
                Record("FISCAL_ISSUANCE_UNKNOWN", "PHP", 1, 9.99m),
                Record("FUTURE_STATE", "PHP", 1, 1m)
            ])
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementFiscalExceptionOutcome.Success);
        var report = result.Value!;
        report.LifecycleSummaries.Should().Contain(summary => summary.LifecycleState == "PENDING" && summary.Count == 2);
        report.LifecycleSummaries.Should().Contain(summary => summary.LifecycleState == "ISSUED" && summary.Count == 1);
        report.LifecycleSummaries.Should().Contain(summary => summary.LifecycleState == "OTHER" && summary.Count == 1);
        report.ExceptionSummaries.Single(summary => summary.CategoryId == ManagementFiscalExceptionReportingValues.IssuanceFailed)
            .Count.Should().Be(1);
        report.ExceptionSummaries.Single(summary => summary.CategoryId == ManagementFiscalExceptionReportingValues.ReferenceConflict)
            .Count.Should().Be(1);
        report.ExceptionSummaries.Single(summary => summary.CategoryId == ManagementFiscalExceptionReportingValues.OutcomeUnavailable)
            .Count.Should().Be(1);
        report.ExceptionSummaries.Sum(summary => summary.Count).Should().Be(3, "pending work is not automatically exceptional");
        report.Availability.Should().Be(ManagementDashboardReportingValues.Partial);
    }

    [Fact]
    public async Task Summary_UsesExactPhpExpectedIssuanceAmounts()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved(
            [
                Record("FISCAL_ISSUANCE_RECORDED", "PHP", 2, 200.20m),
                Record("FISCAL_ISSUANCE_FAILED_REQUEST", "PHP", 1, 50.05m),
                Record("FISCAL_ISSUANCE_RECORDED", "PHP", 1, 999999999999.99m)
            ])
        };

        var report = (await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None)).Value!;

        report.CurrencySummaries.Should().BeEquivalentTo(
        [
            new ManagementFiscalCurrencySummary("PHP", 4, 1000000000250.24m, 3, 1)
        ], options => options.WithStrictOrdering());
        report.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("GrandTotal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Summary_RejectsNonPhpSourceDataAndAuditsSourceFailure()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved([Record("FISCAL_ISSUANCE_RECORDED", "USD", 1, 12.34m)])
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementFiscalExceptionOutcome.SourceUnavailable);
        result.Value.Should().BeNull();
        repository.Audits.Should().ContainSingle(audit =>
            audit.Result == "FAILED" && audit.ReasonCode == "UNSUPPORTED_CURRENCY_SOURCE_DATA");
    }

    [Theory]
    [InlineData(null, "2026-08-02T00:00:00Z", ManagementFiscalExceptionReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T00:00:00", "2026-08-02T00:00:00Z", ManagementFiscalExceptionReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T08:00:00+08:00", "2026-08-02T00:00:00Z", ManagementFiscalExceptionReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T00:00:00Z", null, ManagementFiscalExceptionReportingValues.InvalidPeriodEnd)]
    [InlineData("2026-08-02T00:00:00Z", "2026-08-02T00:00:00Z", ManagementFiscalExceptionReportingValues.InvalidPeriodRange)]
    [InlineData("2026-08-03T00:00:00Z", "2026-08-02T00:00:00Z", ManagementFiscalExceptionReportingValues.InvalidPeriodRange)]
    [InlineData("2026-08-01T00:00:00Z", "2026-09-02T00:00:00Z", ManagementFiscalExceptionReportingValues.PeriodTooLong)]
    public async Task Summary_RejectsMissingAmbiguousOrInvalidPeriods(string? start, string? end, string errorCode)
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(), Query(start: start, end: end), CancellationToken.None);

        result.Outcome.Should().Be(ManagementFiscalExceptionOutcome.InvalidPeriod);
        result.ErrorCode.Should().Be(errorCode);
        repository.ReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit => audit.Result == "DENIED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GLOBAL")]
    [InlineData("PORTFOLIO")]
    public async Task Summary_RequiresExplicitSiteOrSiteGroup(string? scopeType)
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(), Query(scopeType: scopeType), CancellationToken.None);

        result.Outcome.Should().Be(ManagementFiscalExceptionOutcome.InvalidScope);
        repository.ScopeReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_UsesServerResolvedSiteGroupScopeAndCanonicalPermission()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(
                ManagementDashboardScopeReadStatus.Resolved,
                Scope("SITE_GROUP", SiteGroupId, "Synthetic Group"))
        };

        var report = (await CreateService(repository).GetSummaryAsync(
            Actor(), Query("site-group", SiteGroupId), CancellationToken.None)).Value!;

        report.EffectiveScope.ScopeReference.Should().Be(SiteGroupId);
        repository.LastScopePermission.Should().Be(ManagementFiscalExceptionReportingValues.Permission);
    }

    [Fact]
    public async Task Summary_DeniedScopeIsConcealedAndAudited()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Denied, null)
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementFiscalExceptionOutcome.ScopeNotFoundOrDenied);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.ScopeNotFoundOrDenied);
        repository.ReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit => audit.ReasonCode == "SCOPE_NOT_FOUND_OR_DENIED");
    }

    [Fact]
    public async Task Summary_FeatureAndParentFeatureDefaultFailClosed()
    {
        var repository = new FakeRepository();

        var reportDisabled = await CreateService(repository, reportEnabled: false)
            .GetSummaryAsync(Actor(), Query(), CancellationToken.None);
        var parentDisabled = await CreateService(new FakeRepository(), dashboardEnabled: false)
            .GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        reportDisabled.Outcome.Should().Be(ManagementFiscalExceptionOutcome.FeatureDisabled);
        parentDisabled.Outcome.Should().Be(ManagementFiscalExceptionOutcome.FeatureDisabled);
        repository.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_InvalidSessionOrSourceFailsClosed()
    {
        var invalidSession = new FakeRepository { ActorValidation = ManagementDashboardActorValidationStatus.Invalid };
        var failedSource = new FakeRepository
        {
            ReadResult = new ManagementFiscalExceptionReadResult(ManagementFiscalExceptionReadStatus.Unavailable, null)
        };

        var sessionResult = await CreateService(invalidSession).GetSummaryAsync(Actor(), Query(), CancellationToken.None);
        var sourceResult = await CreateService(failedSource).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        sessionResult.Outcome.Should().Be(ManagementFiscalExceptionOutcome.SessionInvalid);
        invalidSession.ScopeReadCount.Should().Be(0);
        sourceResult.Outcome.Should().Be(ManagementFiscalExceptionOutcome.SourceUnavailable);
        sourceResult.Retryable.Should().BeTrue();
        failedSource.Audits.Should().ContainSingle(audit => audit.EventType == "MANAGEMENT_FISCAL_EXCEPTION_SOURCE_FAILURE");
    }

    [Fact]
    public async Task Summary_EmptyCohortIsExplicitNoActivityWithUnavailableFactsNotZeros()
    {
        var report = (await CreateService(new FakeRepository()).GetSummaryAsync(Actor(), Query(), CancellationToken.None)).Value!;

        report.Availability.Should().Be(ManagementFiscalExceptionReportingValues.NoActivityAvailability);
        report.Freshness.Should().Be(ManagementDashboardReportingValues.NotApplicable);
        report.DataAsOf.Should().BeNull();
        report.CurrencySummaries.Should().BeEmpty();
        report.Warnings.Should().Contain(ManagementFiscalExceptionReportingValues.NoActivity);
        report.UnavailableFacts.Should().Contain("OVERDUE_DETECTION_UNAVAILABLE");
    }

    [Fact]
    public void Policy_ReusesSalesInvoiceReportingPermissionOnly()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementFiscalExceptionReportingValues.Policy)
            .Should().Equal(ManagementFiscalExceptionReportingValues.Permission);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementFiscalExceptionReportingValues.Policy)
            .Should().NotContain(["dashboard.view", "reconciliation.view", "fiscal-issuance.status.read", "reports.export"]);
    }

    private static ManagementFiscalExceptionReportingService CreateService(
        FakeRepository repository,
        bool dashboardEnabled = true,
        bool reportEnabled = true) =>
        new(
            repository,
            new ManagementDashboardReportingOptions { Enabled = dashboardEnabled },
            new ManagementFiscalExceptionReportingOptions { Enabled = reportEnabled },
            new FixedTimeProvider(Now));

    private static ManagementDashboardActor Actor() => new(UserId, SessionId);

    private static ManagementFiscalExceptionQuery Query(
        string? scopeType = "SITE",
        Guid? reference = null,
        string? start = "2026-08-01T00:00:00Z",
        string? end = "2026-08-02T00:00:00Z") =>
        new(scopeType, reference ?? SiteId, start, end, CorrelationId);

    private static ManagementDashboardScopeSnapshot Scope(
        string type = "SITE",
        Guid? reference = null,
        string name = "Synthetic Site") =>
        new(type, reference ?? SiteId, name, Now.AddMinutes(-1),
            [new ManagementDashboardSiteSnapshot(SiteId, "ACTIVE", true, Now.AddMinutes(-1))]);

    private static ManagementFiscalAggregateRecord Record(string state, string currency, long count, decimal amount) =>
        new(state, currency, count, amount);

    private static ManagementFiscalExceptionReadResult Resolved(
        IReadOnlyList<ManagementFiscalAggregateRecord>? records = null) =>
        new(
            ManagementFiscalExceptionReadStatus.Resolved,
            new ManagementFiscalExceptionSourceSnapshot(
                records ?? [],
                records?.Count > 0 ? Now.AddMinutes(-1) : null));

    private sealed class FakeRepository : IManagementFiscalExceptionReportingRepository
    {
        public ManagementDashboardActorValidationStatus ActorValidation { get; init; } = ManagementDashboardActorValidationStatus.Valid;
        public ManagementDashboardScopeReadResult ScopeResult { get; init; } =
            new(ManagementDashboardScopeReadStatus.Resolved, Scope());
        public ManagementFiscalExceptionReadResult ReadResult { get; init; } = Resolved();
        public int ScopeReadCount { get; private set; }
        public int ReadCount { get; private set; }
        public string? LastScopePermission { get; private set; }
        public List<ManagementFiscalExceptionAuditRecord> Audits { get; } = [];

        public Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
            ManagementDashboardActor actor, CancellationToken cancellationToken) => Task.FromResult(ActorValidation);

        public Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
            ManagementDashboardActor actor, string scopeType, Guid scopeReference, CancellationToken cancellationToken)
        {
            ScopeReadCount++;
            LastScopePermission = ManagementFiscalExceptionReportingValues.Permission;
            return Task.FromResult(ScopeResult);
        }

        public Task<ManagementFiscalExceptionReadResult> ReadSummaryAsync(
            ManagementDashboardScopeSnapshot scope,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(ReadResult);
        }

        public Task RecordAuditAsync(ManagementFiscalExceptionAuditRecord record, CancellationToken cancellationToken)
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
