using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Contracts.ManagementPlatform;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

/// <summary>
/// Orchestrates governed device registration, provisioning, lifecycle, and enrollment.
/// </summary>
/// <remarks>
/// BRD v1.2 Sections 9.16 and 9.21; SDD v1.2 Sections 10 and 14.
/// Central PMS remains the runtime device-trust authority. Management Platform callers
/// can administer canonical identities only through this boundary.
/// </remarks>
public sealed class DeviceAdministrationService : IDeviceAdministrationService
{
    private static readonly TimeSpan ProvisioningLifetime = TimeSpan.FromMinutes(15);
    private readonly IDeviceAdministrationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DeviceAdministrationService(IDeviceAdministrationRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<IReadOnlyList<DeviceSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        _repository.ListAsync(cancellationToken);

    public Task<IReadOnlyList<DeviceAdministrationSiteScopeDto>> ListScopesAsync(CancellationToken cancellationToken) =>
        _repository.ListScopesAsync(cancellationToken);

    public async Task<DeviceDetailsDto> GetAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _repository.GetAsync(RequireId(deviceId, "DEVICE_REFERENCE_REQUIRED"), cancellationToken)
        ?? throw new DeviceAdministrationException("DEVICE_NOT_FOUND");

    public Task<DeviceSummaryDto> RegisterAsync(
        RegisterDeviceRequest request,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var type = NormalizeType(request.DeviceType);
        var name = RequireText(request.DeviceName, 128, "DEVICE_NAME_REQUIRED");
        var terminal = OptionalText(request.TerminalIdentifier, 128, "TERMINAL_IDENTIFIER_INVALID");
        var serial = OptionalText(request.TerminalSerialNumber, 128, "TERMINAL_SERIAL_NUMBER_INVALID");
        if (type == DeviceTypes.AssistedPaymentTerminal && terminal is null)
        {
            throw new DeviceAdministrationException("APT_TERMINAL_IDENTIFIER_REQUIRED");
        }
        if (type == DeviceTypes.AssistedPaymentTerminal && serial is null)
        {
            throw new DeviceAdministrationException("APT_TERMINAL_SERIAL_NUMBER_REQUIRED");
        }

        var deviceId = Guid.NewGuid();
        var reference = $"DEV-{deviceId:N}".ToUpperInvariant();
        var command = new DeviceRegistrationCommand(
            deviceId,
            Guid.NewGuid(),
            reference,
            name,
            type,
            OptionalText(request.Description, 512, "DEVICE_DESCRIPTION_INVALID"),
            RequireId(request.SiteId, "DEVICE_SITE_REQUIRED"),
            request.SiteGroupId,
            terminal,
            serial,
            RequireId(actorUserId, "DEVICE_ADMINISTRATOR_REQUIRED"),
            RequireId(correlationId, "CORRELATION_REQUIRED"),
            _timeProvider.GetUtcNow());
        return _repository.RegisterAsync(command, cancellationToken);
    }

    public async Task<DeviceProvisioningCredentialDto> ProvisionAsync(
        Guid deviceId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var material = CreateProvisioning(deviceId, "ENROLLMENT", actorUserId, correlationId);
        var device = await _repository.IssueProvisioningAsync(material.Command, cancellationToken);
        return ToProvisioning(device, material.RawToken, material.Command);
    }

    public Task<DeviceSummaryDto> CancelProvisioningAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(deviceId, "CANCEL_PROVISIONING", reasonCode, actorUserId, correlationId, cancellationToken);

    public Task<DeviceSummaryDto> SuspendAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(deviceId, "SUSPEND", reasonCode, actorUserId, correlationId, cancellationToken);

    public Task<DeviceSummaryDto> ReactivateAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(deviceId, "REACTIVATE", reasonCode, actorUserId, correlationId, cancellationToken);

    public Task<DeviceSummaryDto> RevokeAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(deviceId, "REVOKE", reasonCode, actorUserId, correlationId, cancellationToken);

    public async Task<DeviceProvisioningCredentialDto> RotateAsync(Guid deviceId, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        var material = CreateProvisioning(deviceId, "ROTATION", actorUserId, correlationId, reasonCode);
        var device = await _repository.IssueProvisioningAsync(material.Command, cancellationToken);
        return ToProvisioning(device, material.RawToken, material.Command);
    }

    public async Task<ReplaceDeviceResultDto> ReplaceAsync(
        Guid deviceId,
        ReplaceDeviceRequest request,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var replacementId = Guid.NewGuid();
        var replacementReference = $"DEV-{replacementId:N}".ToUpperInvariant();
        var material = CreateToken();
        var now = _timeProvider.GetUtcNow();
        return await _repository.ReplaceAsync(
            new DeviceReplacementCommand(
                RequireId(deviceId, "DEVICE_REFERENCE_REQUIRED"),
                replacementId,
                Guid.NewGuid(),
                replacementReference,
                OptionalText(request.DeviceName, 128, "DEVICE_NAME_INVALID"),
                OptionalText(request.Description, 512, "DEVICE_DESCRIPTION_INVALID"),
                request.SiteId,
                request.SiteGroupId,
                OptionalText(request.TerminalIdentifier, 128, "TERMINAL_IDENTIFIER_INVALID"),
                OptionalText(request.TerminalSerialNumber, 128, "TERMINAL_SERIAL_NUMBER_INVALID"),
                RequireReason(request.ReasonCode),
                Hash(material),
                now,
                now.Add(ProvisioningLifetime),
                RequireId(actorUserId, "DEVICE_ADMINISTRATOR_REQUIRED"),
                RequireId(correlationId, "CORRELATION_REQUIRED")),
            material,
            cancellationToken);
    }

    public Task<DeviceEnrollmentResultDto> EnrollAsync(
        EnrollDeviceRequest request,
        string? certificateFingerprint,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var type = NormalizeType(request.DeviceType);
        var token = RequireText(request.ProvisioningCredential, 512, "PROVISIONING_CREDENTIAL_REQUIRED");
        if (token.Length < 48)
        {
            throw new DeviceAdministrationException("PROVISIONING_CREDENTIAL_INVALID");
        }

        string? runtimeCredential = null;
        string fingerprint;
        string credentialReference;
        string credentialType;
        if (type == DeviceTypes.OperatorConsole)
        {
            runtimeCredential = CreateToken();
            fingerprint = Hash(runtimeCredential);
            credentialReference = $"device-browser-sha256:{fingerprint}";
            credentialType = "API_KEY_REFERENCE";
        }
        else
        {
            fingerprint = NormalizeFingerprint(certificateFingerprint);
            credentialReference = $"device-mtls-sha256:{fingerprint}";
            credentialType = "MTLS_CERTIFICATE_REFERENCE";
        }

        var terminalSerialNumber = OptionalText(
            request.TerminalSerialNumber,
            128,
            "TERMINAL_SERIAL_NUMBER_INVALID");
        if (type == DeviceTypes.AssistedPaymentTerminal && terminalSerialNumber is null)
        {
            throw new DeviceAdministrationException("APT_TERMINAL_SERIAL_NUMBER_REQUIRED");
        }

        return _repository.EnrollAsync(
            new DeviceEnrollmentCommand(
                Hash(token),
                RequireText(request.DeviceReference, 64, "DEVICE_REFERENCE_REQUIRED"),
                type,
                RequireId(request.SiteId, "DEVICE_SITE_REQUIRED"),
                request.SiteGroupId,
                OptionalText(request.TerminalIdentifier, 128, "TERMINAL_IDENTIFIER_INVALID"),
                terminalSerialNumber,
                fingerprint,
                credentialReference,
                credentialType,
                RequireId(correlationId, "CORRELATION_REQUIRED"),
                _timeProvider.GetUtcNow()),
            runtimeCredential,
            cancellationToken);
    }

    private Task<DeviceSummaryDto> ChangeLifecycleAsync(Guid deviceId, string action, string reasonCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ChangeLifecycleAsync(new(
            RequireId(deviceId, "DEVICE_REFERENCE_REQUIRED"),
            action,
            RequireReason(reasonCode),
            RequireId(actorUserId, "DEVICE_ADMINISTRATOR_REQUIRED"),
            RequireId(correlationId, "CORRELATION_REQUIRED"),
            _timeProvider.GetUtcNow()), cancellationToken);

    private (string RawToken, DeviceProvisioningCommand Command) CreateProvisioning(Guid deviceId, string purpose, Guid actorUserId, Guid correlationId, string? reasonCode = null)
    {
        var token = CreateToken();
        var now = _timeProvider.GetUtcNow();
        return (token, new(
            RequireId(deviceId, "DEVICE_REFERENCE_REQUIRED"),
            Hash(token),
            purpose,
            now,
            now.Add(ProvisioningLifetime),
            RequireId(actorUserId, "DEVICE_ADMINISTRATOR_REQUIRED"),
            RequireId(correlationId, "CORRELATION_REQUIRED"),
            reasonCode is null ? null : RequireReason(reasonCode)));
    }

    private static DeviceProvisioningCredentialDto ToProvisioning(DeviceSummaryDto device, string token, DeviceProvisioningCommand command) =>
        new(device.DeviceId, device.DeviceReference, device.DeviceType, device.SiteId, device.SiteGroupId,
            device.TerminalIdentifier, device.TerminalSerialNumber, token, command.ExpiresAt, command.Purpose,
            "Shown once. Store it only on the intended device; it cannot be recovered.", command.CorrelationId);

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeType(string value)
    {
        var normalized = RequireText(value, 64, "DEVICE_TYPE_REQUIRED").ToUpperInvariant();
        if (!DeviceTypes.IsKnown(normalized)) throw new DeviceAdministrationException("DEVICE_TYPE_UNSUPPORTED");
        return normalized;
    }

    private static string NormalizeFingerprint(string? value)
    {
        var normalized = (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new DeviceAdministrationException("APT_CLIENT_CERTIFICATE_REQUIRED");
        return normalized;
    }

    private static Guid RequireId(Guid value, string code) =>
        value != Guid.Empty ? value : throw new DeviceAdministrationException(code);

    private static string RequireReason(string value) => RequireText(value, 64, "DEVICE_REASON_REQUIRED").ToUpperInvariant();

    private static string RequireText(string? value, int maximum, string code)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum)
            throw new DeviceAdministrationException(code);
        return normalized;
    }

    private static string? OptionalText(string? value, int maximum, string code)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum) throw new DeviceAdministrationException(code);
        return normalized;
    }
}
