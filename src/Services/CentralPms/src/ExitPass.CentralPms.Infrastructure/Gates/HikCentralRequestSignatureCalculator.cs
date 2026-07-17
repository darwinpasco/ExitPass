using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Calculates sensitive HikCentral AK/SK signatures from already-built signing material.
/// </summary>
public sealed class HikCentralRequestSignatureCalculator : IHikCentralRequestSignatureCalculator
{
    /// <inheritdoc />
    public HikCentralRequestSignature Calculate(
        HikCentralSigningMaterial signingMaterial,
        ReadOnlySpan<byte> appSecretBytes)
    {
        ValidateSigningMaterial(signingMaterial);
        if (appSecretBytes.IsEmpty)
        {
            throw Rejected("HIKCENTRAL_APP_SECRET_REQUIRED", "HikCentral app secret bytes are required.");
        }

        var secretCopy = appSecretBytes.ToArray();
        byte[]? signatureBytes = null;
        try
        {
            using var hmac = new HMACSHA256(secretCopy);
            signatureBytes = hmac.ComputeHash(signingMaterial.CanonicalUtf8);
            var encodedSignature = Convert.ToBase64String(signatureBytes);

            return new HikCentralRequestSignature(
                HikCentralRequestSigningMaterialConstants.SignatureMethod,
                HikCentralRequestSigningMaterialConstants.HeaderSignature,
                encodedSignature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretCopy);
            if (signatureBytes is not null)
            {
                CryptographicOperations.ZeroMemory(signatureBytes);
            }
        }
    }

    private static void ValidateSigningMaterial(HikCentralSigningMaterial signingMaterial)
    {
        if (signingMaterial is null)
        {
            throw Rejected("HIKCENTRAL_SIGNING_MATERIAL_REQUIRED", "HikCentral signing material is required.");
        }

        if (!string.Equals(
                signingMaterial.SignatureMethod,
                HikCentralRequestSigningMaterialConstants.SignatureMethod,
                StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_SIGNATURE_METHOD_UNSUPPORTED", "HikCentral signature method is unsupported.");
        }

        if (signingMaterial.CanonicalUtf8 is null || signingMaterial.CanonicalUtf8.Length == 0)
        {
            throw Rejected("HIKCENTRAL_CANONICAL_BYTES_REQUIRED", "HikCentral canonical bytes are required.");
        }

        if (string.IsNullOrWhiteSpace(signingMaterial.CanonicalString))
        {
            throw Rejected("HIKCENTRAL_CANONICAL_STRING_REQUIRED", "HikCentral canonical string is required.");
        }

        var canonicalStringFromBytes = Encoding.UTF8.GetString(signingMaterial.CanonicalUtf8);
        if (!string.Equals(signingMaterial.CanonicalString, canonicalStringFromBytes, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_CANONICAL_BYTES_MISMATCH", "HikCentral canonical bytes do not match canonical text.");
        }

        var expectedCanonicalSha256 = Convert.ToHexString(SHA256.HashData(signingMaterial.CanonicalUtf8)).ToLowerInvariant();
        if (!string.Equals(signingMaterial.CanonicalSha256, expectedCanonicalSha256, StringComparison.Ordinal))
        {
            throw Rejected("HIKCENTRAL_CANONICAL_HASH_MISMATCH", "HikCentral canonical material hash is inconsistent.");
        }
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
