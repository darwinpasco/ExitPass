namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementFiscalExceptionReportingService : IManagementFiscalExceptionReportingService
{
    private static readonly string[] IssuedStates =
        ["FISCAL_ISSUANCE_RECORDED", "FISCAL_ISSUANCE_REPLAYED", "FISCAL_ISSUANCE_RECONCILED"];

    private static readonly string[] FailedStates =
        ["FISCAL_ISSUANCE_FAILED_REQUEST", "FISCAL_ISSUANCE_FAILED_CONFIGURATION", "FISCAL_ISSUANCE_FAILED_SERVICE"];

    private static readonly string[] KnownStates =
    [
        "NOT_REQUIRED", "PENDING_FISCAL_ISSUANCE", "FISCAL_ISSUANCE_REQUESTED",
        "FISCAL_ISSUANCE_RECORDED", "FISCAL_ISSUANCE_REPLAYED", "FISCAL_ISSUANCE_CONFLICT",
        "FISCAL_ISSUANCE_FAILED_REQUEST", "FISCAL_ISSUANCE_FAILED_CONFIGURATION",
        "FISCAL_ISSUANCE_FAILED_SERVICE", "FISCAL_ISSUANCE_UNKNOWN",
        "FISCAL_ISSUANCE_MANUAL_REVIEW", "FISCAL_ISSUANCE_EXCEPTION_RELEASED",
        "FISCAL_ISSUANCE_RECONCILED"
    ];

    private readonly IManagementFiscalExceptionReportingRepository _repository;
    private readonly ManagementDashboardReportingOptions _dashboardOptions;
    private readonly ManagementFiscalExceptionReportingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ManagementFiscalExceptionReportingService(
        IManagementFiscalExceptionReportingRepository repository,
        ManagementDashboardReportingOptions dashboardOptions,
        ManagementFiscalExceptionReportingOptions options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dashboardOptions = dashboardOptions;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>> GetSummaryAsync(
        ManagementDashboardActor actor,
        ManagementFiscalExceptionQuery query,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var actorValidation = await _repository.ValidateActorAsync(actor, cancellationToken);
        if (actorValidation == ManagementDashboardActorValidationStatus.SourceUnavailable)
        {
            return SourceUnavailable(query.CorrelationId);
        }

        if (actorValidation != ManagementDashboardActorValidationStatus.Valid)
        {
            return SessionInvalid(query.CorrelationId);
        }

        if (!_dashboardOptions.Enabled || !_options.Enabled)
        {
            await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_REPORT_DENIED", "DENIED", "FEATURE_DISABLED",
                actor, query.ScopeType, query.ScopeReference, null, null,
                ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);
            return ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
                ManagementFiscalExceptionOutcome.FeatureDisabled,
                query.CorrelationId,
                ManagementFiscalExceptionReportingValues.FeatureDisabled,
                "Fiscal exception reporting is not enabled for this environment.");
        }

        var scopeType = NormalizeScopeType(query.ScopeType);
        if (scopeType is null)
        {
            await AuditInvalidAsync("INVALID_SCOPE_TYPE", actor, query, now, cancellationToken);
            return InvalidScope(query.CorrelationId, ManagementDashboardReportingValues.InvalidScopeType,
                "scopeType must be SITE or SITE_GROUP.");
        }

        if (query.ScopeReference is null || query.ScopeReference == Guid.Empty)
        {
            await AuditInvalidAsync("INVALID_SCOPE_REFERENCE", actor, query, now, cancellationToken);
            return InvalidScope(query.CorrelationId, ManagementDashboardReportingValues.InvalidScopeReference,
                "A non-empty scopeReference is required.");
        }

        if (!ManagementFiscalExceptionPeriodParser.TryParseUtc(query.PeriodStart, out var periodStart))
        {
            await AuditInvalidAsync("INVALID_PERIOD_START", actor, query, now, cancellationToken);
            return InvalidPeriod(query.CorrelationId, ManagementFiscalExceptionReportingValues.InvalidPeriodStart,
                "periodStart must be an explicit UTC timestamp.");
        }

        if (!ManagementFiscalExceptionPeriodParser.TryParseUtc(query.PeriodEnd, out var periodEnd))
        {
            await AuditInvalidAsync("INVALID_PERIOD_END", actor, query, now, cancellationToken);
            return InvalidPeriod(query.CorrelationId, ManagementFiscalExceptionReportingValues.InvalidPeriodEnd,
                "periodEnd must be an explicit UTC timestamp.");
        }

        if (periodStart >= periodEnd)
        {
            await AuditInvalidAsync("INVALID_PERIOD_RANGE", actor, query, now, cancellationToken, periodStart, periodEnd);
            return InvalidPeriod(query.CorrelationId, ManagementFiscalExceptionReportingValues.InvalidPeriodRange,
                "periodStart must be earlier than periodEnd.");
        }

        if (periodEnd - periodStart > TimeSpan.FromDays(ManagementFiscalExceptionReportingValues.MaximumPeriodDays))
        {
            await AuditInvalidAsync("PERIOD_TOO_LONG", actor, query, now, cancellationToken, periodStart, periodEnd);
            return InvalidPeriod(query.CorrelationId, ManagementFiscalExceptionReportingValues.PeriodTooLong,
                "The reporting period cannot exceed 31 days.");
        }

        var scopeResult = await _repository.ResolveScopeAsync(actor, scopeType, query.ScopeReference.Value, cancellationToken);
        if (scopeResult.Status == ManagementDashboardScopeReadStatus.SourceUnavailable)
        {
            await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_SOURCE_FAILURE", "FAILED", "SCOPE_QUERY_FAILED",
                actor, scopeType, query.ScopeReference, periodStart, periodEnd,
                ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        if (scopeResult.Status != ManagementDashboardScopeReadStatus.Resolved || scopeResult.Scope is null)
        {
            await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_REPORT_DENIED", "DENIED", "SCOPE_NOT_FOUND_OR_DENIED",
                actor, scopeType, query.ScopeReference, periodStart, periodEnd,
                ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);
            return ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
                ManagementFiscalExceptionOutcome.ScopeNotFoundOrDenied,
                query.CorrelationId,
                ManagementDashboardReportingValues.ScopeNotFoundOrDenied,
                "The requested reporting scope was not found or is not available to this session.");
        }

        var source = await _repository.ReadSummaryAsync(scopeResult.Scope, periodStart, periodEnd, cancellationToken);
        if (source.Status != ManagementFiscalExceptionReadStatus.Resolved || source.Snapshot is null)
        {
            await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_SOURCE_FAILURE", "FAILED", "FISCAL_REFERENCE_QUERY_FAILED",
                actor, scopeType, query.ScopeReference, periodStart, periodEnd,
                ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        if (source.Snapshot.Records.Any(record => !string.Equals(
                record.CurrencyCode.Trim(),
                ManagementFiscalExceptionReportingValues.SupportedCurrency,
                StringComparison.OrdinalIgnoreCase)))
        {
            await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_SOURCE_FAILURE", "FAILED", "UNSUPPORTED_CURRENCY_SOURCE_DATA",
                actor, scopeType, query.ScopeReference, periodStart, periodEnd,
                ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        var report = BuildReport(scopeType, query.ScopeReference.Value, scopeResult.Scope, periodStart, periodEnd,
            now, query.CorrelationId, source.Snapshot);
        await AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_REPORT_READ", "SUCCESS", "REPORT_RETURNED",
            actor, scopeType, query.ScopeReference, periodStart, periodEnd,
            report.Availability, source.Snapshot.Records.Sum(row => row.Count), query.CorrelationId, now, cancellationToken);
        return ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Success(report, query.CorrelationId);
    }

    private static ManagementFiscalExceptionReport BuildReport(
        string scopeType,
        Guid scopeReference,
        ManagementDashboardScopeSnapshot scope,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset generatedAt,
        Guid correlationId,
        ManagementFiscalExceptionSourceSnapshot source)
    {
        var hasActivity = source.Records.Count > 0;
        var requestedScope = new ManagementDashboardScope(scopeType, scopeReference, scope.DisplayName);
        var effectiveScope = new ManagementDashboardScope(scope.ScopeType, scope.ScopeReference, scope.DisplayName);
        var warnings = hasActivity ? Array.Empty<string>() : [ManagementFiscalExceptionReportingValues.NoActivity];
        var unavailableFacts = new[]
        {
            "SALES_INVOICE_PRINT_RESULT_UNAVAILABLE",
            "DIGITAL_COPY_AVAILABILITY_UNAVAILABLE",
            "REPRINT_ADJUSTMENT_VOID_DELIVERY_UNAVAILABLE",
            "OVERDUE_DETECTION_UNAVAILABLE",
            "BIR_COMPLIANCE_CERTIFICATION_UNAVAILABLE"
        };

        return new ManagementFiscalExceptionReport(
            ManagementFiscalExceptionReportingValues.ContractVersion,
            ManagementFiscalExceptionReportingValues.ReportId,
            requestedScope,
            effectiveScope,
            periodStart,
            periodEnd,
            ManagementFiscalExceptionReportingValues.TimeBasis,
            generatedAt,
            source.DataAsOf,
            hasActivity ? ManagementDashboardReportingValues.Partial : ManagementFiscalExceptionReportingValues.NoActivityAvailability,
            source.DataAsOf is null ? ManagementDashboardReportingValues.NotApplicable : ManagementDashboardReportingValues.Current,
            correlationId,
            BuildSourceCoverage(source),
            BuildLifecycleSummaries(source.Records),
            BuildExceptionSummaries(source.Records),
            BuildCurrencySummaries(source.Records),
            warnings,
            [
                "The cohort contains Central PMS fiscal issuance references first recorded in the requested half-open UTC period.",
                "Current lifecycle state is evaluated when the report is generated and can change after this response.",
                "Payment confirmation amounts describe expected issuance value; no issued-document amount is available for comparison.",
                "The report does not query a Site POS Server and does not certify BIR compliance."
            ],
            unavailableFacts,
            ManagementFiscalExceptionReportingValues.SourceAuthority);
    }

    private static IReadOnlyList<ManagementFiscalSourceCoverage> BuildSourceCoverage(
        ManagementFiscalExceptionSourceSnapshot source) =>
    [
        new(
            "central-pms-fiscal-issuance-references",
            ManagementDashboardReportingValues.Available,
            source.DataAsOf,
            "Central PMS coordination references and latest persisted Sales Invoice issuance state.",
            ["The reference proves coordination state only; POS Server remains the issuance authority."]),
        new(
            "central-pms-payment-confirmations",
            ManagementDashboardReportingValues.Available,
            source.DataAsOf,
            "Canonical payment confirmation currency and expected issuance amount linked to each reference.",
            ["Payment confirmation alone does not prove Sales Invoice issuance."]),
        new(
            "pos-server-fiscal-records",
            ManagementDashboardReportingValues.Partial,
            source.DataAsOf,
            "Only authoritative POS Server issuance outcomes persisted by Central PMS are represented.",
            ["No synchronous POS Server read occurs during report generation."])
    ];

    private static IReadOnlyList<ManagementFiscalLifecycleSummary> BuildLifecycleSummaries(
        IReadOnlyList<ManagementFiscalAggregateRecord> records) =>
        records.GroupBy(row => NormalizeLifecycle(row.FiscalIssuanceState), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ManagementFiscalLifecycleSummary(group.Key, group.Sum(row => row.Count)))
            .ToArray();

    private static IReadOnlyList<ManagementFiscalCurrencySummary> BuildCurrencySummaries(
        IReadOnlyList<ManagementFiscalAggregateRecord> records) =>
        records.GroupBy(row => row.CurrencyCode, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ManagementFiscalCurrencySummary(
                group.Key,
                group.Sum(row => row.Count),
                group.Sum(row => row.ExpectedIssuanceAmount),
                group.Where(row => IssuedStates.Contains(row.FiscalIssuanceState, StringComparer.OrdinalIgnoreCase)).Sum(row => row.Count),
                group.Where(row => FailedStates.Contains(row.FiscalIssuanceState, StringComparer.OrdinalIgnoreCase)).Sum(row => row.Count)))
            .ToArray();

    private static IReadOnlyList<ManagementFiscalExceptionSummary> BuildExceptionSummaries(
        IReadOnlyList<ManagementFiscalAggregateRecord> records) =>
        ExceptionDefinitions().Select(definition =>
        {
            var matches = records.Where(row => definition.States.Contains(row.FiscalIssuanceState, StringComparer.OrdinalIgnoreCase)).ToArray();
            var amounts = matches.GroupBy(row => row.CurrencyCode, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ManagementFiscalAmountSummary(group.Key, group.Sum(row => row.ExpectedIssuanceAmount)))
                .ToArray();
            return new ManagementFiscalExceptionSummary(
                definition.CategoryId,
                ManagementDashboardReportingValues.Available,
                matches.Sum(row => row.Count),
                amounts,
                definition.Definition,
                definition.Terminal,
                definition.CanResolveLater,
                definition.Limitations);
        }).ToArray();

    private static IReadOnlyList<ExceptionDefinition> ExceptionDefinitions() =>
    [
        new(
            ManagementFiscalExceptionReportingValues.IssuanceFailed,
            FailedStates,
            "The latest Central PMS state records a request, configuration, or service failure for the issuance reference.",
            false,
            true,
            ["Failure does not prove that no document exists; recovery or readback can change the state."]),
        new(
            ManagementFiscalExceptionReportingValues.ReferenceConflict,
            ["FISCAL_ISSUANCE_CONFLICT"],
            "The latest Central PMS state records an idempotency, replay, or reference conflict requiring controlled review.",
            false,
            true,
            ["The conflicting identifiers are not returned by this aggregate report."]),
        new(
            ManagementFiscalExceptionReportingValues.OutcomeUnavailable,
            ["FISCAL_ISSUANCE_UNKNOWN", "FISCAL_ISSUANCE_MANUAL_REVIEW"],
            "Central PMS does not hold a conclusive latest issuance outcome for the reference.",
            false,
            true,
            ["No missing or failed document assertion is made while the outcome is inconclusive."])
    ];

    private static string NormalizeLifecycle(string state)
    {
        var normalized = state.Trim().ToUpperInvariant();
        if (!KnownStates.Contains(normalized, StringComparer.Ordinal))
        {
            return "OTHER";
        }

        return normalized switch
        {
            "PENDING_FISCAL_ISSUANCE" => "PENDING",
            "FISCAL_ISSUANCE_REQUESTED" => "REQUESTED",
            "FISCAL_ISSUANCE_RECORDED" or "FISCAL_ISSUANCE_REPLAYED" or "FISCAL_ISSUANCE_RECONCILED" => "ISSUED",
            "FISCAL_ISSUANCE_FAILED_REQUEST" or "FISCAL_ISSUANCE_FAILED_CONFIGURATION" or "FISCAL_ISSUANCE_FAILED_SERVICE" => "FAILED",
            "FISCAL_ISSUANCE_CONFLICT" => "CONFLICT",
            "FISCAL_ISSUANCE_UNKNOWN" => "OUTCOME_UNAVAILABLE",
            "FISCAL_ISSUANCE_MANUAL_REVIEW" => "MANUAL_REVIEW",
            "FISCAL_ISSUANCE_EXCEPTION_RELEASED" => "EXCEPTION_RELEASED",
            "NOT_REQUIRED" => "NOT_REQUIRED",
            _ => "OTHER"
        };
    }

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

    private Task AuditInvalidAsync(
        string reason,
        ManagementDashboardActor actor,
        ManagementFiscalExceptionQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? periodStart = null,
        DateTimeOffset? periodEnd = null) =>
        AuditAsync("MANAGEMENT_FISCAL_EXCEPTION_REPORT_DENIED", "DENIED", reason, actor,
            query.ScopeType, query.ScopeReference, periodStart, periodEnd,
            ManagementDashboardReportingValues.Unavailable, 0, query.CorrelationId, now, cancellationToken);

    private Task AuditAsync(
        string eventType,
        string result,
        string reason,
        ManagementDashboardActor actor,
        string? scopeType,
        Guid? scopeReference,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        string resultClassification,
        long aggregateCount,
        Guid correlationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        _repository.RecordAuditAsync(
            new ManagementFiscalExceptionAuditRecord(
                eventType, result, reason, actor.UserId, actor.HumanSessionId, scopeType, scopeReference,
                periodStart, periodEnd, ManagementFiscalExceptionReportingValues.TimeBasis,
                resultClassification, aggregateCount, correlationId, occurredAt),
            cancellationToken);

    private static ManagementFiscalExceptionResult<ManagementFiscalExceptionReport> InvalidScope(
        Guid correlationId, string errorCode, string message) =>
        ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
            ManagementFiscalExceptionOutcome.InvalidScope, correlationId, errorCode, message);

    private static ManagementFiscalExceptionResult<ManagementFiscalExceptionReport> InvalidPeriod(
        Guid correlationId, string errorCode, string message) =>
        ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
            ManagementFiscalExceptionOutcome.InvalidPeriod, correlationId, errorCode, message);

    private static ManagementFiscalExceptionResult<ManagementFiscalExceptionReport> SourceUnavailable(Guid correlationId) =>
        ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
            ManagementFiscalExceptionOutcome.SourceUnavailable,
            correlationId,
            ManagementFiscalExceptionReportingValues.SourceUnavailable,
            "The authoritative fiscal exception reporting source is temporarily unavailable.",
            true);

    private static ManagementFiscalExceptionResult<ManagementFiscalExceptionReport> SessionInvalid(Guid correlationId) =>
        ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
            ManagementFiscalExceptionOutcome.SessionInvalid,
            correlationId,
            ManagementDashboardReportingValues.SessionInvalid,
            "The Management Platform session is no longer valid.");

    private sealed record ExceptionDefinition(
        string CategoryId,
        IReadOnlyList<string> States,
        string Definition,
        bool Terminal,
        bool CanResolveLater,
        IReadOnlyList<string> Limitations);
}
