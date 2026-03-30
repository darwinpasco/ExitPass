namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public sealed class CreateOrReusePaymentAttemptPolicyInput
{
    public Guid ParkingSessionId { get; init; }
    public Guid TariffSnapshotId { get; init; }
    public PaymentProvider PaymentProvider { get; init; } = PaymentProvider.GCash;
    public string IdempotencyKey { get; init; } = string.Empty;
}