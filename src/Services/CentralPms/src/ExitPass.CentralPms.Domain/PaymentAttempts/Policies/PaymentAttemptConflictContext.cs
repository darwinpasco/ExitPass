using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

/// <summary>
/// Inputs used to preserve one active payment attempt per parking session.
/// </summary>
public sealed class PaymentAttemptConflictContext
{
    /// <summary>
    /// Tariff snapshot that the requested payment attempt would consume.
    /// </summary>
    public TariffSnapshot Snapshot { get; init; } = default!;

    /// <summary>
    /// Existing active attempt that blocks another attempt for the parking session.
    /// </summary>
    public PaymentAttempt? ExistingActiveAttempt { get; init; }

    /// <summary>
    /// Existing attempt found for the same idempotency key.
    /// </summary>
    public PaymentAttempt? ExistingIdempotentAttempt { get; init; }

    /// <summary>
    /// Requested payment attempt shape being evaluated.
    /// </summary>
    public CreateOrReusePaymentAttemptPolicyInput Request { get; init; } = default!;
}
