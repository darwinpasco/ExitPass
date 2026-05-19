namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Default AUB signer boundary used until vendor-provided JWS/JWE wire-format details are configured.
/// </summary>
public sealed class AubDeferredRequestSigner : IAubRequestSigner
{
    /// <inheritdoc />
    public string CreateAuthorizationHeader(AubSignedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // TODO: Implement AUB Card Cashier JWS/JWE signing after the exact compact serialization
        // and encryption profile is confirmed from vendor deliverables. The local PDFs require
        // RSA-backed JWS/JWE and an Authorization header, but the readable text does not expose
        // enough deterministic wire-format detail to implement cryptography safely.
        throw new InvalidOperationException(
            "AUB Card Cashier authorization signing is not configured. Provide an IAubRequestSigner implementation with the vendor-confirmed JWS/JWE profile.");
    }
}
