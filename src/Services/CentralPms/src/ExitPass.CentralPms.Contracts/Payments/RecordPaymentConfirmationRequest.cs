namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// Request payload for recording verified payment confirmation from an external provider.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Provider outcome evidence must bind to exactly one canonical PaymentAttempt
/// - Recorded confirmation must preserve provider reference and verification evidence
/// </summary>
/// <param name="PaymentAttemptId">Canonical payment attempt identifier being confirmed.</param>
/// <param name="ProviderReference">Provider-side unique reference for the confirmed payment.</param>
/// <param name="ProviderStatus">Provider-reported payment status.</param>
/// <param name="RequestedBy">Internal actor or integration identity recording the confirmation.</param>
/// <param name="RawCallbackReference">Optional raw callback or webhook reference from the provider.</param>
/// <param name="ProviderSignatureValid">Optional flag indicating whether the provider signature was verified.</param>
/// <param name="ProviderPayloadHash">Optional hash of the provider payload for tamper evidence.</param>
/// <param name="AmountConfirmed">Optional amount confirmed by the provider.</param>
/// <param name="CurrencyCode">Optional ISO currency code reported by the provider.</param>
public sealed record RecordPaymentConfirmationRequest(
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string RequestedBy,
    string? RawCallbackReference,
    bool? ProviderSignatureValid,
    string? ProviderPayloadHash,
    decimal? AmountConfirmed,
    string? CurrencyCode);
