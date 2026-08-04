using ExitPass.CentralPms.Application.StatutoryEvidence;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementPlatformStatutoryEvidenceGovernanceValues
{
    public const string PolicyName = "StatutoryEvidenceGovernanceView";
    public const string Permission = "statutory-discounts.evidence-governance.view";
    public const string ContractVersion = "management-platform-statutory-evidence-governance:v1";

    public const string ScopeTypeSite = "SITE";
    public const string ScopeTypeSiteGroup = "SITE_GROUP";

    public const string ConfiguredReady = "CONFIGURED_READY";
    public const string ConfiguredPartiallyReady = "CONFIGURED_PARTIALLY_READY";
    public const string ConfigurationIncomplete = "CONFIGURATION_INCOMPLETE";
    public const string CaptureDisabled = "CAPTURE_DISABLED";
    public const string ConfigurationUnavailable = "CONFIGURATION_UNAVAILABLE";
    public const string Unknown = "UNKNOWN";

    public const string Ready = "READY";
    public const string PartiallyReady = "PARTIALLY_READY";
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string Disabled = "DISABLED";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string Unavailable = "UNAVAILABLE";
    public const string Stale = "STALE";

    public const string Fresh = "FRESH";
    public const string StaleFreshness = "STALE";

    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string ScopeDenied = "SCOPE_DENIED";
    public const string SiteScopeDenied = "SITE_SCOPE_DENIED";
    public const string SiteGroupScopeDenied = "SITE_GROUP_SCOPE_DENIED";
    public const string EmptyAuthorizedScope = "EMPTY_AUTHORIZED_SCOPE";
    public const string InvalidFilter = "INVALID_FILTER";
    public const string ConfigurationSourceUnavailable = "CONFIGURATION_UNAVAILABLE";
    public const string MalformedCanonicalConfiguration = "MALFORMED_CANONICAL_CONFIGURATION";
    public const string TransientDatabaseFailure = "TRANSIENT_DATABASE_FAILURE";
    public const string UnexpectedFailure = "UNEXPECTED_INTERNAL_FAILURE";

    public const string WarningCaptureDisabled = "CAPTURE_DISABLED";
    public const string WarningUploadProfileIncomplete = "UPLOAD_PROFILE_INCOMPLETE";
    public const string WarningMaximumSizeNotConfigured = "MAXIMUM_SIZE_NOT_CONFIGURED";
    public const string WarningAllowedMediaNotConfigured = "ALLOWED_MEDIA_NOT_CONFIGURED";
    public const string WarningUploadTtlInvalid = "UPLOAD_TTL_INVALID";
    public const string WarningProtectedStorageNotConfigured = "PROTECTED_STORAGE_NOT_CONFIGURED";
    public const string WarningStoragePrivacyUnverified = "STORAGE_PRIVACY_UNVERIFIED";
    public const string WarningEncryptionPostureUnknown = "ENCRYPTION_POSTURE_UNKNOWN";
    public const string WarningChecksumConfigurationIncomplete = "CHECKSUM_CONFIGURATION_INCOMPLETE";
    public const string WarningRetentionPolicyUnavailable = "RETENTION_POLICY_UNAVAILABLE";
    public const string WarningMalwareScanningNotImplemented = "MALWARE_SCANNING_NOT_IMPLEMENTED";
    public const string WarningSecurePreviewNotImplemented = "SECURE_PREVIEW_NOT_IMPLEMENTED";
    public const string WarningRetentionWorkerNotImplemented = "RETENTION_WORKER_NOT_IMPLEMENTED";
    public const string WarningDeletionWorkerNotImplemented = "DELETION_WORKER_NOT_IMPLEMENTED";
    public const string WarningObjectReconciliationNotImplemented = "OBJECT_RECONCILIATION_NOT_IMPLEMENTED";
    public const string WarningConfigurationStale = "CONFIGURATION_STALE";

    public static readonly string[] EntitlementTypes = ["SENIOR_CITIZEN", "PWD"];
}

public sealed record ManagementPlatformStatutoryEvidenceGovernanceQuery(
    string? ScopeType,
    Guid? ScopeReference,
    string? EntitlementType,
    string? GovernanceStatus,
    string? ReadinessStatus,
    bool? CaptureEnabled,
    bool IncludeStale,
    Guid CorrelationId,
    Guid? ActorUserId,
    Guid? ActorServiceIdentityId);

