namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record RegisterDeviceRequest(
    string DeviceName,
    string DeviceType,
    string? Description,
    Guid SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber = null);

public sealed record DeviceAdministrationSiteScopeDto(
    Guid SiteId,
    string SiteCode,
    string SiteName,
    Guid SiteGroupId,
    string SiteGroupCode,
    string SiteGroupName);

public sealed record DeviceLifecycleRequest(string ReasonCode);

public sealed record ReplaceDeviceRequest(
    string? DeviceName,
    string? Description,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string ReasonCode,
    string? TerminalSerialNumber = null);

public sealed record EnrollDeviceRequest(
    string ProvisioningCredential,
    string DeviceReference,
    string DeviceType,
    Guid SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber = null);

public sealed record DeviceSummaryDto(
    Guid DeviceId,
    string DeviceReference,
    string DeviceName,
    string DeviceType,
    string? Description,
    string? TerminalIdentifier,
    string? TerminalSerialNumber,
    Guid ServiceIdentityId,
    Guid SiteId,
    string SiteCode,
    string SiteName,
    Guid SiteGroupId,
    string SiteGroupCode,
    string SiteGroupName,
    string LifecycleStatus,
    string? LastProvisioningStatus,
    long CredentialVersion,
    string CredentialStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long RowVersion);

public sealed record DeviceProvisioningHistoryDto(
    Guid ProvisioningReference,
    string Purpose,
    string Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset? CancelledAt,
    long IntendedCredentialVersion,
    Guid CorrelationId);

public sealed record DeviceCredentialHistoryDto(
    long CredentialVersion,
    string CredentialType,
    string Status,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevocationReasonCode,
    Guid CorrelationId);

public sealed record DeviceAdministrativeActionDto(
    string Action,
    string? PreviousState,
    string ResultingState,
    Guid SiteId,
    Guid? SiteGroupId,
    long? PreviousCredentialVersion,
    long ResultingCredentialVersion,
    Guid? ActorUserId,
    Guid? ActorServiceIdentityId,
    string? ReasonCode,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public sealed record DeviceDetailsDto(
    DeviceSummaryDto Device,
    IReadOnlyList<DeviceProvisioningHistoryDto> ProvisioningHistory,
    IReadOnlyList<DeviceCredentialHistoryDto> CredentialHistory,
    IReadOnlyList<DeviceAdministrativeActionDto> RecentAdministrativeActions,
    Guid? PredecessorDeviceId,
    string? PredecessorDeviceReference,
    Guid? ReplacementDeviceId,
    string? ReplacementDeviceReference);

public sealed record DeviceProvisioningCredentialDto(
    Guid DeviceId,
    string DeviceReference,
    string DeviceType,
    Guid SiteId,
    Guid SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber,
    string ProvisioningCredential,
    DateTimeOffset ExpiresAt,
    string Purpose,
    string DisplayPolicy,
    Guid CorrelationId);

public sealed record DeviceEnrollmentResultDto(
    Guid DeviceId,
    string DeviceReference,
    Guid ServiceIdentityId,
    string DeviceType,
    Guid SiteId,
    Guid SiteGroupId,
    long CredentialVersion,
    string? RuntimeCredential,
    string RuntimeCredentialType,
    string DisplayPolicy,
    Guid CorrelationId);

public sealed record ReplaceDeviceResultDto(
    DeviceSummaryDto ReplacedDevice,
    DeviceSummaryDto ReplacementDevice,
    DeviceProvisioningCredentialDto Provisioning);
