using System.Net;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Represents a sanitized PayMongo provider API failure.
/// </summary>
public sealed class PayMongoProviderApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoProviderApiException"/> class.
    /// </summary>
    public PayMongoProviderApiException(HttpStatusCode statusCode, string reasonCode)
        : base($"PayMongo provider request failed with status {(int)statusCode} ({reasonCode}).")
    {
        StatusCode = statusCode;
        ReasonCode = reasonCode;
    }

    /// <summary>
    /// Gets the provider HTTP status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the sanitized provider error reason code.
    /// </summary>
    public string ReasonCode { get; }
}
