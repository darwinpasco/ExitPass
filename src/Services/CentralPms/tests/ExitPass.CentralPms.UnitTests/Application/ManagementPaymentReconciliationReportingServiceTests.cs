using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPaymentReconciliationReportingServiceTests
{
    private static readonly Guid UserId = Guid.Parse("93300000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93300000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93300000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("93300000-0000-0000-0000-000000000201");
    private static readonly Guid CorrelationId = Guid.Parse("93300000-0000-0000-0000-000000000301");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-22T08:00:00Z");

    [Fact]
    public async Task Summary_AggregatesExactPhpStatusChannelAndProviderValues()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved(
                attempts:
                [
                    Aggregate("PHP", "REQUESTED", "CASH", "CASH", "CASH", 2, 200.20m),
                    Aggregate("PHP", "CONFIRMED", "QRPH", "QRPH", "PAYMONGO", 1, 100.10m),
                    Aggregate("PHP", "FUTURE_STATUS", "CARD", "CARD", "PAYMONGO", 1, 12.34m)
                ],
                confirmations:
                [
                    Aggregate("PHP", "RECORDED", "CASH", "CASH", "CASH", 1, 100.10m),
                    Aggregate("PHP", "RECORDED", "QRPH", "QRPH", "PAYMONGO", 1, 100.10m),
                    Aggregate("PHP", "VOIDED", "CARD", "CARD", "PAYMONGO", 1, 12.34m)
                ],
                outcomes:
                [Aggregate("PHP", "CONFIRMED", "QRPH", "QRPH", "PAYMONGO", 1, 100.10m)])
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.Success);
        result.Value!.CurrencySummaries.Should().BeEquivalentTo(
            [
                new ManagementPaymentCurrencySummary("PHP", 4, 312.64m, 2, 200.20m)
            ],
            options => options.WithStrictOrdering());
        result.Value.PaymentAttemptSummaries.Should().Contain(summary =>
            summary.Status == "OTHER" && summary.CurrencyCode == "PHP" && summary.Amount == 12.34m);
        result.Value.ChannelSummaries.Should().Contain(summary =>
            summary.ChannelCode == "CASH" && summary.ChannelType == "CASH");
        result.Value.ProviderSummaries.Should().Contain(summary =>
            summary.ProviderCode == "PAYMONGO" && summary.VerifiedOutcomeCount == 1);
        result.Value.Should().NotBeNull();
        result.Value!.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("TotalAmount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Summary_AttemptsAreNotAutomaticallyConfirmedAndPendingIsNotAnException()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved(attempts: [Aggregate("PHP", "PENDING_PROVIDER", "QRPH", "QRPH", "PAYMONGO", 3, 450m)])
        };

        var report = (await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None)).Value!;

        report.CurrencySummaries.Should().ContainSingle(summary =>
            summary.AttemptCount == 3 && summary.ConfirmedCount == 0 && summary.ConfirmedAmount == 0m);
        report.InternalReconciliationSummaries.Should().OnlyContain(summary => summary.Count == 0);
    }

    [Fact]
    public async Task Summary_MapsEverySupportedInternalConditionWithCurrencySeparatedAmounts()
    {
        var conditions = new[]
        {
            Condition(ManagementPaymentReconciliationReportingValues.AmountMismatch, "PHP", 2, 5.25m),
            Condition(ManagementPaymentReconciliationReportingValues.CurrencyMismatch, null, 1, null),
            Condition(ManagementPaymentReconciliationReportingValues.DuplicateProviderReference, null, 2, null),
            Condition(ManagementPaymentReconciliationReportingValues.ConfirmedOutcomeWithoutConfirmation, "PHP", 1, 75m),
            Condition(ManagementPaymentReconciliationReportingValues.ConfirmationAttemptStatusInconsistent, "PHP", 1, 9.99m)
        };
        var repository = new FakeRepository { ReadResult = Resolved(conditions: conditions) };

        var report = (await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None)).Value!;

        report.InternalReconciliationSummaries.Should().HaveCount(5);
        report.InternalReconciliationSummaries.Should().OnlyContain(summary => summary.Count > 0);
        report.InternalReconciliationSummaries.Single(summary =>
                summary.CategoryId == ManagementPaymentReconciliationReportingValues.AmountMismatch)
            .Amounts.Should().ContainSingle(amount => amount.CurrencyCode == "PHP" && amount.Amount == 5.25m);
        report.InternalReconciliationSummaries.Single(summary =>
                summary.CategoryId == ManagementPaymentReconciliationReportingValues.CurrencyMismatch)
            .Amounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Summary_RejectsNonPhpSourceDataAndAuditsSourceFailure()
    {
        var repository = new FakeRepository
        {
            ReadResult = Resolved(attempts: [Aggregate("USD", "REQUESTED", "CARD", "CARD", "PAYMONGO", 1, 12.34m)])
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.SourceUnavailable);
        result.Value.Should().BeNull();
        repository.Audits.Should().ContainSingle(audit =>
            audit.Result == "FAILED" && audit.ReasonCode == "UNSUPPORTED_CURRENCY_SOURCE_DATA");
    }

    [Theory]
    [InlineData(null, "2026-08-02T00:00:00Z", ManagementPaymentReconciliationReportingValues.InvalidPeriodStart)]
    [InlineData("", "2026-08-02T00:00:00Z", ManagementPaymentReconciliationReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T00:00:00", "2026-08-02T00:00:00Z", ManagementPaymentReconciliationReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T08:00:00+08:00", "2026-08-02T00:00:00Z", ManagementPaymentReconciliationReportingValues.InvalidPeriodStart)]
    [InlineData("2026-08-01T00:00:00Z", null, ManagementPaymentReconciliationReportingValues.InvalidPeriodEnd)]
    [InlineData("2026-08-02T00:00:00Z", "2026-08-01T00:00:00Z", ManagementPaymentReconciliationReportingValues.InvalidPeriodRange)]
    [InlineData("2026-08-01T00:00:00Z", "2026-09-02T00:00:00Z", ManagementPaymentReconciliationReportingValues.PeriodTooLong)]
    public async Task Summary_RejectsMissingAmbiguousOrInvalidPeriods(string? start, string? end, string errorCode)
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(),
            Query(start: start, end: end),
            CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.InvalidPeriod);
        result.ErrorCode.Should().Be(errorCode);
        repository.ReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit => audit.Result == "DENIED");
    }

    [Fact]
    public async Task Summary_AllowsExactlyThirtyOneDaysAndPreservesHalfOpenBounds()
    {
        var repository = new FakeRepository();
        const string start = "2026-08-01T00:00:00.0000000Z";
        const string end = "2026-09-01T00:00:00.0000000Z";

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(), Query(start: start, end: end), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.Success);
        repository.LastPeriodStart.Should().Be(DateTimeOffset.Parse(start));
        repository.LastPeriodEnd.Should().Be(DateTimeOffset.Parse(end));
        result.Value!.PeriodStart.Should().Be(DateTimeOffset.Parse(start));
        result.Value.PeriodEnd.Should().Be(DateTimeOffset.Parse(end));
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

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.InvalidScope);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.InvalidScopeType);
        repository.ScopeReadCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Summary_RequiresExplicitNonEmptyScopeReference(string? reference)
    {
        var repository = new FakeRepository();
        Guid? scopeReference = reference is null ? null : Guid.Parse(reference);

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(),
            new ManagementPaymentReconciliationQuery(
                "SITE",
                scopeReference,
                "2026-08-01T00:00:00Z",
                "2026-08-02T00:00:00Z",
                CorrelationId),
            CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.InvalidScope);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.InvalidScopeReference);
        repository.ScopeReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_UsesServerResolvedSiteGroupScope()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(
                ManagementDashboardScopeReadStatus.Resolved,
                Scope("SITE_GROUP", SiteGroupId, "Synthetic Group"))
        };

        var result = await CreateService(repository).GetSummaryAsync(
            Actor(), Query("site-group", SiteGroupId), CancellationToken.None);

        result.Value!.RequestedScope.ScopeType.Should().Be("SITE_GROUP");
        result.Value.EffectiveScope.ScopeReference.Should().Be(SiteGroupId);
        repository.LastScopePermission.Should().Be(ManagementPaymentReconciliationReportingValues.Permission);
    }

    [Fact]
    public async Task Summary_WhenScopeDenied_UsesConcealedOutcomeAndAuditsDenial()
    {
        var repository = new FakeRepository
        {
            ScopeResult = new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Denied, null)
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.ScopeNotFoundOrDenied);
        result.ErrorCode.Should().Be(ManagementDashboardReportingValues.ScopeNotFoundOrDenied);
        repository.ReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit => audit.ReasonCode == "SCOPE_NOT_FOUND_OR_DENIED");
    }

    [Fact]
    public async Task Summary_WhenFeatureDisabled_FailsClosedAndAudits()
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository, reportEnabled: false)
            .GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.FeatureDisabled);
        result.ErrorCode.Should().Be(ManagementPaymentReconciliationReportingValues.FeatureDisabled);
        repository.ReadCount.Should().Be(0);
        repository.Audits.Should().ContainSingle(audit => audit.ReasonCode == "FEATURE_DISABLED");
    }

    [Fact]
    public async Task Summary_WhenSessionIsStaleOrAccountRevoked_ReturnsSessionInvalidBeforeReading()
    {
        var repository = new FakeRepository { ActorValidation = ManagementDashboardActorValidationStatus.Invalid };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.SessionInvalid);
        repository.ScopeReadCount.Should().Be(0);
        repository.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_WhenCanonicalSourceFails_ReturnsRetryableUnavailableAndAuditsFailure()
    {
        var repository = new FakeRepository
        {
            ReadResult = new ManagementPaymentReconciliationReadResult(ManagementPaymentReconciliationReadStatus.Unavailable, null)
        };

        var result = await CreateService(repository).GetSummaryAsync(Actor(), Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPaymentReconciliationOutcome.SourceUnavailable);
        result.Retryable.Should().BeTrue();
        repository.Audits.Should().ContainSingle(audit => audit.EventType == "MANAGEMENT_PAYMENT_RECONCILIATION_SOURCE_FAILURE");
    }

    [Fact]
    public async Task Summary_EmptyPeriodIsExplicitNoActivityNotUnavailableOrFabricatedTotals()
    {
        var report = (await CreateService(new FakeRepository()).GetSummaryAsync(Actor(), Query(), CancellationToken.None)).Value!;

        report.Availability.Should().Be(ManagementDashboardReportingValues.Available);
        report.Freshness.Should().Be(ManagementDashboardReportingValues.NotApplicable);
        report.DataAsOf.Should().BeNull();
        report.CurrencySummaries.Should().BeEmpty();
        report.Warnings.Should().Contain(ManagementPaymentReconciliationReportingValues.NoActivity);
    }

    [Fact]
    public void PaymentReportPolicy_ReusesReadOnlyReconciliationPermissionOnly()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPaymentReconciliationReportingValues.Policy)
            .Should().Equal(ManagementPaymentReconciliationReportingValues.Permission);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPaymentReconciliationReportingValues.Policy)
            .Should().NotContain(["dashboard.view", "reconciliation.manage", "payment.manage", "reports.export"]);
    }

    private static ManagementPaymentReconciliationReportingService CreateService(
        FakeRepository repository,
        bool dashboardEnabled = true,
        bool reportEnabled = true) =>
        new(
            repository,
            new ManagementDashboardReportingOptions { Enabled = dashboardEnabled },
            new ManagementPaymentReconciliationReportingOptions { Enabled = reportEnabled },
            new FixedTimeProvider(Now));

    private static ManagementDashboardActor Actor() => new(UserId, SessionId);

    private static ManagementPaymentReconciliationQuery Query(
        string? scopeType = "SITE",
        Guid? reference = null,
        string? start = "2026-08-01T00:00:00Z",
        string? end = "2026-08-02T00:00:00Z") =>
        new(scopeType, reference ?? SiteId, start, end, CorrelationId);

    private static ManagementDashboardScopeSnapshot Scope(
        string type = "SITE",
        Guid? reference = null,
        string name = "Synthetic Site") =>
        new(
            type,
            reference ?? SiteId,
            name,
            Now.AddMinutes(-1),
            [new ManagementDashboardSiteSnapshot(SiteId, "ACTIVE", true, Now.AddMinutes(-1))]);

    private static ManagementPaymentAggregateRecord Aggregate(
        string currency,
        string status,
        string channel,
        string channelType,
        string provider,
        long count,
        decimal amount) =>
        new(currency, status, channel, channelType, provider, count, amount);

    private static ManagementPaymentReconciliationConditionRecord Condition(
        string category,
        string? currency,
        long count,
        decimal? amount) =>
        new(category, currency, count, amount);

    private static ManagementPaymentReconciliationReadResult Resolved(
        IReadOnlyList<ManagementPaymentAggregateRecord>? attempts = null,
        IReadOnlyList<ManagementPaymentAggregateRecord>? confirmations = null,
        IReadOnlyList<ManagementPaymentAggregateRecord>? outcomes = null,
        IReadOnlyList<ManagementPaymentReconciliationConditionRecord>? conditions = null) =>
        new(
            ManagementPaymentReconciliationReadStatus.Resolved,
            new ManagementPaymentReconciliationSourceSnapshot(
                attempts ?? [],
                confirmations ?? [],
                outcomes ?? [],
                conditions ?? [],
                attempts?.Count > 0 || confirmations?.Count > 0 || outcomes?.Count > 0 ? Now.AddMinutes(-1) : null));

    private sealed class FakeRepository : IManagementPaymentReconciliationReportingRepository
    {
        public ManagementDashboardActorValidationStatus ActorValidation { get; init; } = ManagementDashboardActorValidationStatus.Valid;
        public ManagementDashboardScopeReadResult ScopeResult { get; init; } =
            new(ManagementDashboardScopeReadStatus.Resolved, Scope());
        public ManagementPaymentReconciliationReadResult ReadResult { get; init; } = Resolved();
        public int ScopeReadCount { get; private set; }
        public int ReadCount { get; private set; }
        public string? LastScopePermission { get; private set; }
        public DateTimeOffset? LastPeriodStart { get; private set; }
        public DateTimeOffset? LastPeriodEnd { get; private set; }
        public List<ManagementPaymentReconciliationAuditRecord> Audits { get; } = [];

        public Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
            ManagementDashboardActor actor,
            CancellationToken cancellationToken) => Task.FromResult(ActorValidation);

        public Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
            ManagementDashboardActor actor,
            string scopeType,
            Guid scopeReference,
            CancellationToken cancellationToken)
        {
            ScopeReadCount++;
            LastScopePermission = ManagementPaymentReconciliationReportingValues.Permission;
            return Task.FromResult(ScopeResult);
        }

        public Task<ManagementPaymentReconciliationReadResult> ReadSummaryAsync(
            ManagementDashboardScopeSnapshot scope,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            LastPeriodStart = periodStart;
            LastPeriodEnd = periodEnd;
            return Task.FromResult(ReadResult);
        }

        public Task RecordAuditAsync(ManagementPaymentReconciliationAuditRecord record, CancellationToken cancellationToken)
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