public sealed record ManagementPlatformStatutoryEvidenceGovernanceResult(
    ManagementPlatformStatutoryEvidenceGovernanceOutcome Outcome,
    Guid CorrelationId,
    ManagementPlatformStatutoryEvidenceGovernance? Governance,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable)
{
    public static ManagementPlatformStatutoryEvidenceGovernanceResult Success(
        ManagementPlatformStatutoryEvidenceGovernance governance) =>
        new(ManagementPlatformStatutoryEvidenceGovernanceOutcome.Success, governance.CorrelationId, governance, null, null, false);

    public static ManagementPlatformStatutoryEvidenceGovernanceResult Failed(
        ManagementPlatformStatutoryEvidenceGovernanceOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new(outcome, correlationId, null, errorCode, errorMessage, retryable);
}

public enum ManagementPlatformStatutoryEvidenceGovernanceOutcome
{
    Success,
    InvalidFilter,
    ScopeDenied,
    EmptyAuthorizedScope,
    ConfigurationUnavailable,
    MalformedCanonicalConfiguration,
    TransientDatabaseFailure,
    UnexpectedFailure
}

public sealed record ManagementPlatformStatutoryEvidenceGovernance(
    string ContractVersion,
    string? RequestedScopeType,
    Guid? RequestedScopeReference,
    Guid CorrelationId,
    DateTimeOffset EvaluatedAt,
    string FreshnessStatus,
    bool Stale,
    IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceSite> Sites,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);

public sealed record ManagementPlatformStatutoryEvidenceGovernanceSite(
    Guid SiteReference,
    string? SiteDisplayName,
    Guid SiteGroupReference,
    string? SiteGroupDisplayName,
    IReadOnlyList<string> EntitlementTypesSupported,
    string GovernanceStatus,
    string ReadinessStatus,
    bool EvidenceCaptureConfigured,
    bool EvidenceCaptureEnabled,
    IReadOnlyList<ManagementPlatformStatutoryEvidenceDocumentProfile> RequiredDocumentProfiles,
    IReadOnlyList<string> AllowedMediaTypes,
    long? MaximumUploadSizeBytes,
    int? UploadAuthorizationTtlSeconds,
    string UploadAuthorizationReadiness,
    string UploadFinalizationReadiness,
    string ProtectedStorageProviderClassification,
    string ProtectedStorageReadiness,
    string StoragePrivateAccessPosture,
    string ServerSideEncryptionPosture,
    string ChecksumVerificationReadiness,
    string ProviderMetadataVerificationReadiness,
    string UploadLifecycleReadiness,
    string ValidationLifecycleReadiness,
    string MalwareScanLifecycleReadiness,
    string ReviewabilityLifecycleReadiness,
    string BindingLifecycleReadiness,
    string HoldLifecycleReadiness,
    string DeletionRequestLifecycleReadiness,
    string MalwareScanningExecutionReadiness,
    string SecurePreviewReadiness,
    string RetentionPolicyReadiness,
    string RetentionWorkerReadiness,
    string DeletionWorkerReadiness,
    string ObjectReconciliationReadiness,
    DateTimeOffset LastEvaluatedAt,
    DateTimeOffset? ConfigurationUpdatedAt,
    string FreshnessStatus,
    bool Stale,
    bool Retryable,
    string SupportReference,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);

public sealed record ManagementPlatformStatutoryEvidenceDocumentProfile(
    string ProfileCode,
    string ProfileVersion,
    string RetentionClassCode,
    string RetentionPolicyVersion,
    string RetentionPolicyStatus,
    bool RetentionPolicyApproved);

public sealed record ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
    ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus Status,
    IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceScopeSite> Sites);

public enum ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus
{
    Resolved,
    Denied,
    Empty,
    SourceUnavailable,
    Malformed
}

public sealed record ManagementPlatformStatutoryEvidenceGovernanceScopeSite(
    Guid SiteId,
    string? SiteName,
    Guid SiteGroupId,
    string? SiteGroupName);

public sealed record ManagementPlatformStatutoryEvidenceGovernanceConfiguration(
    IReadOnlyList<ManagementPlatformStatutoryEvidenceDocumentProfile> RetentionPolicies,
    IReadOnlySet<Guid> CaptureEnabledSiteIds,
    bool HasMetadataTables,
    bool HasUploadAuthorizationTable,
    DateTimeOffset? LastConfigurationUpdatedAt);

public sealed record ManagementPlatformStatutoryEvidenceGovernanceActor(
    Guid? UserId,
    Guid? ServiceIdentityId);

public interface IManagementPlatformStatutoryEvidenceGovernanceRepository
{
    Task<ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult> ResolveScopeAsync(
        ManagementPlatformStatutoryEvidenceGovernanceActor actor,
        string? scopeType,
        Guid? scopeReference,
        CancellationToken cancellationToken);

    Task<ManagementPlatformStatutoryEvidenceGovernanceConfiguration> ReadConfigurationAsync(
        IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceScopeSite> sites,
        CancellationToken cancellationToken);
}

public interface IManagementPlatformStatutoryEvidenceGovernanceService
{
    Task<ManagementPlatformStatutoryEvidenceGovernanceResult> ReadGovernanceAsync(
        ManagementPlatformStatutoryEvidenceGovernanceQuery query,
        CancellationToken cancellationToken);
}
