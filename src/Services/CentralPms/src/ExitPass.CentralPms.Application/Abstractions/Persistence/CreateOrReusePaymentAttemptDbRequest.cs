namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

public sealed class CreateOrReusePaymentAttemptDbRequest
{
    public Guid ParkingSessionId { get; init; }
    public Guid TariffSnapshotId { get; init; }
    public string PaymentProviderCode { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public Guid CorrelationId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}