using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Builds signed HikCentral HTTP request messages without sending them.
/// </summary>
public sealed class HikCentralSignedHttpRequestBuilder : IHikCentralSignedHttpRequestBuilder
{
    private static readonly HashSet<string> ApprovedPlannedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        HikCentralRequestSigningMaterialConstants.HeaderAccept,
        HikCentralRequestSigningMaterialConstants.HeaderContentMd5,
        HikCentralRequestSigningMaterialConstants.HeaderContentType,
        HikCentralRequestSigningMaterialConstants.HeaderClientKey,
        HikCentralRequestSigningMaterialConstants.HeaderNonce,
        HikCentralRequestSigningMaterialConstants.HeaderTimestamp,
        HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders
    };

    /// <inheritdoc />
    public HttpRequestMessage Build(
        Uri baseAddress,
        HikCentralGateActionRequestPlan requestPlan,
        HikCentralSigningMaterial signingMaterial,
        HikCentralRequestSignature signature)
    {
        ValidateBaseAddress(baseAddress);
        ValidateConsistency(requestPlan, signingMaterial, signature);

        var requestUri = BuildRequestUri(baseAddress, requestPlan.RelativePath);
        var request = new HttpRequestMessage(new HttpMethod(requestPlan.HttpMethod), requestUri)
        {
            Content = new ByteArrayContent(requestPlan.BodyUtf8.ToArray())
        };

        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderAccept, signingMaterial.Accept);
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderClientKey, RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderClientKey));
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderNonce, RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderNonce));
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderTimestamp, RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderTimestamp));
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderSignatureMethod, signingMaterial.SignatureMethod);
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders, signingMaterial.SignedHeaderNames);
        request.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderSignature, signature.EncodedSignatureValue);

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(requestPlan.ContentType);
        request.Content.Headers.TryAddWithoutValidation(HikCentralRequestSigningMaterialConstants.HeaderContentMd5, signingMaterial.ContentMd5);

        // The returned message contains sensitive runtime headers and must not be logged, persisted, or audited.
        return request;
    }

    private static Uri BuildRequestUri(Uri baseAddress, string relativePath)
    {
        var originBuilder = new UriBuilder(baseAddress.Scheme, baseAddress.Host, baseAddress.IsDefaultPort ? -1 : baseAddress.Port);
        return new Uri(originBuilder.Uri, relativePath.TrimStart('/'));
    }

    private static void ValidateBaseAddress(Uri baseAddress)
    {
        if (baseAddress is null)
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_REQUIRED", "HikCentral base address is required.");
        }

        if (!baseAddress.IsAbsoluteUri)
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_ABSOLUTE_REQUIRED", "HikCentral base address must be absolute.");
        }

        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED", "HikCentral base address must use HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(baseAddress.Host))
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_HOST_REQUIRED", "HikCentral base address host is required.");
        }

        if (!string.IsNullOrEmpty(baseAddress.UserInfo))
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_CREDENTIALS_UNSUPPORTED", "HikCentral base address must not include credentials.");
        }

        if (!string.IsNullOrEmpty(baseAddress.Query))
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_QUERY_UNSUPPORTED", "HikCentral base address must not include a query string.");
        }

        if (!string.IsNullOrEmpty(baseAddress.Fragment))
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_FRAGMENT_UNSUPPORTED", "HikCentral base address must not include a fragment.");
        }

        if (baseAddress.AbsolutePath is not "" and not "/")
        {
            throw Rejected("HIKCENTRAL_BASE_ADDRESS_PATH_UNSUPPORTED", "HikCentral base address must not include a path prefix.");
        }
    }

    private static void ValidateConsistency(
        HikCentralGateActionRequestPlan requestPlan,
        HikCentralSigningMaterial signingMaterial,
        HikCentralRequestSignature signature)
    {
        ValidateRequestPlan(requestPlan);
        ValidateSigningMaterial(signingMaterial, requestPlan);
        ValidateSignature(signature, signingMaterial);
    }

    private static void ValidateRequestPlan(HikCentralGateActionRequestPlan requestPlan)
    {
        if (requestPlan is null)
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_REQUIRED", "HikCentral request plan is required.");
        }

        if (!string.Equals(requestPlan.VendorCode, HikCentralGateActionConstants.VendorCode, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_VENDOR_UNSUPPORTED", "HikCentral request plan vendor is unsupported.");
        }

        if (!string.Equals(requestPlan.VendorOperation, HikCentralGateActionConstants.OpenGateOperation, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_OPERATION_UNSUPPORTED", "HikCentral request plan operation is unsupported.");
        }

        if (!string.Equals(requestPlan.HttpMethod, HikCentralGateActionConstants.RequestMethod, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_METHOD_UNSUPPORTED", "HikCentral request plan method is unsupported.");
        }

        if (!string.Equals(requestPlan.RelativePath, HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_PATH_UNAPPROVED", "HikCentral request plan path is not approved.");
        }

        ValidateSafeRelativePath(requestPlan.RelativePath);

        if (!string.Equals(requestPlan.ContentType, HikCentralGateActionRequestPlanConstants.JsonContentType, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_CONTENT_TYPE_UNSUPPORTED", "HikCentral request plan content type is unsupported.");
        }

        if (requestPlan.BodyUtf8 is null || requestPlan.BodyUtf8.Length == 0)
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_BODY_REQUIRED", "HikCentral request plan body is required.");
        }

        var expectedBodySha256 = Convert.ToHexString(SHA256.HashData(requestPlan.BodyUtf8)).ToLowerInvariant();
        if (!string.Equals(requestPlan.BodySha256, expectedBodySha256, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_REQUEST_PLAN_BODY_HASH_MISMATCH", "HikCentral request plan body hash is inconsistent.");
        }
    }

    private static void ValidateSigningMaterial(
        HikCentralSigningMaterial signingMaterial,
        HikCentralGateActionRequestPlan requestPlan)
    {
        if (signingMaterial is null)
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_REQUIRED", "HikCentral signing material is required.");
        }

        if (!string.Equals(requestPlan.HttpMethod, signingMaterial.HttpMethod, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_METHOD_MISMATCH", "HikCentral signing material method does not match the request plan.");
        }

        if (!string.Equals(requestPlan.RelativePath, signingMaterial.ResourcePath, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_PATH_MISMATCH", "HikCentral signing material path does not match the request plan.");
        }

        if (!string.Equals(requestPlan.ContentType, signingMaterial.ContentType, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CONTENT_TYPE_MISMATCH", "HikCentral signing material content type does not match the request plan.");
        }

        if (!string.Equals(signingMaterial.Accept, HikCentralRequestSigningMaterialConstants.Accept, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_ACCEPT_UNSUPPORTED", "HikCentral signing material Accept value is unsupported.");
        }

        var expectedContentMd5 = Convert.ToBase64String(MD5.HashData(requestPlan.BodyUtf8));
        if (!string.Equals(signingMaterial.ContentMd5, expectedContentMd5, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CONTENT_MD5_MISMATCH", "HikCentral signing material Content-MD5 is inconsistent.");
        }

        if (!string.Equals(signingMaterial.SignatureMethod, HikCentralRequestSigningMaterialConstants.SignatureMethod, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_SIGNATURE_METHOD_UNSUPPORTED", "HikCentral signing material signature method is unsupported.");
        }

        if (!string.Equals(signingMaterial.SignedHeaderNames, HikCentralRequestSigningMaterialConstants.SignedHeaderNames, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_SIGNED_HEADERS_UNSUPPORTED", "HikCentral signing material signed headers are unsupported.");
        }

        ValidatePlannedHeaders(signingMaterial);

        if (signingMaterial.CanonicalUtf8 is null || signingMaterial.CanonicalUtf8.Length == 0)
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CANONICAL_BYTES_REQUIRED", "HikCentral signing material canonical bytes are required.");
        }

        if (string.IsNullOrWhiteSpace(signingMaterial.CanonicalString))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CANONICAL_STRING_REQUIRED", "HikCentral signing material canonical string is required.");
        }

        if (!string.Equals(signingMaterial.CanonicalString, Encoding.UTF8.GetString(signingMaterial.CanonicalUtf8), StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CANONICAL_BYTES_MISMATCH", "HikCentral signing material canonical bytes are inconsistent.");
        }

        var expectedCanonicalSha256 = Convert.ToHexString(SHA256.HashData(signingMaterial.CanonicalUtf8)).ToLowerInvariant();
        if (!string.Equals(signingMaterial.CanonicalSha256, expectedCanonicalSha256, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_CANONICAL_HASH_MISMATCH", "HikCentral signing material canonical hash is inconsistent.");
        }
    }

    private static void ValidatePlannedHeaders(HikCentralSigningMaterial signingMaterial)
    {
        if (signingMaterial.PlannedHeaders is null)
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADERS_REQUIRED", "HikCentral signing material planned headers are required.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in signingMaterial.PlannedHeaders)
        {
            if (header is null || string.IsNullOrWhiteSpace(header.Name) || string.IsNullOrWhiteSpace(header.Value))
            {
                throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADER_INVALID", "HikCentral signing material header is invalid.");
            }

            if (ContainsLineBreak(header.Name) || ContainsLineBreak(header.Value))
            {
                throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADER_UNSAFE", "HikCentral signing material header is unsafe.");
            }

            if (!ApprovedPlannedHeaders.Contains(header.Name.Trim()))
            {
                throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADER_UNSUPPORTED", "HikCentral signing material header is unsupported.");
            }

            if (!seen.Add(header.Name.Trim()))
            {
                throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADER_DUPLICATE", "HikCentral signing material header is duplicated.");
            }
        }

        _ = RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderClientKey);
        _ = RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderNonce);
        _ = RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderTimestamp);
        _ = RequiredHeaderValue(signingMaterial, HikCentralRequestSigningMaterialConstants.HeaderSignatureHeaders);
    }

    private static void ValidateSignature(
        HikCentralRequestSignature signature,
        HikCentralSigningMaterial signingMaterial)
    {
        if (signature is null)
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_REQUIRED", "HikCentral request signature is required.");
        }

        if (!string.Equals(signature.SignatureAlgorithmIdentifier, signingMaterial.SignatureMethod, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_METHOD_MISMATCH", "HikCentral request signature method does not match signing material.");
        }

        if (!string.Equals(signature.HeaderName, HikCentralRequestSigningMaterialConstants.HeaderSignature, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_HEADER_UNSUPPORTED", "HikCentral request signature header is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(signature.EncodedSignatureValue) || ContainsLineBreak(signature.EncodedSignatureValue))
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_VALUE_INVALID", "HikCentral request signature value is invalid.");
        }
    }

    private static string RequiredHeaderValue(HikCentralSigningMaterial signingMaterial, string headerName)
    {
        var values = signingMaterial.PlannedHeaders
            .Where(header => string.Equals(header.Name, headerName, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value)
            .ToArray();

        return values.Length == 1 && !string.IsNullOrWhiteSpace(values[0])
            ? values[0]
            : throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_HEADER_REQUIRED", "Required HikCentral signing material header is missing.");
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

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
