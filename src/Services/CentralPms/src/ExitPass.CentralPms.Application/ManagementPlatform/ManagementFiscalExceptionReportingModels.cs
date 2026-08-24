using System.Globalization;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementFiscalExceptionReportingValues
{
    public const string ContractVersion = "management-platform-fiscal-exception-reporting:v1";
    public const string ReportId = "fiscal-exception-summary";
    public const string Policy = "ManagementPlatformFiscalExceptionSummaryRead";
    public const string Permission = "sales-invoice-report.view";
    public const string FeatureDisabled = "MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED";
    public const string InvalidPeriodStart = "INVALID_FISCAL_EXCEPTION_PERIOD_START";
    public const string InvalidPeriodEnd = "INVALID_FISCAL_EXCEPTION_PERIOD_END";
    public const string InvalidPeriodRange = "INVALID_FISCAL_EXCEPTION_PERIOD_RANGE";
    public const string PeriodTooLong = "FISCAL_EXCEPTION_PERIOD_TOO_LONG";
    public const string SourceUnavailable = "FISCAL_EXCEPTION_SOURCE_UNAVAILABLE";
    public const string UnexpectedFailure = "MANAGEMENT_FISCAL_EXCEPTION_UNEXPECTED_FAILURE";
    public const string NoActivity = "NO_SALES_INVOICE_ISSUANCE_ACTIVITY_IN_PERIOD";
    public const string SourceAuthority = "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES";
    public const string TimeBasis = "FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT";
    public const string SupportedCurrency = "PHP";
    public const string NoActivityAvailability = "NO_ACTIVITY";
    public const int MaximumPeriodDays = 31;

    public const string IssuanceFailed = "SALES_INVOICE_ISSUANCE_FAILED";
    public const string ReferenceConflict = "SALES_INVOICE_REFERENCE_CONFLICT";
    public const string OutcomeUnavailable = "SALES_INVOICE_OUTCOME_UNAVAILABLE";
}

public sealed class ManagementFiscalExceptionReportingOptions
{
    public const string SectionName = "ManagementPlatform:DashboardReporting:FiscalExceptions";

    public bool Enabled { get; set; }
}

public sealed record ManagementFiscalExceptionQuery(
    string? ScopeType,
    Guid? ScopeReference,
    string? PeriodStart,
    string? PeriodEnd,
    Guid CorrelationId);

public enum ManagementFiscalExceptionOutcome
{
    Success,
    FeatureDisabled,
    InvalidScope,
    InvalidPeriod,
    ScopeNotFoundOrDenied,
    SessionInvalid,
    SourceUnavailable,
    UnexpectedFailure
}

public sealed record ManagementFiscalExceptionResult<T>(
    ManagementFiscalExceptionOutcome Outcome,
    Guid CorrelationId,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable)
{
    public static ManagementFiscalExceptionResult<T> Success(T value, Guid correlationId) =>
        new(ManagementFiscalExceptionOutcome.Success, correlationId, value, null, null, false);

    public static ManagementFiscalExceptionResult<T> Failed(
        ManagementFiscalExceptionOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new(outcome, correlationId, default, errorCode, errorMessage, retryable);
}

public sealed record ManagementFiscalExceptionReport(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScope RequestedScope,
    ManagementDashboardScope EffectiveScope,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string TimeBasis,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementFiscalSourceCoverage> SourceCoverage,
    IReadOnlyList<ManagementFiscalLifecycleSummary> LifecycleSummaries,
    IReadOnlyList<ManagementFiscalExceptionSummary> ExceptionSummaries,
    IReadOnlyList<ManagementFiscalCurrencySummary> CurrencySummaries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> UnavailableFacts,
    string SourceAuthority);

public sealed record ManagementFiscalSourceCoverage(
    string SourceId,
    string Availability,
    DateTimeOffset? DataAsOf,
    string Description,
    IReadOnlyList<string> Limitations);

public sealed record ManagementFiscalLifecycleSummary(
    string LifecycleState,
    long Count);

public sealed record ManagementFiscalExceptionSummary(
    string CategoryId,
    string Availability,
    long Count,
    IReadOnlyList<ManagementFiscalAmountSummary> AffectedExpectedAmounts,
    string Definition,
    bool Terminal,
    bool CanResolveLater,
    IReadOnlyList<string> Limitations);

public sealed record ManagementFiscalCurrencySummary(
    string CurrencyCode,
    long IssuanceExpectationCount,
    decimal ExpectedIssuanceAmount,
    long IssuedCount,
    long FailedCount);

public sealed record ManagementFiscalAmountSummary(string CurrencyCode, decimal Amount);

public enum ManagementFiscalExceptionReadStatus
{
    Resolved,
    Unavailable
}

public sealed record ManagementFiscalExceptionReadResult(
    ManagementFiscalExceptionReadStatus Status,
    ManagementFiscalExceptionSourceSnapshot? Snapshot);

public sealed record ManagementFiscalExceptionSourceSnapshot(
    IReadOnlyList<ManagementFiscalAggregateRecord> Records,
    DateTimeOffset? DataAsOf);

public sealed record ManagementFiscalAggregateRecord(
    string FiscalIssuanceState,
    string CurrencyCode,
    long Count,
    decimal ExpectedIssuanceAmount);

public sealed record ManagementFiscalExceptionAuditRecord(
    string EventType,
    string Result,
    string ReasonCode,
    Guid ActorUserId,
    Guid HumanSessionId,
    string? ScopeType,
    Guid? ScopeReference,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string TimeBasis,
    string ResultClassification,
    long AggregateCount,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public interface IManagementFiscalExceptionReportingRepository
{
    Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
        ManagementDashboardActor actor,
        CancellationToken cancellationToken);

    Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
        ManagementDashboardActor actor,
        string scopeType,
        Guid scopeReference,
        CancellationToken cancellationToken);

    Task<ManagementFiscalExceptionReadResult> ReadSummaryAsync(
        ManagementDashboardScopeSnapshot scope,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);

    Task RecordAuditAsync(ManagementFiscalExceptionAuditRecord record, CancellationToken cancellationToken);
}

public interface IManagementFiscalExceptionReportingService
{
    Task<ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>> GetSummaryAsync(
        ManagementDashboardActor actor,
        ManagementFiscalExceptionQuery query,
        CancellationToken cancellationToken);
}

internal static class ManagementFiscalExceptionPeriodParser
{
    public static bool TryParseUtc(string? value, out DateTimeOffset result)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out result) ||
            result.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("+00:00", StringComparison.OrdinalIgnoreCase);
    }
}
