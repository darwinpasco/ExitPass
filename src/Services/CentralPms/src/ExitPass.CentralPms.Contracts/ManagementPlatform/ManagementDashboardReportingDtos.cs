namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record ManagementDashboardCatalogResponse(
    string ContractVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ManagementDashboardCatalogEntryDto> Reports);

public sealed record ManagementDashboardCatalogEntryDto(
    string ReportId,
    string ContractVersion,
    string DisplayTitle,
    string FunctionalDomain,
    string Description,
    IReadOnlyList<string> SupportedScopeTypes,
    string RequiredPermission,
    string Availability,
    string SourceAuthority,
    string PrivacyClassification,
    IReadOnlyList<string> SupportedFilters,
    string FreshnessSemantics,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ManagementDashboardOperationalOverviewResponse(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScopeDto RequestedScope,
    ManagementDashboardScopeDto EffectiveScope,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementDashboardOverviewSectionDto> Sections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ManagementDashboardScopeDto(
    string ScopeType,
    Guid ScopeReference,
    string DisplayName);

public sealed record ManagementDashboardOverviewSectionDto(
    string SectionId,
    string DisplayTitle,
    string Availability,
    string Freshness,
    string SourceAuthority,
    DateTimeOffset? DataAsOf,
    IReadOnlyList<ManagementDashboardMetricDto> Metrics,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ManagementDashboardMetricDto(
    string MetricId,
    string DisplayLabel,
    long Value,
    string Unit);
