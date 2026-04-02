namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Provider callback data must be normalized before persistence.
/// </summary>
public sealed record RecordPaymentConfirmationCommand(
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string RequestedBy,
    string? RawCallbackReference,
    bool? ProviderSignatureValid,
    string? ProviderPayloadHash,
    decimal? AmountConfirmed,
    string? CurrencyCode,
    Guid CorrelationId);
