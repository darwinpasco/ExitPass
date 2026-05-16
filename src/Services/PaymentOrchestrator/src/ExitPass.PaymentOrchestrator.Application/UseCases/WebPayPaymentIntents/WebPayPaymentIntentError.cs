namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Deterministic WebPay payment intent error.
/// </summary>
/// <param name="StatusCode">HTTP status code for the API response.</param>
/// <param name="ErrorCode">Provider-neutral error code.</param>
/// <param name="Message">Provider-neutral error message.</param>
/// <param name="Retryable">Indicates whether the request can be retried.</param>
public sealed record WebPayPaymentIntentError(
    int StatusCode,
    string ErrorCode,
    string Message,
    bool Retryable);
