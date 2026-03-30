namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public sealed class IdempotentReplayComparison
{
    public Guid RequestedParkingSessionId { get; init; }
    public Guid RequestedTariffSnapshotId { get; init; }
    public string RequestedProviderCode { get; init; } = string.Empty;
    public string PersistedIdempotencyKey { get; init; } = string.Empty;
    public Guid PersistedParkingSessionId { get; init; }
    public Guid PersistedTariffSnapshotId { get; init; }
    public string PersistedProviderCode { get; init; } = string.Empty;
}