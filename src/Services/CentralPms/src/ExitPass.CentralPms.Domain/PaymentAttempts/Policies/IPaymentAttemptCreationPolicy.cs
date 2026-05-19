using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

/// <summary>
/// Enforces Central PMS payment attempt creation and idempotent replay rules.
/// </summary>
public interface IPaymentAttemptCreationPolicy
{
    /// <summary>
    /// Validates the minimum request fields before a payment attempt can be created or reused.
    /// </summary>
    void ValidateRequest(CreateOrReusePaymentAttemptPolicyInput input);

    /// <summary>
    /// Validates that the tariff snapshot may still be bound to a payment attempt.
    /// </summary>
    void ValidateSnapshotEligibility(TariffSnapshot snapshot, ISystemClock clock);

    /// <summary>
    /// Determines whether Central PMS should create, reuse, or reject the requested attempt.
    /// </summary>
    PaymentAttemptConflictOutcome DetermineOutcome(PaymentAttemptConflictContext context);

    /// <summary>
    /// Returns whether an idempotent replay matches the originally persisted payment attempt request.
    /// </summary>
    bool IsValidIdempotentReplay(IdempotentReplayComparison comparison);
}
