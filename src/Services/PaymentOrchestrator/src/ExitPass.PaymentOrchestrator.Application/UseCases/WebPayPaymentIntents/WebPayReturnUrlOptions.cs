namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// WebPay public return URL settings used for provider hosted checkout redirects.
/// </summary>
public sealed class WebPayReturnUrlOptions
{
    /// <summary>
    /// Gets or initializes the public WebPay base URL, for example an ngrok or deployed HTTPS origin.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or initializes the success return path.
    /// </summary>
    public string PaymentSuccessPath { get; set; } = "/webpay/payment-return";

    /// <summary>
    /// Gets or initializes the cancel return path.
    /// </summary>
    public string PaymentCancelPath { get; set; } = "/webpay/payment-cancelled";
}
