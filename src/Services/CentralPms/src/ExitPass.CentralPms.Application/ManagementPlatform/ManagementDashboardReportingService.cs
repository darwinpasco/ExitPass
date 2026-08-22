namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementDashboardReportingService : IManagementDashboardReportingService
{
    private const string SiteRegistrySource = "CENTRAL_PMS_SITE_REGISTRY";
    private const string ProjectionSource = "CENTRAL_PMS_VENDOR_SESSION_PROJECTION";
    private const string DeferredSource = "PHASE_1_SOURCE_NOT_APPROVED";

    private readonly IManagementDashboardReportingRepository _repository;
    private readonly ManagementDashboardReportingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ManagementDashboardReportingService(
        IManagementDashboardReportingRepository repository,
        ManagementDashboardReportingOptions options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<ManagementDashboardReportingResult<ManagementDashboardCatalog>> GetCatalogAsync(
        ManagementDashboardActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ManagementDashboardReportingResult<ManagementDashboardCatalog>.Failed(
                ManagementDashboardReportingOutcome.FeatureDisabled,
                correlationId,
                ManagementDashboardReportingValues.FeatureDisabled,
                "Management Dashboard and Reporting is not enabled for this environment.");
        }

        var actorValidation = await _repository.ValidateActorAsync(actor, cancellationToken);
        if (actorValidation == ManagementDashboardActorValidationStatus.SourceUnavailable)
        {
            return SourceUnavailable<ManagementDashboardCatalog>(correlationId);
        }

        if (actorValidation != ManagementDashboardActorValidationStatus.Valid)
        {
            return SessionInvalid<ManagementDashboardCatalog>(correlationId);
        }

        var now = _timeProvider.GetUtcNow();
        var catalog = new ManagementDashboardCatalog(
            ManagementDashboardReportingValues.ContractVersion,
            now,
            BuildCatalog());

        try
        {
            await _repository.RecordAuditAsync(
                Audit(
                    "MANAGEMENT_DASHBOARD_CATALOG_READ",
                    "SUCCESS",
                    "CATALOG_RETURNED",
                    "dashboard-catalog",
                    actor,
                    null,
                    null,
                    ManagementDashboardReportingValues.Available,
                    "CONTROLLED_REPORT_CATALOG",
                    correlationId,
                    now),
                cancellationToken);
        }
        catch (ManagementDashboardSourceUnavailableException)
        {
            return SourceUnavailable<ManagementDashboardCatalog>(correlationId);
        }

        return ManagementDashboardReportingResult<ManagementDashboardCatalog>.Success(catalog, correlationId);
    }

    public async Task<ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>> GetOperationalOverviewAsync(
        ManagementDashboardActor actor,
        ManagementDashboardOperationalOverviewQuery query,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Failed(
                ManagementDashboardReportingOutcome.FeatureDisabled,
                query.CorrelationId,
                ManagementDashboardReportingValues.FeatureDisabled,
                "Management Dashboard and Reporting is not enabled for this environment.");
        }

        var actorValidation = await _repository.ValidateActorAsync(actor, cancellationToken);
        if (actorValidation == ManagementDashboardActorValidationStatus.SourceUnavailable)
        {
            return SourceUnavailable<ManagementDashboardOperationalOverview>(query.CorrelationId);
        }

        if (actorValidation != ManagementDashboardActorValidationStatus.Valid)
        {
            return SessionInvalid<ManagementDashboardOperationalOverview>(query.CorrelationId);
        }

        var scopeType = NormalizeScopeType(query.ScopeType);
        if (scopeType is null)
        {
            return ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Failed(
                ManagementDashboardReportingOutcome.InvalidScope,
                query.CorrelationId,
                ManagementDashboardReportingValues.InvalidScopeType,
                "An explicit SITE or SITE_GROUP scopeType is required.");
        }

        if (query.ScopeReference is null || query.ScopeReference == Guid.Empty)
        {
            return ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Failed(
                ManagementDashboardReportingOutcome.InvalidScope,
                query.CorrelationId,
                ManagementDashboardReportingValues.InvalidScopeReference,
                "An explicit scopeReference is required.");
        }

        var now = _timeProvider.GetUtcNow();
        var scope = await _repository.ResolveScopeAsync(
            actor,
            scopeType,
            query.ScopeReference.Value,
            cancellationToken);

        if (scope.Status == ManagementDashboardScopeReadStatus.SourceUnavailable)
        {
            await RecordFailureAuditAsync(
                actor,
                scopeType,
                query.ScopeReference,
                "QUERY_FAILED",
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return SourceUnavailable<ManagementDashboardOperationalOverview>(query.CorrelationId);
        }

        if (scope.Status != ManagementDashboardScopeReadStatus.Resolved || scope.Scope is null)
        {
            await RecordFailureAuditAsync(
                actor,
                scopeType,
                query.ScopeReference,
                "SCOPE_DENIED",
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken,
                result: "DENIED");
            return ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Failed(
                ManagementDashboardReportingOutcome.ScopeNotFoundOrDenied,
                query.CorrelationId,
                ManagementDashboardReportingValues.ScopeNotFoundOrDenied,
                "The requested dashboard scope was not found or is not available to the caller.");
        }

        var projection = await _repository.ReadProjectionHealthAsync(scope.Scope, cancellationToken);
        var siteSection = BuildSiteSection(scope.Scope);
        var projectionSections = BuildProjectionSections(projection, now);
        var sections = new[] { siteSection }.Concat(projectionSections).ToArray();
        var availability = sections.Any(section => section.Availability == ManagementDashboardReportingValues.Unavailable)
            ? ManagementDashboardReportingValues.Partial
            : sections.Any(section => section.Availability == ManagementDashboardReportingValues.Partial)
                ? ManagementDashboardReportingValues.Partial
                : ManagementDashboardReportingValues.Available;
        var freshness = sections.Any(section => section.Freshness == ManagementDashboardReportingValues.Stale)
            ? ManagementDashboardReportingValues.Stale
            : ManagementDashboardReportingValues.Current;
        var warnings = sections.SelectMany(section => section.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var dataAsOf = sections.Where(section => section.DataAsOf.HasValue).Select(section => section.DataAsOf!.Value).DefaultIfEmpty().Min();

        var overview = new ManagementDashboardOperationalOverview(
            ManagementDashboardReportingValues.ContractVersion,
            ManagementDashboardReportingValues.OperationalOverviewReportId,
            new ManagementDashboardScope(scopeType, query.ScopeReference.Value, scope.Scope.DisplayName),
            new ManagementDashboardScope(scope.Scope.ScopeType, scope.Scope.ScopeReference, scope.Scope.DisplayName),
            now,
            dataAsOf == default ? null : dataAsOf,
            availability,
            freshness,
            query.CorrelationId,
            sections,
            warnings,
            ["Phase 1 is operational visibility only and is not payment, fiscal, settlement, or exit authority."]);

        var projectionUnavailable = projection.Status == ManagementDashboardProjectionReadStatus.Unavailable;
        await _repository.RecordAuditAsync(
            Audit(
                projectionUnavailable ? "MANAGEMENT_DASHBOARD_SOURCE_UNAVAILABLE" : "MANAGEMENT_DASHBOARD_OVERVIEW_READ",
                "SUCCESS",
                projectionUnavailable ? ManagementDashboardReportingValues.ProjectionSourceUnavailable : "OVERVIEW_RETURNED",
                ManagementDashboardReportingValues.OperationalOverviewReportId,
                actor,
                scope.Scope.ScopeType,
                scope.Scope.ScopeReference,
                availability,
                projectionUnavailable ? ManagementDashboardReportingValues.Unavailable : freshness,
                query.CorrelationId,
                now),
            cancellationToken);

        return ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Success(overview, query.CorrelationId);
    }

    private async Task RecordFailureAuditAsync(
        ManagementDashboardActor actor,
        string? scopeType,
        Guid? scopeReference,
        string reason,
        string sourceClassification,
        Guid correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string result = "FAILED")
    {
        try
        {
            await _repository.RecordAuditAsync(
                Audit(
                    "MANAGEMENT_DASHBOARD_OVERVIEW_READ",
                    result,
                    reason,
                    ManagementDashboardReportingValues.OperationalOverviewReportId,
                    actor,
                    scopeType,
                    scopeReference,
                    ManagementDashboardReportingValues.Unavailable,
                    sourceClassification,
                    correlationId,
                    now),
                cancellationToken);
        }
        catch (ManagementDashboardSourceUnavailableException)
        {
            // The endpoint still returns the original safe failure; the API layer logs the dependency failure.
        }
    }

    private ManagementDashboardOverviewSection BuildSiteSection(ManagementDashboardScopeSnapshot scope)
    {
        var sites = scope.Sites;
        return new ManagementDashboardOverviewSection(
            "site-operational-status",
            "Site operational status",
            ManagementDashboardReportingValues.Available,
            ManagementDashboardReportingValues.Current,
            SiteRegistrySource,
            scope.DataAsOf,
            [
                Metric("sites-total", "Sites", sites.Count),
                Metric("sites-active", "Active Sites", sites.LongCount(site => site.Status == "ACTIVE")),
                Metric("sites-suspended", "Suspended Sites", sites.LongCount(site => site.Status == "SUSPENDED")),
                Metric("sites-payment-enabled", "Payment-enabled Sites", sites.LongCount(site => site.PaymentEnabled))
            ],
            [],
            ["Site status is configuration posture and does not prove connector, payment, fiscal, or gate availability."]);
    }

    private IReadOnlyList<ManagementDashboardOverviewSection> BuildProjectionSections(
        ManagementDashboardProjectionReadResult projection,
        DateTimeOffset now)
    {
        if (projection.Status == ManagementDashboardProjectionReadStatus.Unavailable)
        {
            return
            [
                UnavailableSection("connector-health", "Connector health"),
                UnavailableSection("vendor-projection-freshness", "Vendor projection freshness")
            ];
        }

        if (projection.Targets.Count == 0)
        {
            return
            [
                new ManagementDashboardOverviewSection(
                    "connector-health",
                    "Connector health",
                    ManagementDashboardReportingValues.NotApplicable,
                    ManagementDashboardReportingValues.NotApplicable,
                    ProjectionSource,
                    null,
                    [],
                    [ManagementDashboardReportingValues.ProjectionNotConfigured],
                    ["No vendor projection sync target is configured in the authorized scope."]),
                new ManagementDashboardOverviewSection(
                    "vendor-projection-freshness",
                    "Vendor projection freshness",
                    ManagementDashboardReportingValues.NotApplicable,
                    ManagementDashboardReportingValues.NotApplicable,
                    ProjectionSource,
                    null,
                    [],
                    [ManagementDashboardReportingValues.ProjectionNotConfigured],
                    ["No projection data is available because no sync target is configured."])
            ];
        }

        var latest = projection.Targets
            .Select(target => target.LatestProjectionAt ?? target.LastSuccessAt ?? target.LastAttemptAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();
        var staleCutoff = now.AddMinutes(-Math.Max(1, _options.ProjectionStaleAfterMinutes));
        var staleTargets = projection.Targets.LongCount(target =>
            target.Enabled && (!target.LatestProjectionAt.HasValue || target.LatestProjectionAt.Value < staleCutoff));
        var freshness = staleTargets > 0 ? ManagementDashboardReportingValues.Stale : ManagementDashboardReportingValues.Current;
        var availability = projection.Targets.Any(target => target.HealthStatus is "FAILING" or "UNKNOWN")
            ? ManagementDashboardReportingValues.Partial
            : ManagementDashboardReportingValues.Available;
        var warnings = staleTargets > 0 ? new[] { ManagementDashboardReportingValues.ProjectionStale } : [];

        return
        [
            new ManagementDashboardOverviewSection(
                "connector-health",
                "Connector health",
                availability,
                freshness,
                ProjectionSource,
                latest == default ? null : latest,
                [
                    Metric("connector-targets", "Configured targets", projection.Targets.Count),
                    Metric("connector-targets-enabled", "Enabled targets", projection.Targets.LongCount(target => target.Enabled)),
                    Metric("connector-targets-healthy", "Healthy targets", projection.Targets.LongCount(target => target.HealthStatus == "HEALTHY")),
                    Metric("connector-targets-degraded", "Degraded targets", projection.Targets.LongCount(target => target.HealthStatus == "DEGRADED")),
                    Metric("connector-targets-failing", "Failing targets", projection.Targets.LongCount(target => target.HealthStatus == "FAILING"))
                ],
                warnings,
                ["Health is derived from Central PMS projection synchronization targets; raw provider diagnostics are excluded."]),
            new ManagementDashboardOverviewSection(
                "vendor-projection-freshness",
                "Vendor projection freshness",
                availability,
                freshness,
                ProjectionSource,
                latest == default ? null : latest,
                [
                    Metric("active-projections", "Active projected sessions", projection.Targets.Sum(target => target.ActiveProjectionCount)),
                    Metric("stale-projection-targets", "Stale projection targets", staleTargets)
                ],
                warnings,
                ["Projected sessions are operational visibility only and are not parking, payment, fiscal, or exit truth."])
        ];
    }

    private static ManagementDashboardOverviewSection UnavailableSection(string id, string title) =>
        new(
            id,
            title,
            ManagementDashboardReportingValues.Unavailable,
            ManagementDashboardReportingValues.Unavailable,
            ProjectionSource,
            null,
            [],
            [ManagementDashboardReportingValues.ProjectionSourceUnavailable],
            ["The authoritative projection source could not be read; no zero or empty success value was substituted."]);

    private static ManagementDashboardMetric Metric(string id, string label, long value) =>
        new(id, label, value, "COUNT");

    private static string? NormalizeScopeType(string? scopeType)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
        {
            return null;
        }

        var normalized = scopeType.Trim().Replace('-', '_').ToUpperInvariant();
        return normalized is ManagementDashboardReportingValues.ScopeSite or ManagementDashboardReportingValues.ScopeSiteGroup
            ? normalized
            : null;
    }

    private static IReadOnlyList<ManagementDashboardCatalogEntry> BuildCatalog() =>
    [
        new(
            ManagementDashboardReportingValues.OperationalOverviewReportId,
            ManagementDashboardReportingValues.ContractVersion,
            "Operational overview",
            "Operations",
            "Scoped Site status, connector health, and vendor projection freshness.",
            [ManagementDashboardReportingValues.ScopeSite, ManagementDashboardReportingValues.ScopeSiteGroup],
            ManagementDashboardReportingValues.OverviewPermission,
            ManagementDashboardReportingValues.Partial,
            "CENTRAL_PMS_OPERATIONAL_READ_MODELS",
            "INTERNAL_OPERATIONAL_AGGREGATE",
            ["scopeType", "scopeReference"],
            "Each section supplies its own source timestamp and CURRENT, STALE, PARTIAL, UNAVAILABLE, or NOT_APPLICABLE classification.",
            ["FISCAL_SUMMARY_DEFERRED", "MANAGEMENT_ACTIVITY_DEFERRED"],
            ["Phase 1 exposes no exports, mutations, payment finality, fiscal authority, settlement closure, or exit authority."]),
        new(
            ManagementDashboardReportingValues.PaymentReconciliationReportId,
            ManagementPaymentReconciliationReportingValues.ContractVersion,
            "Payment and reconciliation summary",
            "Payments and reconciliation",
            "Canonical payment attempts, recorded confirmations, verified provider outcomes, and internally provable consistency conditions.",
            [ManagementDashboardReportingValues.ScopeSite, ManagementDashboardReportingValues.ScopeSiteGroup],
            ManagementPaymentReconciliationReportingValues.Permission,
            ManagementDashboardReportingValues.Partial,
            ManagementPaymentReconciliationReportingValues.SourceAuthority,
            "INTERNAL_FINANCIAL_AGGREGATE",
            ["scopeType", "scopeReference", "periodStart", "periodEnd"],
            "dataAsOf is the latest canonical payment record timestamp in the half-open requested period; it is not provider-live freshness.",
            ["SETTLEMENT_STATUS_UNAVAILABLE", "PROVIDER_PAYOUT_UNAVAILABLE", "FISCAL_REMITTANCE_UNAVAILABLE"],
            ["The report proves internal Central PMS consistency only and exposes no payment or reconciliation mutation authority."]),
        new(
            ManagementDashboardReportingValues.FiscalExceptionReportId,
            ManagementFiscalExceptionReportingValues.ContractVersion,
            "Fiscal exception summary",
            "Fiscal",
            "Authoritative Central PMS Sales Invoice issuance lifecycle and supported exception conditions.",
            [ManagementDashboardReportingValues.ScopeSite, ManagementDashboardReportingValues.ScopeSiteGroup],
            ManagementFiscalExceptionReportingValues.Permission,
            ManagementDashboardReportingValues.Partial,
            ManagementFiscalExceptionReportingValues.SourceAuthority,
            "INTERNAL_FISCAL_AGGREGATE",
            ["scopeType", "scopeReference", "periodStart", "periodEnd"],
            "dataAsOf is the latest persisted Central PMS fiscal issuance reference or linked payment-confirmation timestamp for the selected cohort; it is not live POS Server status.",
            ["PRINT_RESULT_UNAVAILABLE", "OVERDUE_DETECTION_UNAVAILABLE", "BIR_COMPLIANCE_CERTIFICATION_UNAVAILABLE"],
            ["The report does not synchronously query a Site POS Server and does not expose transaction-level fiscal document details."]),
        DeferredCatalogEntry(ManagementDashboardReportingValues.ManagementActivityReportId, "Management activity summary", "Audit")
    ];

    private static ManagementDashboardCatalogEntry DeferredCatalogEntry(string reportId, string title, string domain) =>
        new(
            reportId,
            ManagementDashboardReportingValues.ContractVersion,
            title,
            domain,
            "The capability is inventoried but is not exposed as an operational phase-1 report.",
            [ManagementDashboardReportingValues.ScopeSite, ManagementDashboardReportingValues.ScopeSiteGroup],
            ManagementDashboardReportingValues.CatalogPermission,
            ManagementDashboardReportingValues.Unavailable,
            DeferredSource,
            "NOT_EXPOSED",
            [],
            "No freshness claim is made until an authoritative read model is approved.",
            ["PHASE_1_SOURCE_UNAVAILABLE"],
            ["No report payload or placeholder result is available in phase 1."]);

    private static ManagementDashboardAuditRecord Audit(
        string eventType,
        string result,
        string reason,
        string reportId,
        ManagementDashboardActor actor,
        string? scopeType,
        Guid? scopeReference,
        string resultClassification,
        string sourceClassification,
        Guid correlationId,
        DateTimeOffset occurredAt) =>
        new(
            eventType,
            result,
            reason,
            reportId,
            actor.UserId,
            actor.HumanSessionId,
            scopeType,
            scopeReference,
            resultClassification,
            sourceClassification,
            correlationId,
            occurredAt);

    private static ManagementDashboardReportingResult<T> SourceUnavailable<T>(Guid correlationId) =>
        ManagementDashboardReportingResult<T>.Failed(
            ManagementDashboardReportingOutcome.SourceUnavailable,
            correlationId,
            ManagementDashboardReportingValues.SourceUnavailable,
            "A required Management Dashboard source is temporarily unavailable.",
            retryable: true);

    private static ManagementDashboardReportingResult<T> SessionInvalid<T>(Guid correlationId) =>
        ManagementDashboardReportingResult<T>.Failed(
            ManagementDashboardReportingOutcome.SessionInvalid,
            correlationId,
            ManagementDashboardReportingValues.SessionInvalid,
            "The Management Platform session is no longer valid.");
}
