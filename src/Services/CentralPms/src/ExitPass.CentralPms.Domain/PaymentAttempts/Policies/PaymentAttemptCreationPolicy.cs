using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public sealed class PaymentAttemptCreationPolicy : IPaymentAttemptCreationPolicy
{
    public void ValidateRequest(CreateOrReusePaymentAttemptPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.IdempotencyKey);
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    /// - 10.7.4 One Active Payment Attempt Per Session
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    /// - 8.3 PaymentAttempt State Machine
    ///
    /// Invariants Enforced:
    /// - tariff snapshot must be eligible before attempt creation
    /// - competing active payment attempt must not be silently bypassed
    /// - only a semantically identical request may reuse an idempotent result
    /// </summary>
    public void ValidateSnapshotEligibility(TariffSnapshot snapshot, ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.EnsureEligibleForPayment(clock);
    }

    public PaymentAttemptConflictOutcome DetermineOutcome(PaymentAttemptConflictContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExistingIdempotentAttempt is not null)
        {
            var comparison = new IdempotentReplayComparison
            {
                RequestedParkingSessionId = context.Request.ParkingSessionId,
                RequestedTariffSnapshotId = context.Request.TariffSnapshotId,
                RequestedProviderCode = context.Request.PaymentProvider.Code,
                PersistedIdempotencyKey = context.ExistingIdempotentAttempt.IdempotencyKey,
                PersistedParkingSessionId = context.ExistingIdempotentAttempt.ParkingSessionId,
                PersistedTariffSnapshotId = context.ExistingIdempotentAttempt.TariffSnapshotId,
                PersistedProviderCode = context.ExistingIdempotentAttempt.PaymentProvider.Code
            };

            return IsValidIdempotentReplay(comparison)
                ? PaymentAttemptConflictOutcome.ReuseExisting
                : PaymentAttemptConflictOutcome.RejectIdempotencyConflict;
        }

        if (context.ExistingActiveAttempt is not null)
        {
            return PaymentAttemptConflictOutcome.RejectActiveAttemptExists;
        }

        if (!context.Snapshot.IsActive())
        {
            return PaymentAttemptConflictOutcome.RejectSnapshotInvalid;
        }

        return PaymentAttemptConflictOutcome.CreateNew;
    }

    public bool IsValidIdempotentReplay(IdempotentReplayComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        return comparison.RequestedParkingSessionId == comparison.PersistedParkingSessionId
            && comparison.RequestedTariffSnapshotId == comparison.PersistedTariffSnapshotId
            && string.Equals(comparison.RequestedProviderCode, comparison.PersistedProviderCode, StringComparison.OrdinalIgnoreCase);
    }
}