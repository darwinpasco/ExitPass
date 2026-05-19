namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Represents the AUB request details that participate in provider authorization signing.
/// </summary>
/// <param name="Method">The HTTP method used for the provider request.</param>
/// <param name="RequestPath">The absolute path and query sent to AUB.</param>
/// <param name="Body">The serialized JSON request body.</param>
/// <param name="CustomerRequestId">The AUB customer request identifier header value.</param>
/// <param name="Date">The request date header value.</param>
public sealed record AubSignedRequest(
    string Method,
    string RequestPath,
    string Body,
    string CustomerRequestId,
    DateTimeOffset Date);
