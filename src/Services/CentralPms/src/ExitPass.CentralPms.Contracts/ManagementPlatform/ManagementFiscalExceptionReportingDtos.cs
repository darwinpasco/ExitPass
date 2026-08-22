namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record ManagementFiscalExceptionSummaryResponse(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScopeDto RequestedScope,
    ManagementDashboardScopeDto EffectiveScope,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string TimeBasis,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementFiscalSourceCoverageDto> SourceCoverage,
    IReadOnlyList<ManagementFiscalLifecycleSummaryDto> LifecycleSummaries,
    IReadOnlyList<ManagementFiscalExceptionSummaryDto> ExceptionSummaries,
    IReadOnlyList<ManagementFiscalCurrencySummaryDto> CurrencySummaries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> UnavailableFacts,
    string SourceAuthority);

public sealed record ManagementFiscalSourceCoverageDto(
    string SourceId,
    string Availability,
    DateTimeOffset? DataAsOf,
    string Description,
    IReadOnlyList<string> Limitations);

public sealed record ManagementFiscalLifecycleSummaryDto(string LifecycleState, long Count);

public sealed record ManagementFiscalExceptionSummaryDto(
    string CategoryId,
    string Availability,
    long Count,
    IReadOnlyList<ManagementFiscalAmountSummaryDto> AffectedExpectedAmounts,
    string Definition,
    bool Terminal,
    bool CanResolveLater,
    IReadOnlyList<string> Limitations);

public sealed record ManagementFiscalCurrencySummaryDto(
    string CurrencyCode,
    long IssuanceExpectationCount,
    decimal ExpectedIssuanceAmount,
    long IssuedCount,
    long FailedCount);

public sealed record ManagementFiscalAmountSummaryDto(string CurrencyCode, decimal Amount);
