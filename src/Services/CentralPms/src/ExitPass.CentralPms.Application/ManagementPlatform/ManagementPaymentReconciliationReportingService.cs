namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPaymentReconciliationReportingService : IManagementPaymentReconciliationReportingService
{
    private static readonly string[] AttemptStatuses =
        ["REQUESTED", "PENDING_PROVIDER", "PENDING_FINALIZATION", "CONFIRMED", "FAILED", "EXPIRED", "CANCELLED"];

    private static readonly string[] ConfirmationStatuses = ["RECORDED", "VOIDED"];

    private static readonly string[] ProviderOutcomeStatuses =
        ["CONFIRMED", "FAILED", "EXPIRED", "CANCELLED", "REJECTED", "UNKNOWN"];

    private readonly IManagementPaymentReconciliationReportingRepository _repository;
    private readonly ManagementDashboardReportingOptions _dashboardOptions;
    private readonly ManagementPaymentReconciliationReportingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ManagementPaymentReconciliationReportingService(
        IManagementPaymentReconciliationReportingRepository repository,
        ManagementDashboardReportingOptions dashboardOptions,
        ManagementPaymentReconciliationReportingOptions options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dashboardOptions = dashboardOptions;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>> GetSummaryAsync(
        ManagementDashboardActor actor,
        ManagementPaymentReconciliationQuery query,
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
            await AuditAsync(
                "MANAGEMENT_PAYMENT_RECONCILIATION_REPORT_DENIED",
                "DENIED",
                "FEATURE_DISABLED",
                actor,
                null,
                null,
                null,
                null,
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
                ManagementPaymentReconciliationOutcome.FeatureDisabled,
                query.CorrelationId,
                ManagementPaymentReconciliationReportingValues.FeatureDisabled,
                "Payment and reconciliation reporting is not enabled for this environment.");
        }

        var scopeType = NormalizeScopeType(query.ScopeType);
        if (scopeType is null)
        {
            await AuditInvalidAsync("INVALID_SCOPE_TYPE", actor, query, now, cancellationToken);
            return InvalidScope(
                query.CorrelationId,
                ManagementDashboardReportingValues.InvalidScopeType,
                "scopeType must be SITE or SITE_GROUP.");
        }

        if (query.ScopeReference is null || query.ScopeReference == Guid.Empty)
        {
            await AuditInvalidAsync("INVALID_SCOPE_REFERENCE", actor, query, now, cancellationToken);
            return InvalidScope(
                query.CorrelationId,
                ManagementDashboardReportingValues.InvalidScopeReference,
                "A non-empty scopeReference is required.");
        }

        if (!ManagementPaymentReconciliationPeriodParser.TryParseUtc(query.PeriodStart, out var periodStart))
        {
            await AuditInvalidAsync("INVALID_PERIOD_START", actor, query, now, cancellationToken);
            return InvalidPeriod(
                query.CorrelationId,
                ManagementPaymentReconciliationReportingValues.InvalidPeriodStart,
                "periodStart must be an explicit UTC timestamp.");
        }

        if (!ManagementPaymentReconciliationPeriodParser.TryParseUtc(query.PeriodEnd, out var periodEnd))
        {
            await AuditInvalidAsync("INVALID_PERIOD_END", actor, query, now, cancellationToken);
            return InvalidPeriod(
                query.CorrelationId,
                ManagementPaymentReconciliationReportingValues.InvalidPeriodEnd,
                "periodEnd must be an explicit UTC timestamp.");
        }

        if (periodStart >= periodEnd)
        {
            await AuditInvalidAsync("INVALID_PERIOD_RANGE", actor, query, now, cancellationToken, periodStart, periodEnd);
            return InvalidPeriod(
                query.CorrelationId,
                ManagementPaymentReconciliationReportingValues.InvalidPeriodRange,
                "periodStart must be earlier than periodEnd.");
        }

        if (periodEnd - periodStart > TimeSpan.FromDays(ManagementPaymentReconciliationReportingValues.MaximumPeriodDays))
        {
            await AuditInvalidAsync("PERIOD_TOO_LONG", actor, query, now, cancellationToken, periodStart, periodEnd);
            return InvalidPeriod(
                query.CorrelationId,
                ManagementPaymentReconciliationReportingValues.PeriodTooLong,
                "The reporting period cannot exceed 31 days.");
        }

        var scopeResult = await _repository.ResolveScopeAsync(
            actor,
            scopeType,
            query.ScopeReference.Value,
            cancellationToken);
        if (scopeResult.Status == ManagementDashboardScopeReadStatus.SourceUnavailable)
        {
            await AuditAsync(
                "MANAGEMENT_PAYMENT_RECONCILIATION_SOURCE_FAILURE",
                "FAILED",
                "SCOPE_QUERY_FAILED",
                actor,
                scopeType,
                query.ScopeReference,
                periodStart,
                periodEnd,
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        if (scopeResult.Status != ManagementDashboardScopeReadStatus.Resolved || scopeResult.Scope is null)
        {
            await AuditAsync(
                "MANAGEMENT_PAYMENT_RECONCILIATION_REPORT_DENIED",
                "DENIED",
                "SCOPE_NOT_FOUND_OR_DENIED",
                actor,
                scopeType,
                query.ScopeReference,
                periodStart,
                periodEnd,
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
                ManagementPaymentReconciliationOutcome.ScopeNotFoundOrDenied,
                query.CorrelationId,
                ManagementDashboardReportingValues.ScopeNotFoundOrDenied,
                "The requested reporting scope was not found or is not available to this session.");
        }

        var source = await _repository.ReadSummaryAsync(scopeResult.Scope, periodStart, periodEnd, cancellationToken);
        if (source.Status != ManagementPaymentReconciliationReadStatus.Resolved || source.Snapshot is null)
        {
            await AuditAsync(
                "MANAGEMENT_PAYMENT_RECONCILIATION_SOURCE_FAILURE",
                "FAILED",
                "PAYMENT_QUERY_FAILED",
                actor,
                scopeType,
                query.ScopeReference,
                periodStart,
                periodEnd,
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        if (!UsesSupportedCurrency(source.Snapshot))
        {
            await AuditAsync(
                "MANAGEMENT_PAYMENT_RECONCILIATION_SOURCE_FAILURE",
                "FAILED",
                "UNSUPPORTED_CURRENCY_SOURCE_DATA",
                actor,
                scopeType,
                query.ScopeReference,
                periodStart,
                periodEnd,
                ManagementDashboardReportingValues.Unavailable,
                query.CorrelationId,
                now,
                cancellationToken);
            return SourceUnavailable(query.CorrelationId);
        }

        var report = BuildReport(scopeType, query.ScopeReference.Value, scopeResult.Scope, periodStart, periodEnd, now, query.CorrelationId, source.Snapshot);
        await AuditAsync(
            "MANAGEMENT_PAYMENT_RECONCILIATION_REPORT_READ",
            "SUCCESS",
            "REPORT_RETURNED",
            actor,
            scopeType,
            query.ScopeReference,
            periodStart,
            periodEnd,
            report.Availability,
            query.CorrelationId,
            now,
            cancellationToken);
        return ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Success(report, query.CorrelationId);
    }

    private static bool UsesSupportedCurrency(ManagementPaymentReconciliationSourceSnapshot source) =>
        source.Attempts.All(HasSupportedCurrency) &&
        source.Confirmations.All(HasSupportedCurrency) &&
        source.ProviderOutcomes.All(HasSupportedCurrency) &&
        source.ReconciliationConditions.All(condition =>
            condition.CurrencyCode is null || HasSupportedCurrency(condition.CurrencyCode));

    private static bool HasSupportedCurrency(ManagementPaymentAggregateRecord record) =>
        HasSupportedCurrency(record.CurrencyCode);

    private static bool HasSupportedCurrency(string currencyCode) =>
        string.Equals(
            currencyCode.Trim(),
            ManagementPaymentReconciliationReportingValues.SupportedCurrency,
            StringComparison.OrdinalIgnoreCase);

    private static ManagementPaymentReconciliationReport BuildReport(
        string scopeType,
        Guid scopeReference,
        ManagementDashboardScopeSnapshot scope,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset generatedAt,
        Guid correlationId,
        ManagementPaymentReconciliationSourceSnapshot source)
    {
        var hasActivity = source.Attempts.Count > 0 || source.Confirmations.Count > 0 || source.ProviderOutcomes.Count > 0;
        var warnings = hasActivity ? Array.Empty<string>() : [ManagementPaymentReconciliationReportingValues.NoActivity];
        var limitations = new[]
        {
            "This report proves internal Central PMS consistency only.",
            "Provider settlement, payout, bank deposit, cash custody, fees, refunds, chargebacks, disputes, and fiscal remittance are unavailable.",
            "Payment channel and provider dimensions are limited to canonical payment-rail metadata."
        };

        var requestedScope = new ManagementDashboardScope(scopeType, scopeReference, scope.DisplayName);
        var effectiveScope = new ManagementDashboardScope(scope.ScopeType, scope.ScopeReference, scope.DisplayName);
        return new ManagementPaymentReconciliationReport(
            ManagementPaymentReconciliationReportingValues.ContractVersion,
            ManagementPaymentReconciliationReportingValues.ReportId,
            requestedScope,
            effectiveScope,
            periodStart,
            periodEnd,
            generatedAt,
            source.DataAsOf,
            hasActivity ? ManagementDashboardReportingValues.Partial : ManagementDashboardReportingValues.Available,
            source.DataAsOf is null ? ManagementDashboardReportingValues.NotApplicable : ManagementDashboardReportingValues.Current,
            correlationId,
            BuildCurrencySummaries(source),
            BuildStatusSummaries(source.Attempts, NormalizeAttemptStatus),
            BuildStatusSummaries(source.Confirmations, NormalizeConfirmationStatus),
            BuildCanonicalStatusSummaries(source),
            BuildChannelSummaries(source),
            BuildProviderSummaries(source),
            BuildReconciliationSummaries(source.ReconciliationConditions),
            warnings,
            limitations,
            ManagementPaymentReconciliationReportingValues.SourceAuthority);
    }

    private static IReadOnlyList<ManagementPaymentCurrencySummary> BuildCurrencySummaries(
        ManagementPaymentReconciliationSourceSnapshot source) =>
        source.Attempts.Select(row => row.CurrencyCode)
            .Concat(source.Confirmations.Where(IsRecordedConfirmation).Select(row => row.CurrencyCode))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(currency => new ManagementPaymentCurrencySummary(
                currency,
                source.Attempts.Where(row => row.CurrencyCode == currency).Sum(row => row.Count),
                source.Attempts.Where(row => row.CurrencyCode == currency).Sum(row => row.Amount),
                source.Confirmations.Where(row => row.CurrencyCode == currency && IsRecordedConfirmation(row)).Sum(row => row.Count),
                source.Confirmations.Where(row => row.CurrencyCode == currency && IsRecordedConfirmation(row)).Sum(row => row.Amount)))
            .ToArray();

    private static IReadOnlyList<ManagementPaymentStatusSummary> BuildStatusSummaries(
        IReadOnlyList<ManagementPaymentAggregateRecord> rows,
        Func<string, string> normalize) =>
        rows.GroupBy(row => new { Status = normalize(row.Status), row.CurrencyCode })
            .OrderBy(group => group.Key.Status, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(group => new ManagementPaymentStatusSummary(
                group.Key.Status,
                group.Key.CurrencyCode,
                group.Sum(row => row.Count),
                group.Sum(row => row.Amount)))
            .ToArray();

    private static IReadOnlyList<ManagementPaymentCanonicalStatusSummary> BuildCanonicalStatusSummaries(
        ManagementPaymentReconciliationSourceSnapshot source) =>
        source.Attempts.Select(row => (Type: "PAYMENT_ATTEMPT", Row: row, Status: NormalizeAttemptStatus(row.Status)))
            .Concat(source.Confirmations.Select(row => (Type: "PAYMENT_CONFIRMATION", Row: row, Status: NormalizeConfirmationStatus(row.Status))))
            .Concat(source.ProviderOutcomes.Select(row => (Type: "PROVIDER_OUTCOME", Row: row, Status: NormalizeProviderOutcomeStatus(row.Status))))
            .GroupBy(item => new { item.Type, item.Status, item.Row.CurrencyCode })
            .OrderBy(group => group.Key.Type, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Status, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(group => new ManagementPaymentCanonicalStatusSummary(
                group.Key.Type,
                group.Key.Status,
                group.Key.CurrencyCode,
                group.Sum(item => item.Row.Count),
                group.Sum(item => item.Row.Amount)))
            .ToArray();

    private static IReadOnlyList<ManagementPaymentChannelSummary> BuildChannelSummaries(
        ManagementPaymentReconciliationSourceSnapshot source) =>
        source.Attempts.Select(row => (Kind: "ATTEMPT", Row: row))
            .Concat(source.Confirmations.Where(IsRecordedConfirmation).Select(row => (Kind: "CONFIRMATION", Row: row)))
            .GroupBy(item => new { item.Row.ChannelCode, item.Row.ChannelType, item.Row.CurrencyCode })
            .OrderBy(group => group.Key.ChannelCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(group => new ManagementPaymentChannelSummary(
                group.Key.ChannelCode,
                group.Key.ChannelType,
                group.Key.CurrencyCode,
                group.Where(item => item.Kind == "ATTEMPT").Sum(item => item.Row.Count),
                group.Where(item => item.Kind == "ATTEMPT").Sum(item => item.Row.Amount),
                group.Where(item => item.Kind == "CONFIRMATION").Sum(item => item.Row.Count),
                group.Where(item => item.Kind == "CONFIRMATION").Sum(item => item.Row.Amount)))
            .ToArray();

    private static IReadOnlyList<ManagementPaymentProviderSummary> BuildProviderSummaries(
        ManagementPaymentReconciliationSourceSnapshot source) =>
        source.Attempts.Select(row => (Kind: "ATTEMPT", Row: row))
            .Concat(source.Confirmations.Where(IsRecordedConfirmation).Select(row => (Kind: "CONFIRMATION", Row: row)))
            .Concat(source.ProviderOutcomes.Select(row => (Kind: "OUTCOME", Row: row)))
            .Where(item => item.Row.ProviderCode != "UNAVAILABLE")
            .GroupBy(item => new { item.Row.ProviderCode, item.Row.CurrencyCode })
            .OrderBy(group => group.Key.ProviderCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(group => new ManagementPaymentProviderSummary(
                group.Key.ProviderCode,
                group.Key.CurrencyCode,
                group.Where(item => item.Kind == "ATTEMPT").Sum(item => item.Row.Count),
                group.Where(item => item.Kind == "ATTEMPT").Sum(item => item.Row.Amount),
                group.Where(item => item.Kind == "CONFIRMATION").Sum(item => item.Row.Count),
                group.Where(item => item.Kind == "CONFIRMATION").Sum(item => item.Row.Amount),
                group.Where(item => item.Kind == "OUTCOME").Sum(item => item.Row.Count),
                group.Where(item => item.Kind == "OUTCOME").Sum(item => item.Row.Amount)))
            .ToArray();

    private static IReadOnlyList<ManagementInternalReconciliationSummary> BuildReconciliationSummaries(
        IReadOnlyList<ManagementPaymentReconciliationConditionRecord> rows)
    {
        return ReconciliationDefinitions().Select(definition =>
        {
            var matches = rows.Where(row => row.CategoryId == definition.CategoryId).ToArray();
            var amounts = matches.Where(row => row.CurrencyCode is not null && row.Amount is not null)
                .GroupBy(row => row.CurrencyCode!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ManagementReconciliationAmountSummary(group.Key, group.Sum(row => row.Amount!.Value)))
                .ToArray();
            return new ManagementInternalReconciliationSummary(
                definition.CategoryId,
                ManagementDashboardReportingValues.Available,
                matches.Sum(row => row.Count),
                amounts,
                definition.Definition,
                definition.MonetaryTreatment,
                definition.Limitations);
        }).ToArray();
    }

    private static IReadOnlyList<ReconciliationDefinition> ReconciliationDefinitions() =>
    [
        new(
            ManagementPaymentReconciliationReportingValues.AmountMismatch,
            "A recorded confirmation in the period has the same currency as its canonical attempt but a different amount.",
            "Absolute amount variance is summed separately by currency.",
            []),
        new(
            ManagementPaymentReconciliationReportingValues.CurrencyMismatch,
            "A recorded confirmation in the period has a currency different from its canonical attempt.",
            "No cross-currency amount is calculated.",
            ["Amounts cannot be combined across currencies."]),
        new(
            ManagementPaymentReconciliationReportingValues.DuplicateProviderReference,
            "More than one recorded confirmation in the scope and period carries the same non-empty transaction reference for the same canonical provider.",
            "No amount is reported because duplicate records must not be treated as distinct value.",
            ["The reference itself is never returned."]),
        new(
            ManagementPaymentReconciliationReportingValues.ConfirmedOutcomeWithoutConfirmation,
            "A provider outcome verified as CONFIRMED in the period has no canonical confirmation linked by provider outcome id.",
            "Verified outcome amount is summed separately by currency; it is not treated as confirmed revenue.",
            []),
        new(
            ManagementPaymentReconciliationReportingValues.ConfirmationAttemptStatusInconsistent,
            "A recorded confirmation in the period is linked to an attempt whose current status is not CONFIRMED.",
            "Recorded confirmation amount is summed separately by currency and is not promoted to payment finality.",
            ["The report does not repair or finalize the attempt."])
    ];

    private static bool IsRecordedConfirmation(ManagementPaymentAggregateRecord row) =>
        string.Equals(row.Status, "RECORDED", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAttemptStatus(string status) =>
        AttemptStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) ? status.ToUpperInvariant() : "OTHER";

    private static string NormalizeConfirmationStatus(string status) =>
        ConfirmationStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) ? status.ToUpperInvariant() : "OTHER";

    private static string NormalizeProviderOutcomeStatus(string status) =>
        ProviderOutcomeStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) ? status.ToUpperInvariant() : "OTHER";

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
        ManagementPaymentReconciliationQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? periodStart = null,
        DateTimeOffset? periodEnd = null) =>
        AuditAsync(
            "MANAGEMENT_PAYMENT_RECONCILIATION_REPORT_DENIED",
            "DENIED",
            reason,
            actor,
            query.ScopeType,
            query.ScopeReference,
            periodStart,
            periodEnd,
            ManagementDashboardReportingValues.Unavailable,
            query.CorrelationId,
            now,
            cancellationToken);

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
        Guid correlationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        _repository.RecordAuditAsync(
            new ManagementPaymentReconciliationAuditRecord(
                eventType,
                result,
                reason,
                actor.UserId,
                actor.HumanSessionId,
                scopeType,
                scopeReference,
                periodStart,
                periodEnd,
                resultClassification,
                correlationId,
                occurredAt),
            cancellationToken);

    private static ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport> InvalidScope(
        Guid correlationId,
        string errorCode,
        string message) =>
        ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
            ManagementPaymentReconciliationOutcome.InvalidScope,
            correlationId,
            errorCode,
            message);

    private static ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport> InvalidPeriod(
        Guid correlationId,
        string errorCode,
        string message) =>
        ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
            ManagementPaymentReconciliationOutcome.InvalidPeriod,
            correlationId,
            errorCode,
            message);

    private static ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport> SourceUnavailable(Guid correlationId) =>
        ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
            ManagementPaymentReconciliationOutcome.SourceUnavailable,
            correlationId,
            ManagementPaymentReconciliationReportingValues.SourceUnavailable,
            "The canonical payment reporting source is temporarily unavailable.",
            retryable: true);

    private static ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport> SessionInvalid(Guid correlationId) =>
        ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
            ManagementPaymentReconciliationOutcome.SessionInvalid,
            correlationId,
            ManagementDashboardReportingValues.SessionInvalid,
            "The Management Platform session is no longer valid.");

    private sealed record ReconciliationDefinition(
        string CategoryId,
        string Definition,
        string MonetaryTreatment,
        IReadOnlyList<string> Limitations);
}
