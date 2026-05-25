namespace ExitPass.CentralPms.Application.Security;

/// <summary>
/// Validates gate-device service identity before Central PMS executes a physical-control action.
///
/// BRD v1.2 Reference:
/// - Section 9.12 Exit Authorization
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 6.6 Consume Exit Authorization
/// - Section 10.6 Internal Service APIs
///
/// ExitPass v1.2 Invariant:
/// - Gate/device calls must prove active device identity and assignment before they can consume an ExitAuthorization.
/// </summary>
public interface IGateDeviceIdentityValidator
{
    /// <summary>
    /// Validates that the service identity and gate device are active, linked, and assigned to the authorization site.
    /// </summary>
    /// <param name="request">Validation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<GateDeviceIdentityValidationResult> ValidateConsumeAsync(
        GateDeviceIdentityValidationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gate-device identity validation request.
/// </summary>
public sealed record GateDeviceIdentityValidationRequest(
    Guid ExitAuthorizationId,
    string GateDeviceIdentifier,
    Guid ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Gate-device identity validation result.
/// </summary>
public sealed record GateDeviceIdentityValidationResult(
    bool IsAuthorized,
    string ResultCode,
    string Message,
    Guid? GateDeviceId = null,
    Guid? SiteId = null,
    Guid? LaneId = null)
{
    /// <summary>
    /// Creates an authorized validation result.
    /// </summary>
    public static GateDeviceIdentityValidationResult Authorized(
        Guid gateDeviceId,
        Guid siteId,
        Guid? laneId)
    {
        return new GateDeviceIdentityValidationResult(
            true,
            "GATE_DEVICE_AUTHORIZED",
            "Gate device identity is authorized for this consume request.",
            gateDeviceId,
            siteId,
            laneId);
    }

    /// <summary>
    /// Creates a rejected validation result.
    /// </summary>
    public static GateDeviceIdentityValidationResult Rejected(
        string resultCode,
        string message)
    {
        return new GateDeviceIdentityValidationResult(false, resultCode, message);
    }
}
