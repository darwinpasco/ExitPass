namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Safe HikCentral signing header name and value used for canonicalization only.
/// </summary>
public sealed record HikCentralSigningHeader(string Name, string Value);

/// <summary>
/// Explicit, deterministic input for HikCentral signing-material construction.
/// </summary>
public sealed record HikCentralSigningMaterialInput(
    HikCentralGateActionRequestPlan RequestPlan,
    string ClientKeyIdentifier,
    string TimestampMilliseconds,
    string Nonce,
    string SignatureMethod,
    IReadOnlyList<HikCentralSigningHeader>? AdditionalSignedHeaders = null);

/// <summary>
/// Side-effect-free HikCentral signing material. This is not a signed request and contains no final signature.
/// </summary>
public sealed record HikCentralSigningMaterial(
    string HttpMethod,
    string Accept,
    string ContentMd5,
    string ContentType,
    string TimestampMilliseconds,
    string Nonce,
    string SignatureMethod,
    string SignedHeaderNames,
    IReadOnlyList<HikCentralSigningHeader> PlannedHeaders,
    string ResourcePath,
    string CanonicalString,
    byte[] CanonicalUtf8,
    string CanonicalSha256);

/// <summary>
/// Constants for HikCentral Professional OpenAPI V3.1.0 AK/SK signing material.
/// </summary>
public static class HikCentralRequestSigningMaterialConstants
{
    public const string Accept = "*/*";
    public const string SignatureMethod = "HmacSHA256";
    public const string SignedHeaderNames = "x-ca-key,x-ca-nonce,x-ca-timestamp";
    public const string HeaderAccept = "Accept";
    public const string HeaderContentMd5 = "Content-MD5";
    public const string HeaderContentType = "Content-Type";
    public const string HeaderClientKey = "X-Ca-Key";
    public const string HeaderNonce = "X-Ca-Nonce";
    public const string HeaderTimestamp = "X-Ca-Timestamp";
    public const string HeaderSignatureHeaders = "X-Ca-Signature-Headers";
}
