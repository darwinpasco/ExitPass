namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Creates the AUB Card Cashier authorization value for signed provider requests.
/// </summary>
public interface IAubRequestSigner
{
    /// <summary>
    /// Creates the value for the AUB <c>Authorization</c> header.
    /// </summary>
    /// <param name="request">The request metadata and payload to sign.</param>
    /// <returns>The provider authorization header value.</returns>
    string CreateAuthorizationHeader(AubSignedRequest request);
}
