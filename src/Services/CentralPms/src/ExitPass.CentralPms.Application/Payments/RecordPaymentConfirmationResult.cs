namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 6.4 Finalize Payment, 7.3 Provider Callback / Confirmation Handling
/// Invariant: Application layer returns the persisted confirmation that was recorded by the database.
/// </summary>
public sealed record RecordPaymentConfirmationResult(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string ConfirmationStatus,
    DateTimeOffset VerifiedTimestamp);
