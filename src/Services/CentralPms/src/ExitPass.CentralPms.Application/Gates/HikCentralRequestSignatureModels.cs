namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Calculates the final sensitive HikCentral request signature from validated signing material.
/// </summary>
public interface IHikCentralRequestSignatureCalculator
{
    /// <summary>
    /// Calculates the final encoded signature value using explicit in-memory secret bytes.
    /// </summary>
    HikCentralRequestSignature Calculate(
        HikCentralSigningMaterial signingMaterial,
        ReadOnlySpan<byte> appSecretBytes);
}

/// <summary>
/// Sensitive in-memory HikCentral signature value for later HTTP request construction.
/// </summary>
public sealed class HikCentralRequestSignature
{
    public HikCentralRequestSignature(
        string signatureAlgorithmIdentifier,
        string headerName,
        string encodedSignatureValue)
    {
        SignatureAlgorithmIdentifier = signatureAlgorithmIdentifier;
        HeaderName = headerName;
        EncodedSignatureValue = encodedSignatureValue;
    }

    /// <summary>
    /// Guide-confirmed signature algorithm identifier.
    /// </summary>
    public string SignatureAlgorithmIdentifier { get; }

    /// <summary>
    /// Header name that will carry the final signature in a later HTTP request-construction slice.
    /// </summary>
    public string HeaderName { get; }

    /// <summary>
    /// Sensitive final encoded signature value. Do not log, persist, audit, or expose through APIs.
    /// </summary>
    public string EncodedSignatureValue { get; }

    public override string ToString() =>
        $"{nameof(HikCentralRequestSignature)} {{ {nameof(SignatureAlgorithmIdentifier)} = {SignatureAlgorithmIdentifier}, {nameof(HeaderName)} = {HeaderName}, {nameof(EncodedSignatureValue)} = [REDACTED] }}";
}
