using ExitPass.PaymentOrchestrator.Contracts.WebPay;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Result of WebPay payment intent orchestration.
/// </summary>
/// <param name="Succeeded">Indicates whether orchestration completed successfully.</param>
/// <param name="Response">Successful WebPay response.</param>
/// <param name="Error">Deterministic WebPay error.</param>
public sealed record WebPayPaymentIntentResult(
    bool Succeeded,
    WebPayPaymentIntentResponse? Response,
    WebPayPaymentIntentError? Error)
{
    /// <summary>
    /// Creates a successful WebPay payment intent result.
    /// </summary>
    /// <param name="response">Successful response payload.</param>
    /// <returns>A successful result.</returns>
    public static WebPayPaymentIntentResult Success(WebPayPaymentIntentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new WebPayPaymentIntentResult(true, response, null);
    }

    /// <summary>
    /// Creates a failed WebPay payment intent result.
    /// </summary>
    /// <param name="error">Deterministic error payload.</param>
    /// <returns>A failed result.</returns>
    public static WebPayPaymentIntentResult Failure(WebPayPaymentIntentError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new WebPayPaymentIntentResult(false, null, error);
    }
}
