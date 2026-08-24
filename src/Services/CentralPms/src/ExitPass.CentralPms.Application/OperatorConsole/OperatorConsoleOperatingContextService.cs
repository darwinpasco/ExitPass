using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed class OperatorConsoleOperatingContextService : IOperatorConsoleOperatingContextService
{
    private static readonly HashSet<string> TrustedLevels = new(StringComparer.Ordinal)
    {
        "BROWSER_KEY_ONLY",
        "BROWSER_KEY_AND_MTLS"
    };

    private readonly IOperatorConsoleOperatingContextRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OperatorConsoleOperatingContextService> _logger;

    public OperatorConsoleOperatingContextService(
        IOperatorConsoleOperatingContextRepository repository,
        TimeProvider timeProvider,
        ILogger<OperatorConsoleOperatingContextService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HashDeviceProof(string proof)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length < 32 || proof.Length > 512)
        {
            throw new ArgumentException("Device proof is invalid.", nameof(proof));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
    }

    public async Task<OperatorConsoleOperatingContextResult> ValidateDeviceProofAsync(
        string? proof,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryHash(proof, out var thumbprint))
        {
            return Denied(OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired, correlationId);
        }

        var now = _timeProvider.GetUtcNow();
        var device = await _repository.FindDeviceByProofAsync(thumbprint, now, cancellationToken);
        var failure = ClassifyDevice(device, now);
        if (failure is not null)
        {
            return Denied(failure, correlationId);
        }

        return new OperatorConsoleOperatingContextResult(true, null, null, correlationId);
    }

    public async Task<OperatorConsoleDeviceCookieIssueResult> EstablishDeviceBindingAsync(
        string? provisioningProof,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryHash(provisioningProof, out var currentThumbprint))
        {
            return new(false, null, OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid, correlationId);
        }

        var now = _timeProvider.GetUtcNow();
        var device = await _repository.FindDeviceByProofAsync(currentThumbprint, now, cancellationToken);
        var failure = ClassifyDevice(device, now);
        if (failure is not null)
        {
            Denied(failure, correlationId);
            return new(false, null, failure, correlationId);
        }

        var cookieCredential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var replacementThumbprint = HashDeviceProof(cookieCredential);
        if (!await _repository.RotateDeviceProofAsync(
                device!.OperatorDeviceBindingId,
                currentThumbprint,
                replacementThumbprint,
                now,
                correlationId,
                cancellationToken))
        {
            return new(false, null, OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid, correlationId);
        }

        _logger.LogInformation(
            "Operator Console device binding credential rotated for device {OperatorDeviceBindingId} and correlation {CorrelationId}.",
            device.OperatorDeviceBindingId,
            correlationId);
        return new(true, cookieCredential, null, correlationId);
    }

    public async Task<OperatorConsoleOperatingContextResult> BindSessionAsync(
        Guid humanSessionId,
        Guid userId,
        IReadOnlyList<Guid> authorizedSiteIds,
        IReadOnlyList<Guid> authorizedSiteGroupIds,
        bool hasGlobalScope,
        string? proof,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ValidateIds(humanSessionId, userId, correlationId);
        if (!TryHash(proof, out var thumbprint))
        {
            return Denied(OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired, correlationId);
        }

        var now = _timeProvider.GetUtcNow();
        var device = await _repository.FindDeviceByProofAsync(thumbprint, now, cancellationToken);
        var deviceFailure = ClassifyDevice(device, now);
        if (deviceFailure is not null)
        {
            return Denied(deviceFailure, correlationId);
        }

        var resolvedDevice = device!;
        var siteAuthorized = hasGlobalScope ||
            authorizedSiteIds.Contains(resolvedDevice.SiteId) ||
            authorizedSiteGroupIds.Contains(resolvedDevice.SiteGroupId);
        if (!siteAuthorized)
        {
            return Denied(OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite, correlationId);
        }

        var shift = await _repository.ResolveShiftAsync(
            userId,
            resolvedDevice.SiteId,
            resolvedDevice.SiteGroupId,
            authorizedSiteIds,
            authorizedSiteGroupIds,
            hasGlobalScope,
            now,
            cancellationToken);
        var shiftFailure = ClassifyShift(shift);
        if (shiftFailure is not null)
        {
            return Denied(shiftFailure, correlationId);
        }

        var snapshot = await _repository.ReadSessionBindingSnapshotAsync(humanSessionId, userId, cancellationToken);
        if (snapshot is null || !string.Equals(snapshot.SessionStatus, "ACTIVE", StringComparison.Ordinal) ||
            snapshot.IdleExpiresAt <= now || snapshot.AbsoluteExpiresAt <= now)
        {
            return Denied(OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, correlationId);
        }

        var context = await _repository.BindSessionAsync(
            humanSessionId,
            userId,
            resolvedDevice.OperatorDeviceBindingId,
            shift.OperatorShiftId!.Value,
            resolvedDevice.SiteId,
            resolvedDevice.SiteGroupId,
            snapshot.AuthorizationEpoch,
            snapshot.CredentialVersion,
            now,
            correlationId,
            cancellationToken);

        _logger.LogInformation(
            "Operator Console operating context bound for session {HumanSessionId}, device {OperatorDeviceBindingId}, shift {OperatorShiftId}, site {SiteId}, and correlation {CorrelationId}.",
            humanSessionId,
            context.OperatorDeviceBindingId,
            context.OperatorShiftId,
            context.SiteId,
            correlationId);
        return OperatorConsoleOperatingContextResult.Success(context);
    }

    public async Task<OperatorConsoleOperatingContextResult> ValidateSessionAsync(
        Guid humanSessionId,
        string? proof,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (humanSessionId == Guid.Empty || correlationId == Guid.Empty)
        {
            throw new ArgumentException("Session and correlation identifiers are required.");
        }

        if (!TryHash(proof, out var thumbprint))
        {
            return Denied(OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired, correlationId);
        }

        var now = _timeProvider.GetUtcNow();
        var facts = await _repository.ReadValidationFactsAsync(humanSessionId, cancellationToken);
        var context = facts.Context;
        string? failure = null;

        if (context is null)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired;
        }
        else if (facts.ContextStatus != "ACTIVE")
        {
            failure = OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked;
        }
        else if (!string.Equals(facts.CurrentProofThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid;
        }
        else if (facts.DeviceStatus == "REVOKED" || facts.DeviceStatus is "SUSPENDED" or "LOST" or "RETIRED")
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked;
        }
        else if (facts.DeviceStatus == "EXPIRED" || (facts.DeviceCredentialExpiresAt.HasValue && facts.DeviceCredentialExpiresAt <= now))
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceBindingExpired;
        }
        else if (facts.DeviceStatus != "ACTIVE" || !TrustedLevels.Contains(facts.TrustLevel ?? string.Empty))
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid;
        }
        else if (!facts.HasCanonicalSiteGroupRelationship || facts.ActiveAssignmentCount != 1 ||
            facts.AssignmentSiteId != context.SiteId || facts.AssignmentSiteGroupId != context.SiteGroupId ||
            !facts.HasEffectiveSiteScope)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite;
        }
        else if (facts.SessionStatus != "ACTIVE" || facts.SessionIdleExpiresAt <= now || facts.SessionAbsoluteExpiresAt <= now)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked;
        }
        else if (facts.CurrentAuthorizationEpoch != context.AuthorizationEpoch)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.StaleAuthorizationEpoch;
        }
        else if (facts.CurrentCredentialVersion != context.CredentialVersion)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked;
        }
        else if (facts.ShiftStatus != "ACTIVE" || facts.ShiftRevokedAt.HasValue || !facts.ShiftActiveFrom.HasValue || facts.ShiftActiveFrom > now || facts.ShiftActiveTo <= now)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.ShiftClosedOrExpired;
        }
        else if (facts.ShiftUserId != context.UserId)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.ShiftOutsideUserScope;
        }
        else if (facts.ShiftSiteId != context.SiteId || facts.ShiftSiteGroupId != context.SiteGroupId)
        {
            failure = OperatorConsoleOperatingContextFailureCodes.ShiftIncompatibleWithDevice;
        }

        if (failure is not null)
        {
            await _repository.InvalidateAsync(humanSessionId, failure, now, correlationId, cancellationToken);
            return Denied(failure, correlationId, humanSessionId);
        }

        await _repository.TouchAsync(humanSessionId, now, correlationId, cancellationToken);
        return OperatorConsoleOperatingContextResult.Success(context! with { CorrelationId = correlationId });
    }

    private static string? ClassifyDevice(OperatorConsoleDeviceBindingCandidate? device, DateTimeOffset now)
    {
        if (device is null || device.MatchingProofCount != 1)
        {
            return OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid;
        }
        if (device.DeviceStatus == "EXPIRED" || (device.CredentialExpiresAt.HasValue && device.CredentialExpiresAt <= now))
        {
            return OperatorConsoleOperatingContextFailureCodes.DeviceBindingExpired;
        }
        if (device.DeviceStatus is "REVOKED" or "SUSPENDED" or "LOST" or "RETIRED")
        {
            return OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked;
        }
        if (device.DeviceStatus != "ACTIVE" || !TrustedLevels.Contains(device.TrustLevel))
        {
            return OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid;
        }
        return device.HasCanonicalSiteGroupRelationship &&
            device.ActiveAssignmentCount == 1 &&
            device.AssignmentSiteId == device.SiteId &&
            device.AssignmentSiteGroupId == device.SiteGroupId
            ? null
            : OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite;
    }

    private static string? ClassifyShift(OperatorConsoleShiftResolution shift)
    {
        if (shift.CompatibleActiveShiftCount > 1)
        {
            return OperatorConsoleOperatingContextFailureCodes.ActiveShiftConflict;
        }
        if (shift.CompatibleActiveShiftCount == 1 && shift.OperatorShiftId.HasValue)
        {
            return null;
        }
        if (shift.HasActiveShiftOutsideUserScope)
        {
            return OperatorConsoleOperatingContextFailureCodes.ShiftOutsideUserScope;
        }
        if (shift.HasActiveShiftOutsideDevice)
        {
            return OperatorConsoleOperatingContextFailureCodes.ShiftIncompatibleWithDevice;
        }
        return shift.HasClosedOrExpiredShift
            ? OperatorConsoleOperatingContextFailureCodes.ShiftClosedOrExpired
            : OperatorConsoleOperatingContextFailureCodes.ActiveShiftRequired;
    }

    private bool TryHash(string? proof, out string thumbprint)
    {
        try
        {
            thumbprint = HashDeviceProof(proof ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            thumbprint = string.Empty;
            return false;
        }
    }

    private OperatorConsoleOperatingContextResult Denied(string code, Guid correlationId, Guid? sessionId = null)
    {
        _logger.LogWarning(
            "Operator Console operating context denied with classification {Classification}, session {HumanSessionId}, and correlation {CorrelationId}.",
            code,
            sessionId,
            correlationId);
        return OperatorConsoleOperatingContextResult.Failure(code, correlationId);
    }

    private static void ValidateIds(Guid humanSessionId, Guid userId, Guid correlationId)
    {
        if (humanSessionId == Guid.Empty || userId == Guid.Empty || correlationId == Guid.Empty)
        {
            throw new ArgumentException("Session, user, and correlation identifiers are required.");
        }
    }
}
