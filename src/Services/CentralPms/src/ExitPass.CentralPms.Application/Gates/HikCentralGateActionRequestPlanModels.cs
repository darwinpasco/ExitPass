namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Builds a deterministic HikCentral gate-action request plan without signing or sending.
/// </summary>
public interface IHikCentralGateActionRequestPlanBuilder
{
    /// <summary>
    /// Builds the request plan for one validated gate-action request and explicit control profile.
    /// </summary>
    HikCentralGateActionRequestPlan Build(
        HikCentralGateActionRequest request,
        HikCentralGateControlProfile profile);
}

/// <summary>
/// HikCentral gate-control mechanisms that may be considered by request planning.
/// </summary>
public enum HikCentralGateControlMechanism
{
    /// <summary>
    /// Access-control door operation using the HikCentral door control API.
    /// </summary>
    AccessControlDoorControl,

    /// <summary>
    /// Alarm-output operation. Deferred until exact request body and command values are confirmed.
    /// </summary>
    AlarmOutputControl
}

/// <summary>
/// Secret-free HikCentral gate-control profile used by request planning.
/// </summary>
public sealed record HikCentralGateControlProfile(
    string ProfileCode,
    HikCentralGateControlMechanism ControlMechanism,
    string SupportedVendorOperation,
    string HttpMethod,
    string RelativePath,
    string ContentType,
    string TargetFieldName,
    string CommandFieldName,
    string CommandValue)
{
    /// <summary>
    /// Creates the guide-confirmed access-control door control profile for OPEN_GATE.
    /// HikCentral Professional OpenAPI Developer Guide V3.1.0 section 5.9.1,
    /// POST /artemis/api/acs/v1/door/doControl.
    /// </summary>
    public static HikCentralGateControlProfile AccessControlDoorOpen(string profileCode) =>
        new(
            profileCode,
            HikCentralGateControlMechanism.AccessControlDoorControl,
            HikCentralGateActionConstants.OpenGateOperation,
            HikCentralGateActionConstants.RequestMethod,
            HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath,
            HikCentralGateActionRequestPlanConstants.JsonContentType,
            "doorIndexCode",
            "controlType",
            "Open");
}

/// <summary>
/// Immutable, side-effect-free HikCentral request plan.
/// </summary>
public sealed record HikCentralGateActionRequestPlan(
    string VendorCode,
    string VendorOperation,
    HikCentralGateControlMechanism ControlMechanism,
    string HttpMethod,
    string RelativePath,
    string ContentType,
    byte[] BodyUtf8,
    string BodySha256,
    string TargetResourceCode,
    Guid RequestCorrelationId,
    string ProfileCode);

/// <summary>
/// Stable request-plan constants.
/// </summary>
public static class HikCentralGateActionRequestPlanConstants
{
    /// <summary>
    /// JSON content type for HikCentral POST request bodies.
    /// </summary>
    public const string JsonContentType = "application/json";

    /// <summary>
    /// HikCentral access-control door operation path confirmed by guide section 5.9.1.
    /// </summary>
    public const string AccessControlDoorControlPath = "/artemis/api/acs/v1/door/doControl";
}
