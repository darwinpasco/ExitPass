namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Declares whether an Operator Console endpoint requires trusted-device and shift operating context.
/// Endpoints without this metadata remain protected by the middleware's required-by-default posture.
/// </summary>
public sealed record OperatorConsoleOperatingContextRequirementMetadata(bool Required)
{
    public static readonly OperatorConsoleOperatingContextRequirementMetadata NotRequired = new(false);
}
