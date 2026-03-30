namespace ExitPass.CentralPms.Application.PaymentAttempts.Commands;

public sealed class CreateOrReusePaymentAttemptCommand
{
    public Guid ParkingSessionId { get; init; }
    public Guid TariffSnapshotId { get; init; }
    public string PaymentProviderCode { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public Guid CorrelationId { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
}