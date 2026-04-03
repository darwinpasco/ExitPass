namespace ExitPass.PaymentOrchestrator.Contracts.Providers;

/// <summary>
/// Defines supported provider product codes for the current Payment Orchestrator slice.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider behavior must be selected by explicit product code.
/// </summary>
public static class ProviderProductCode
{
    /// <summary>
    /// PayMongo Checkout Session product code.
    /// </summary>
    public const string PayMongoCheckoutSession = "PAYMONGO_CHECKOUT_SESSION";

    /// <summary>
    /// PayMongo QR Ph product code.
    /// </summary>
    public const string PayMongoQrPh = "PAYMONGO_QRPH";

    /// <summary>
    /// AUB Card Cashier product code.
    /// </summary>
    public const string AubCardCashier = "AUB_CARD_CASHIER";
}
