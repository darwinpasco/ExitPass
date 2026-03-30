namespace ExitPass.CentralPms.Application.PaymentAttempts.Results;

public sealed class CreateOrReusePaymentAttemptResult
{
    public Guid PaymentAttemptId { get; init; }
    public Guid ParkingSessionId { get; init; }
    public Guid TariffSnapshotId { get; init; }
    public string AttemptStatus { get; init; } = string.Empty;
    public string PaymentProviderCode { get; init; } = string.Empty;
    public ProviderHandoffResult ProviderHandoff { get; init; } = new();
    public bool WasReused { get; init; }
}