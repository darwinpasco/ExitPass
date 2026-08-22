using System.Globalization;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementPaymentReconciliationReportingValues
{
    public const string ContractVersion = "management-platform-payment-reconciliation-reporting:v1";
    public const string ReportId = "payment-reconciliation-summary";
    public const string Policy = "ManagementPlatformPaymentReconciliationSummaryRead";
    public const string Permission = "reconciliation.view";
    public const string FeatureDisabled = "MANAGEMENT_PAYMENT_RECONCILIATION_REPORTING_DISABLED";
    public const string InvalidPeriodStart = "INVALID_PAYMENT_RECONCILIATION_PERIOD_START";
    public const string InvalidPeriodEnd = "INVALID_PAYMENT_RECONCILIATION_PERIOD_END";
    public const string InvalidPeriodRange = "INVALID_PAYMENT_RECONCILIATION_PERIOD_RANGE";
    public const string PeriodTooLong = "PAYMENT_RECONCILIATION_PERIOD_TOO_LONG";
    public const string SourceUnavailable = "PAYMENT_RECONCILIATION_SOURCE_UNAVAILABLE";
    public const string UnexpectedFailure = "MANAGEMENT_PAYMENT_RECONCILIATION_UNEXPECTED_FAILURE";
    public const string NoActivity = "NO_PAYMENT_ACTIVITY_IN_PERIOD";
    public const string SourceAuthority = "CENTRAL_PMS_CANONICAL_PAYMENT_RECORDS";
    public const int MaximumPeriodDays = 31;

    public const string AmountMismatch = "ATTEMPT_CONFIRMATION_AMOUNT_MISMATCH";
    public const string CurrencyMismatch = "ATTEMPT_CONFIRMATION_CURRENCY_MISMATCH";
    public const string DuplicateProviderReference = "DUPLICATE_AUTHORITATIVE_PROVIDER_REFERENCE";
    public const string ConfirmedOutcomeWithoutConfirmation = "CONFIRMED_OUTCOME_WITHOUT_CONFIRMATION";
    public const string ConfirmationAttemptStatusInconsistent = "CONFIRMATION_ATTEMPT_STATUS_INCONSISTENT";
}

public sealed class ManagementPaymentReconciliationReportingOptions
{
    public const string SectionName = "ManagementPlatform:DashboardReporting:PaymentReconciliation";

    public bool Enabled { get; set; }
}

public sealed record ManagementPaymentReconciliationQuery(
    string? ScopeType,
    Guid? ScopeReference,
    string? PeriodStart,
    string? PeriodEnd,
    Guid CorrelationId);

public enum ManagementPaymentReconciliationOutcome
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

public sealed record ManagementPaymentReconciliationResult<T>(
    ManagementPaymentReconciliationOutcome Outcome,
    Guid CorrelationId,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable)
{
    public static ManagementPaymentReconciliationResult<T> Success(T value, Guid correlationId) =>
        new(ManagementPaymentReconciliationOutcome.Success, correlationId, value, null, null, false);

    public static ManagementPaymentReconciliationResult<T> Failed(
        ManagementPaymentReconciliationOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new(outcome, correlationId, default, errorCode, errorMessage, retryable);
}

public sealed record ManagementPaymentReconciliationReport(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScope RequestedScope,
    ManagementDashboardScope EffectiveScope,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementPaymentCurrencySummary> CurrencySummaries,
    IReadOnlyList<ManagementPaymentStatusSummary> PaymentAttemptSummaries,
    IReadOnlyList<ManagementPaymentStatusSummary> ConfirmedPaymentSummaries,
    IReadOnlyList<ManagementPaymentCanonicalStatusSummary> CanonicalStatusSummaries,
    IReadOnlyList<ManagementPaymentChannelSummary> ChannelSummaries,
    IReadOnlyList<ManagementPaymentProviderSummary> ProviderSummaries,
    IReadOnlyList<ManagementInternalReconciliationSummary> InternalReconciliationSummaries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    string SourceAuthority);

public sealed record ManagementPaymentCurrencySummary(
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount);

public sealed record ManagementPaymentStatusSummary(
    string Status,
    string CurrencyCode,
    long Count,
    decimal Amount);

public sealed record ManagementPaymentCanonicalStatusSummary(
    string RecordType,
    string Status,
    string CurrencyCode,
    long Count,
    decimal Amount);

public sealed record ManagementPaymentChannelSummary(
    string ChannelCode,
    string ChannelType,
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount);

public sealed record ManagementPaymentProviderSummary(
    string ProviderCode,
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount,
    long VerifiedOutcomeCount,
    decimal VerifiedOutcomeAmount);

public sealed record ManagementInternalReconciliationSummary(
    string CategoryId,
    string Availability,
    long? Count,
    IReadOnlyList<ManagementReconciliationAmountSummary> Amounts,
    string Definition,
    string MonetaryTreatment,
    IReadOnlyList<string> Limitations);

public sealed record ManagementReconciliationAmountSummary(string CurrencyCode, decimal Amount);

public enum ManagementPaymentReconciliationReadStatus
{
    Resolved,
    Unavailable
}

public sealed record ManagementPaymentReconciliationReadResult(
    ManagementPaymentReconciliationReadStatus Status,
    ManagementPaymentReconciliationSourceSnapshot? Snapshot);

public sealed record ManagementPaymentReconciliationSourceSnapshot(
    IReadOnlyList<ManagementPaymentAggregateRecord> Attempts,
    IReadOnlyList<ManagementPaymentAggregateRecord> Confirmations,
    IReadOnlyList<ManagementPaymentAggregateRecord> ProviderOutcomes,
    IReadOnlyList<ManagementPaymentReconciliationConditionRecord> ReconciliationConditions,
    DateTimeOffset? DataAsOf);

public sealed record ManagementPaymentAggregateRecord(
    string CurrencyCode,
    string Status,
    string ChannelCode,
    string ChannelType,
    string ProviderCode,
    long Count,
    decimal Amount);

public sealed record ManagementPaymentReconciliationConditionRecord(
    string CategoryId,
    string? CurrencyCode,
    long Count,
    decimal? Amount);

public sealed record ManagementPaymentReconciliationAuditRecord(
    string EventType,
    string Result,
    string ReasonCode,
    Guid ActorUserId,
    Guid HumanSessionId,
    string? ScopeType,
    Guid? ScopeReference,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string ResultClassification,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public interface IManagementPaymentReconciliationReportingRepository
{
    Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
        ManagementDashboardActor actor,
        CancellationToken cancellationToken);

    Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
        ManagementDashboardActor actor,
        string scopeType,
        Guid scopeReference,
        CancellationToken cancellationToken);

    Task<ManagementPaymentReconciliationReadResult> ReadSummaryAsync(
        ManagementDashboardScopeSnapshot scope,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);

    Task RecordAuditAsync(ManagementPaymentReconciliationAuditRecord record, CancellationToken cancellationToken);
}

public interface IManagementPaymentReconciliationReportingService
{
    Task<ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>> GetSummaryAsync(
        ManagementDashboardActor actor,
        ManagementPaymentReconciliationQuery query,
        CancellationToken cancellationToken);
}

internal static class ManagementPaymentReconciliationPeriodParser
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
