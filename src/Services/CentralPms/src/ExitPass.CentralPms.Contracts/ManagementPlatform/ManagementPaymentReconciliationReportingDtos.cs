namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record ManagementPaymentReconciliationSummaryResponse(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScopeDto RequestedScope,
    ManagementDashboardScopeDto EffectiveScope,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementPaymentCurrencySummaryDto> CurrencySummaries,
    IReadOnlyList<ManagementPaymentStatusSummaryDto> PaymentAttemptSummaries,
    IReadOnlyList<ManagementPaymentStatusSummaryDto> ConfirmedPaymentSummaries,
    IReadOnlyList<ManagementPaymentCanonicalStatusSummaryDto> CanonicalStatusSummaries,
    IReadOnlyList<ManagementPaymentChannelSummaryDto> ChannelSummaries,
    IReadOnlyList<ManagementPaymentProviderSummaryDto> ProviderSummaries,
    IReadOnlyList<ManagementInternalReconciliationSummaryDto> InternalReconciliationSummaries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    string SourceAuthority);

public sealed record ManagementPaymentCurrencySummaryDto(
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount);

public sealed record ManagementPaymentStatusSummaryDto(
    string Status,
    string CurrencyCode,
    long Count,
    decimal Amount);

public sealed record ManagementPaymentCanonicalStatusSummaryDto(
    string RecordType,
    string Status,
    string CurrencyCode,
    long Count,
    decimal Amount);

public sealed record ManagementPaymentChannelSummaryDto(
    string ChannelCode,
    string ChannelType,
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount);

public sealed record ManagementPaymentProviderSummaryDto(
    string ProviderCode,
    string CurrencyCode,
    long AttemptCount,
    decimal AttemptedAmount,
    long ConfirmedCount,
    decimal ConfirmedAmount,
    long VerifiedOutcomeCount,
    decimal VerifiedOutcomeAmount);

public sealed record ManagementInternalReconciliationSummaryDto(
    string CategoryId,
    string Availability,
    long? Count,
    IReadOnlyList<ManagementReconciliationAmountSummaryDto> Amounts,
    string Definition,
    string MonetaryTreatment,
    IReadOnlyList<string> Limitations);

public sealed record ManagementReconciliationAmountSummaryDto(string CurrencyCode, decimal Amount);
