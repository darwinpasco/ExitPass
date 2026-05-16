namespace ExitPass.PaymentOrchestrator.Contracts.Routing;

/// <summary>
/// Supported provider-routing payment method codes.
/// </summary>
public static class PaymentMethodCode
{
    /// <summary>
    /// QR Ph payment method.
    /// </summary>
    public const string QrPh = "QRPH";

    /// <summary>
    /// Card payment method.
    /// </summary>
    public const string Card = "CARD";

    /// <summary>
    /// GCash wallet payment method.
    /// </summary>
    public const string GCash = "GCASH";

    /// <summary>
    /// Maya wallet payment method.
    /// </summary>
    public const string Maya = "MAYA";
}
