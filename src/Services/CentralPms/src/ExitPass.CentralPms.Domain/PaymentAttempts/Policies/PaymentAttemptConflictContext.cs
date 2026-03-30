using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public sealed class PaymentAttemptConflictContext
{
    public TariffSnapshot Snapshot { get; init; } = default!;
    public PaymentAttempt? ExistingActiveAttempt { get; init; }
    public PaymentAttempt? ExistingIdempotentAttempt { get; init; }
    public CreateOrReusePaymentAttemptPolicyInput Request { get; init; } = default!;
}