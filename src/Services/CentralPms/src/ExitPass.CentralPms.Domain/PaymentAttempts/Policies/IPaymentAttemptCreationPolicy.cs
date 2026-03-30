using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public interface IPaymentAttemptCreationPolicy
{
    void ValidateRequest(CreateOrReusePaymentAttemptPolicyInput input);
    void ValidateSnapshotEligibility(TariffSnapshot snapshot, ISystemClock clock);
    PaymentAttemptConflictOutcome DetermineOutcome(PaymentAttemptConflictContext context);
    bool IsValidIdempotentReplay(IdempotentReplayComparison comparison);
}