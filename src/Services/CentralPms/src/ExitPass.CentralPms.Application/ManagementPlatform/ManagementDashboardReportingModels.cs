namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementDashboardReportingValues
{
    public const string ContractVersion = "management-platform-dashboard-reporting:v1";
    public const string CatalogPolicy = "ManagementPlatformDashboardCatalogRead";
    public const string OverviewPolicy = "ManagementPlatformOperationalOverviewRead";
    public const string CatalogPermission = "reports.view";
    public const string OverviewPermission = "dashboard.view";

    public const string ScopeSite = "SITE";
    public const string ScopeSiteGroup = "SITE_GROUP";

    public const string Available = "AVAILABLE";
    public const string Partial = "PARTIAL";
    public const string Unavailable = "UNAVAILABLE";
    public const string NotApplicable = "NOT_APPLICABLE";

    public const string Current = "CURRENT";
    public const string Stale = "STALE";

    public const string OperationalOverviewReportId = "operational-overview";
    public const string PaymentReconciliationReportId = "payment-reconciliation-summary";
    public const string FiscalExceptionReportId = "fiscal-exception-summary";
    public const string ManagementActivityReportId = "management-activity-summary";

    public const string FeatureDisabled = "MANAGEMENT_DASHBOARD_REPORTING_DISABLED";
    public const string InvalidScopeType = "INVALID_DASHBOARD_SCOPE_TYPE";
    public const string InvalidScopeReference = "INVALID_DASHBOARD_SCOPE_REFERENCE";
    public const string ScopeNotFoundOrDenied = "DASHBOARD_SCOPE_NOT_FOUND_OR_DENIED";
    public const string SessionInvalid = "DASHBOARD_SESSION_INVALID";
    public const string SourceUnavailable = "DASHBOARD_SOURCE_UNAVAILABLE";
    public const string UnexpectedFailure = "MANAGEMENT_DASHBOARD_UNEXPECTED_FAILURE";
    public const string ProjectionSourceUnavailable = "VENDOR_PROJECTION_SOURCE_UNAVAILABLE";
    public const string ProjectionNotConfigured = "VENDOR_PROJECTION_NOT_CONFIGURED";
    public const string ProjectionStale = "VENDOR_PROJECTION_STALE";
}

public sealed class ManagementDashboardReportingOptions
{
    public const string SectionName = "ManagementPlatform:DashboardReporting";

    public bool Enabled { get; set; }

    public int ProjectionStaleAfterMinutes { get; set; } = 15;
}

public sealed record ManagementDashboardActor(Guid UserId, Guid HumanSessionId);

public sealed record ManagementDashboardOperationalOverviewQuery(
    string? ScopeType,
    Guid? ScopeReference,
    Guid CorrelationId);

public enum ManagementDashboardReportingOutcome
{
    Success,
    FeatureDisabled,
    InvalidScope,
    ScopeNotFoundOrDenied,
    SessionInvalid,
    SourceUnavailable,
    UnexpectedFailure
}

public sealed record ManagementDashboardReportingResult<T>(
    ManagementDashboardReportingOutcome Outcome,
    Guid CorrelationId,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable)
{
    public static ManagementDashboardReportingResult<T> Success(T value, Guid correlationId) =>
        new(ManagementDashboardReportingOutcome.Success, correlationId, value, null, null, false);

    public static ManagementDashboardReportingResult<T> Failed(
        ManagementDashboardReportingOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new(outcome, correlationId, default, errorCode, errorMessage, retryable);
}

public sealed record ManagementDashboardCatalog(
    string ContractVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ManagementDashboardCatalogEntry> Reports);

public sealed record ManagementDashboardCatalogEntry(
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

public sealed record ManagementDashboardOperationalOverview(
    string ContractVersion,
    string ReportId,
    ManagementDashboardScope RequestedScope,
    ManagementDashboardScope EffectiveScope,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataAsOf,
    string Availability,
    string Freshness,
    Guid CorrelationId,
    IReadOnlyList<ManagementDashboardOverviewSection> Sections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ManagementDashboardScope(
    string ScopeType,
    Guid ScopeReference,
    string DisplayName);

public sealed record ManagementDashboardOverviewSection(
    string SectionId,
    string DisplayTitle,
    string Availability,
    string Freshness,
    string SourceAuthority,
    DateTimeOffset? DataAsOf,
    IReadOnlyList<ManagementDashboardMetric> Metrics,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ManagementDashboardMetric(
    string MetricId,
    string DisplayLabel,
    long Value,
    string Unit);

public enum ManagementDashboardScopeReadStatus
{
    Resolved,
    Denied,
    SourceUnavailable
}

public enum ManagementDashboardActorValidationStatus
{
    Valid,
    Invalid,
    SourceUnavailable
}

public sealed record ManagementDashboardScopeReadResult(
    ManagementDashboardScopeReadStatus Status,
    ManagementDashboardScopeSnapshot? Scope);

public sealed record ManagementDashboardScopeSnapshot(
    string ScopeType,
    Guid ScopeReference,
    string DisplayName,
    DateTimeOffset DataAsOf,
    IReadOnlyList<ManagementDashboardSiteSnapshot> Sites);

public sealed record ManagementDashboardSiteSnapshot(
    Guid SiteId,
    string Status,
    bool PaymentEnabled,
    DateTimeOffset UpdatedAt);

public enum ManagementDashboardProjectionReadStatus
{
    Resolved,
    Unavailable
}

public sealed record ManagementDashboardProjectionReadResult(
    ManagementDashboardProjectionReadStatus Status,
    IReadOnlyList<ManagementDashboardProjectionTargetSnapshot> Targets);

public sealed record ManagementDashboardProjectionTargetSnapshot(
    bool Enabled,
    string HealthStatus,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LatestProjectionAt,
    long ActiveProjectionCount);

public sealed record ManagementDashboardAuditRecord(
    string EventType,
    string Result,
    string ReasonCode,
    string ReportId,
    Guid ActorUserId,
    Guid HumanSessionId,
    string? ScopeType,
    Guid? ScopeReference,
    string ResultClassification,
    string SourceClassification,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public interface IManagementDashboardReportingRepository
{
    Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
        ManagementDashboardActor actor,
        CancellationToken cancellationToken);

    Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
        ManagementDashboardActor actor,
        string scopeType,
        Guid scopeReference,
        CancellationToken cancellationToken);

    Task<ManagementDashboardProjectionReadResult> ReadProjectionHealthAsync(
        ManagementDashboardScopeSnapshot scope,
        CancellationToken cancellationToken);

    Task RecordAuditAsync(ManagementDashboardAuditRecord record, CancellationToken cancellationToken);
}

public interface IManagementDashboardReportingService
{
    Task<ManagementDashboardReportingResult<ManagementDashboardCatalog>> GetCatalogAsync(
        ManagementDashboardActor actor,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>> GetOperationalOverviewAsync(
        ManagementDashboardActor actor,
        ManagementDashboardOperationalOverviewQuery query,
        CancellationToken cancellationToken);
}

public sealed class ManagementDashboardSourceUnavailableException : Exception
{
    public ManagementDashboardSourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
