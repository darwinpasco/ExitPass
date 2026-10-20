using ExitPass.CentralPms.Contracts.ManagementPlatform;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class DeviceTypes
{
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string AssistedPaymentTerminal = "ASSISTED_PAYMENT_TERMINAL";

    public static bool IsKnown(string value) =>
        value is OperatorConsole or AssistedPaymentTerminal;
}

public sealed class DeviceAdministrationException : Exception
{
    public DeviceAdministrationException(string errorCode) : base(errorCode) => ErrorCode = errorCode;
    public string ErrorCode { get; }
}

public sealed record DeviceRegistrationCommand(
    Guid DeviceId,
    Guid ServiceIdentityId,
    string DeviceReference,
    string DeviceName,
    string DeviceType,
    string? Description,
    Guid SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber,
    Guid ActorUserId,
    Guid CorrelationId,
    DateTimeOffset Now);

public sealed record DeviceProvisioningCommand(
    Guid DeviceId,
    string TokenHash,
    string Purpose,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    Guid ActorUserId,
    Guid CorrelationId,
    string? ReasonCode = null);

public sealed record DeviceEnrollmentCommand(
    string TokenHash,
    string DeviceReference,
    string DeviceType,
    Guid SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber,
    string CredentialFingerprint,
    string CredentialReference,
    string CredentialType,
    Guid CorrelationId,
    DateTimeOffset Now);

public sealed record DeviceLifecycleCommand(
    Guid DeviceId,
    string Action,
    string ReasonCode,
    Guid ActorUserId,
    Guid CorrelationId,
    DateTimeOffset Now);

public sealed record DeviceReplacementCommand(
    Guid DeviceId,
    Guid ReplacementDeviceId,
    Guid ReplacementServiceIdentityId,
    string ReplacementReference,
    string? DeviceName,
    string? Description,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TerminalIdentifier,
    string? TerminalSerialNumber,
    string ReasonCode,
    string TokenHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    Guid ActorUserId,
    Guid CorrelationId);

public interface IDeviceAdministrationRepository
{
    Task<IReadOnlyList<DeviceAdministrationSiteScopeDto>> ListScopesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<DeviceDetailsDto?> GetAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> RegisterAsync(DeviceRegistrationCommand command, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> IssueProvisioningAsync(DeviceProvisioningCommand command, CancellationToken cancellationToken);
    Task<DeviceEnrollmentResultDto> EnrollAsync(DeviceEnrollmentCommand command, string? runtimeCredential, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> ChangeLifecycleAsync(DeviceLifecycleCommand command, CancellationToken cancellationToken);
    Task<ReplaceDeviceResultDto> ReplaceAsync(DeviceReplacementCommand command, string rawToken, CancellationToken cancellationToken);
}

public interface IDeviceAdministrationService
{
    Task<IReadOnlyList<DeviceAdministrationSiteScopeDto>> ListScopesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<DeviceDetailsDto> GetAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> RegisterAsync(RegisterDeviceRequest request, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceProvisioningCredentialDto> ProvisionAsync(Guid deviceId, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> CancelProvisioningAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> SuspendAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> ReactivateAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceSummaryDto> RevokeAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceProvisioningCredentialDto> RotateAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<ReplaceDeviceResultDto> ReplaceAsync(Guid deviceId, ReplaceDeviceRequest request, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<DeviceEnrollmentResultDto> EnrollAsync(EnrollDeviceRequest request, string? certificateFingerprint, Guid correlationId, CancellationToken cancellationToken);
}
