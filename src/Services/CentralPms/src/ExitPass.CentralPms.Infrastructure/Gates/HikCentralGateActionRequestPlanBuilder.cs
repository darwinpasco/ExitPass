using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Side-effect-free builder for guide-approved HikCentral gate action HTTP request plans.
/// </summary>
public sealed class HikCentralGateActionRequestPlanBuilder
{
    /// <summary>
    /// Builds a deterministic request plan without signing or sending an HTTP request.
    /// </summary>
    public HikCentralGateActionRequestPlan Build(
        HikCentralGateActionRequest request,
        HikCentralGateControlProfile profile)
    {
        ValidateRequest(request);
        ValidateProfile(profile);

        var bodyUtf8 = BuildAccessControlDoorControlBody(request.TargetResourceCode.Trim());
        var bodySha256 = Convert.ToHexString(SHA256.HashData(bodyUtf8)).ToLowerInvariant();

        return new HikCentralGateActionRequestPlan(
            HikCentralGateActionConstants.VendorCode,
            HikCentralGateActionConstants.OpenGateOperation,
            profile.ControlMechanism,
            HikCentralGateActionConstants.RequestMethod,
            HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath,
            HikCentralGateActionRequestPlanConstants.JsonContentType,
            bodyUtf8,
            bodySha256,
            request.TargetResourceCode.Trim(),
            request.CorrelationId,
            profile.ProfileCode.Trim());
    }

    private static void ValidateRequest(HikCentralGateActionRequest request)
    {
        if (request is null)
        {
            throw Rejected("HIKCENTRAL_REQUEST_REQUIRED", "HikCentral gate action request is required.");
        }

        if (!string.Equals(
                request.VendorOperation?.Trim(),
                HikCentralGateActionConstants.OpenGateOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("VENDOR_OPERATION_UNSUPPORTED", "Only OPEN_GATE request planning is supported.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetResourceCode))
        {
            throw Rejected("TARGET_RESOURCE_CODE_REQUIRED", "Target resource code is required.");
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw Rejected("CORRELATION_ID_REQUIRED", "Correlation id is required.");
        }
    }

    private static void ValidateProfile(HikCentralGateControlProfile profile)
    {
        if (profile is null)
        {
            throw Rejected("HIKCENTRAL_GATE_CONTROL_PROFILE_REQUIRED", "HikCentral gate control profile is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileCode))
        {
            throw Rejected("HIKCENTRAL_PROFILE_CODE_REQUIRED", "HikCentral gate control profile code is required.");
        }

        if (profile.ControlMechanism != HikCentralGateControlMechanism.AccessControlDoorControl)
        {
            throw Rejected("HIKCENTRAL_CONTROL_MECHANISM_UNSUPPORTED", "HikCentral control mechanism is not supported by this request planner.");
        }

        if (!string.Equals(
                profile.SupportedVendorOperation?.Trim(),
                HikCentralGateActionConstants.OpenGateOperation,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_OPERATION_UNSUPPORTED", "HikCentral gate control profile does not support OPEN_GATE.");
        }

        if (!string.Equals(
                profile.HttpMethod?.Trim(),
                HikCentralGateActionConstants.RequestMethod,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_METHOD_UNSUPPORTED", "HikCentral gate control profile must use POST.");
        }

        ValidateSafeRelativePath(profile.RelativePath);

        if (!string.Equals(
                profile.RelativePath.Trim(),
                HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_PATH_UNAPPROVED", "HikCentral gate control profile path is not approved for access-control door control.");
        }

        if (!string.Equals(
                profile.ContentType?.Trim(),
                HikCentralGateActionRequestPlanConstants.JsonContentType,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_CONTENT_TYPE_UNSUPPORTED", "HikCentral gate control profile must use application/json.");
        }

        if (!string.Equals(profile.TargetFieldName?.Trim(), "doorIndexCode", StringComparison.Ordinal) ||
            !string.Equals(profile.CommandFieldName?.Trim(), "controlType", StringComparison.Ordinal) ||
            !string.Equals(profile.CommandValue?.Trim(), "Open", StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_BODY_MAPPING_UNAPPROVED", "HikCentral gate control profile body mapping is not approved.");
        }
    }

    private static void ValidateSafeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw Rejected("HIKCENTRAL_PROFILE_PATH_REQUIRED", "HikCentral gate control profile path is required.");
        }

        var path = relativePath.Trim();
        if (!path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) ||
            path.Contains('@', StringComparison.Ordinal) ||
            path.Contains('#', StringComparison.Ordinal) ||
            path.Contains('?', StringComparison.Ordinal) ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal) ||
            path.Contains("/./", StringComparison.Ordinal) ||
            path.EndsWith("/..", StringComparison.Ordinal) ||
            path.EndsWith("/.", StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_PROFILE_PATH_UNSAFE", "HikCentral gate control profile path must be a safe relative API path.");
        }
    }

    private static byte[] BuildAccessControlDoorControlBody(string targetResourceCode)
    {
        var json = string.Concat(
            "{\"doorIndexCode\":\"",
            JsonEscape(targetResourceCode),
            "\",\"controlType\":\"Open\"}");
        return Encoding.UTF8.GetBytes(json);
    }

    private static string JsonEscape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append(@"\b");
                    break;
                case '\f':
                    builder.Append(@"\f");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
