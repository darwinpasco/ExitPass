namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Provider outcome evidence must bind to exactly one canonical PaymentAttempt.
/// </summary>
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
