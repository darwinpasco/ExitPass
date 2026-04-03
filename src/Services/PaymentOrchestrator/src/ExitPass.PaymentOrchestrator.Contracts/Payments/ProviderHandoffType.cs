namespace ExitPass.PaymentOrchestrator.Contracts.Payments;

/// <summary>
/// Represents how the caller should continue the payment flow after provider session creation.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider continuation behavior must be explicit and not inferred.
/// </summary>
public enum ProviderHandoffType
{
    /// <summary>
    /// No further handoff is required.
    /// </summary>
    None = 0,

    /// <summary>
    /// The parker must be redirected to a provider-managed URL.
    /// </summary>
    Redirect = 1,

    /// <summary>
    /// The parker must be redirected using an HTTP form POST.
    /// </summary>
    PostFormRedirect = 2,

    /// <summary>
    /// The parker must be shown a QR artifact to continue payment.
    /// </summary>
    QrDisplay = 3
}
