using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Builds deterministic HikCentral AK/SK signing material without loading secrets or calculating the final signature.
/// </summary>
public sealed class HikCentralRequestSigningMaterialBuilder
{
    private static readonly StringComparer HeaderNameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> BuiltInSignedHeaderNames = new(HeaderNameComparer)
    {
        "x-ca-key",
        "x-ca-nonce",
        "x-ca-timestamp"
    };

    /// <summary>
    /// Builds side-effect-free signing material for a guide-confirmed HikCentral request plan.
    /// </summary>
    public HikCentralSigningMaterial Build(HikCentralSigningMaterialInput input)
    {
        ValidateInput(input);

        var plan = input.RequestPlan;
        var clientKeyIdentifier = input.ClientKeyIdentifier.Trim();
        var timestamp = input.TimestampMilliseconds.Trim();
        var nonce = input.Nonce.Trim();
        var contentMd5 = Convert.ToBase64String(MD5.HashData(plan.BodyUtf8));
        var signedHeaders = BuildSignedHeaders(clientKeyIdentifier, nonce, timestamp, input.AdditionalSignedHeaders);
        var canonicalString = BuildCanonicalString(plan, contentMd5, signedHeaders);
        var canonicalUtf8 = Encoding.UTF8.GetBytes(canonicalString);
        var canonicalSha256 = Convert.ToHexString(SHA256.HashData(canonicalUtf8)).ToLowerInvariant();

        var plannedHeaders = new List<HikCentralSigningHeader>
        {
            new(HikCentralRequestSigningMaterialConstants.HeaderAccept, HikCentralRequestSigningMaterialConstants.Accept),
            new(HikCentralRequestSigningMaterialConstants.HeaderContentMd5, contentMd5),
            new(HikCentralRequestSigningMaterialConstants.HeaderContentType, plan.ContentType),
            new(HikCentralRequestSigningMaterialConstants.HeaderClientKey, clientKeyIdentifier),
            new(HikCentralRequestSigningMaterialConstants.HeaderNonce, nonce),
            new(HikCentralRequestSigningMaterialConstants.HeaderTimestamp, timestamp),
            new(HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders, HikCentralRequestSigningMaterialConstants.SignedHeaderNames)
        };

        return new HikCentralSigningMaterial(
            plan.HttpMethod.ToUpperInvariant(),
            HikCentralRequestSigningMaterialConstants.Accept,
            contentMd5,
            plan.ContentType,
            timestamp,
            nonce,
            HikCentralRequestSigningMaterialConstants.SignatureMethod,
            HikCentralRequestSigningMaterialConstants.SignedHeaderNames,
            plannedHeaders,
            plan.RelativePath,
            canonicalString,
            canonicalUtf8,
            canonicalSha256);
    }

    private static string BuildCanonicalString(
        HikCentralGateActionRequestPlan plan,
        string contentMd5,
        IReadOnlyDictionary<string, string> signedHeaders)
    {
        // HikCentral Professional OpenAPI V3.1.0 section 3.2 canonical AK/SK shape used by the
        // guide-confirmed section 5.9.1 door-control request.
        var builder = new StringBuilder();
        builder.Append(plan.HttpMethod.ToUpperInvariant()).Append('\n');
        builder.Append(HikCentralRequestSigningMaterialConstants.Accept).Append('\n');
        builder.Append(contentMd5).Append('\n');
        builder.Append(plan.ContentType).Append('\n');

        foreach (var header in signedHeaders.OrderBy(header => header.Key, StringComparer.Ordinal))
        {
            builder.Append(header.Key).Append(':').Append(header.Value).Append('\n');
        }

        builder.Append(plan.RelativePath);
        return builder.ToString();
    }

    private static SortedDictionary<string, string> BuildSignedHeaders(
        string clientKeyIdentifier,
        string nonce,
        string timestamp,
        IReadOnlyList<HikCentralSigningHeader>? additionalSignedHeaders)
    {
        var signedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-ca-key"] = clientKeyIdentifier,
            ["x-ca-nonce"] = nonce,
            ["x-ca-timestamp"] = timestamp
        };

        if (additionalSignedHeaders is null)
        {
            return signedHeaders;
        }

        foreach (var header in additionalSignedHeaders)
        {
            ValidateHeader(header);

            var normalizedName = header.Name.Trim().ToLowerInvariant();
            if (!BuiltInSignedHeaderNames.Contains(normalizedName))
            {
                throw Rejected("HIKCENTRAL_SIGNING_HEADER_UNSUPPORTED", "HikCentral signing header is not supported.");
            }

            if (signedHeaders.ContainsKey(normalizedName))
            {
                throw Rejected("HIKCENTRAL_SIGNING_HEADER_DUPLICATE", "HikCentral signing header is duplicated.");
            }

            signedHeaders[normalizedName] = header.Value.Trim();
        }

