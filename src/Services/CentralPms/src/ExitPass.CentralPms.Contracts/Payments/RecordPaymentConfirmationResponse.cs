namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Recorded provider confirmation remains traceable and queryable.
/// </summary>
public sealed record RecordPaymentConfirmationResponse(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string ConfirmationStatus,
    DateTimeOffset VerifiedTimestamp);
