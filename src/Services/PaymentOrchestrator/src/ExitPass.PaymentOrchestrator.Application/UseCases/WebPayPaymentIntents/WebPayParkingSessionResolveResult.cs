using ExitPass.PaymentOrchestrator.Contracts.WebPay;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Result for WebPay pre-payment parking session resolution.
/// </summary>
public sealed class WebPayParkingSessionResolveResult
{
    private WebPayParkingSessionResolveResult(
        WebPayParkingSessionResolveResponse? response,
        WebPayPaymentIntentError? error)
    {
        Response = response;
        Error = error;
    }

    /// <summary>
    /// Indicates whether the resolve operation succeeded.
    /// </summary>
    public bool Succeeded => Response is not null;

    /// <summary>
    /// Successful pre-payment summary response.
    /// </summary>
    public WebPayParkingSessionResolveResponse? Response { get; }

    /// <summary>
    /// Deterministic resolve error.
    /// </summary>
    public WebPayPaymentIntentError? Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static WebPayParkingSessionResolveResult Success(WebPayParkingSessionResolveResponse response)
    {
        return new WebPayParkingSessionResolveResult(response, null);
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static WebPayParkingSessionResolveResult Failure(WebPayPaymentIntentError error)
    {
        return new WebPayParkingSessionResolveResult(null, error);
    }
}