        return signedHeaders;
    }

    private static void ValidateInput(HikCentralSigningMaterialInput input)
    {
        if (input is null)
        {
            throw Rejected("HIKCENTRAL_SIGNING_INPUT_REQUIRED", "HikCentral signing material input is required.");
        }

        ValidatePlan(input.RequestPlan);
        ValidateRequiredSafeValue(input.ClientKeyIdentifier, "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED", "HikCentral client key identifier is required.");
        ValidateTimestamp(input.TimestampMilliseconds);
        ValidateNonce(input.Nonce);

        if (!string.Equals(
                input.SignatureMethod?.Trim(),
                HikCentralRequestSigningMaterialConstants.SignatureMethod,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_METHOD_UNSUPPORTED", "HikCentral signature method is not supported.");
        }
    }

    private static void ValidatePlan(HikCentralGateActionRequestPlan plan)
    {
        if (plan is null)
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_REQUIRED", "HikCentral request plan is required.");
        }

        if (!string.Equals(plan.VendorCode, HikCentralGateActionConstants.VendorCode, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_VENDOR_UNSUPPORTED", "HikCentral request plan vendor is unsupported.");
        }

        if (!string.Equals(plan.VendorOperation, HikCentralGateActionConstants.OpenGateOperation, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_OPERATION_UNSUPPORTED", "HikCentral request plan operation is unsupported.");
        }

        if (!string.Equals(plan.HttpMethod, HikCentralGateActionConstants.RequestMethod, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_METHOD_UNSUPPORTED", "HikCentral request plan method is unsupported.");
        }

        if (!string.Equals(plan.ContentType, HikCentralGateActionRequestPlanConstants.JsonContentType, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_CONTENT_TYPE_UNSUPPORTED", "HikCentral request plan content type is unsupported.");
        }

        ValidateSafeRelativePath(plan.RelativePath);

        if (!string.Equals(
                plan.RelativePath,
                HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_PATH_UNAPPROVED", "HikCentral request plan path is not approved.");
        }

        if (plan.BodyUtf8 is null || plan.BodyUtf8.Length == 0)
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_BODY_REQUIRED", "HikCentral request plan body is required.");
        }

        var expectedBodySha256 = Convert.ToHexString(SHA256.HashData(plan.BodyUtf8)).ToLowerInvariant();
        if (!string.Equals(plan.BodySha256, expectedBodySha256, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_BODY_HASH_MISMATCH", "HikCentral request plan body hash does not match the request body.");
        }
    }

    private static void ValidateSafeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_PATH_REQUIRED", "HikCentral request plan path is required.");
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
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_PATH_UNSAFE", "HikCentral request plan path must be a safe relative API path.");
        }
    }

    private static void ValidateTimestamp(string? timestamp)
    {
        ValidateRequiredSafeValue(timestamp, "HIKCENTRAL_TIMESTAMP_REQUIRED", "HikCentral timestamp is required.");

        var trimmed = timestamp!.Trim();
        if (trimmed.Length != 13 || !trimmed.All(char.IsDigit) || !long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Rejected("HIKCENTRAL_TIMESTAMP_INVALID", "HikCentral timestamp must be Unix epoch milliseconds.");
        }
    }

    private static void ValidateNonce(string? nonce)
    {
        ValidateRequiredSafeValue(nonce, "HIKCENTRAL_NONCE_REQUIRED", "HikCentral nonce is required.");

        var trimmed = nonce!.Trim();
        if (trimmed.Length > 64 || trimmed.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.'))
        {
            throw Rejected("HIKCENTRAL_NONCE_INVALID", "HikCentral nonce contains unsupported characters.");
        }
    }

    private static void ValidateRequiredSafeValue(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Rejected(errorCode, message);
        }

        if (ContainsLineBreak(value))
        {
            throw Rejected("HIKCENTRAL_SIGNING_VALUE_UNSAFE", "HikCentral signing material contains unsafe characters.");
        }
    }

    private static void ValidateHeader(HikCentralSigningHeader header)
    {
        if (header is null || string.IsNullOrWhiteSpace(header.Name) || string.IsNullOrWhiteSpace(header.Value))
        {
            throw Rejected("HIKCENTRAL_SIGNING_HEADER_INVALID", "HikCentral signing header is invalid.");
        }

        if (ContainsLineBreak(header.Name) || ContainsLineBreak(header.Value))
        {
            throw Rejected("HIKCENTRAL_SIGNING_HEADER_UNSAFE", "HikCentral signing header contains unsafe characters.");
        }

        var headerName = header.Name.Trim();
        if (headerName.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw Rejected("HIKCENTRAL_SIGNING_HEADER_NAME_UNSAFE", "HikCentral signing header name is unsafe.");
        }
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
