namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

public sealed class CreatePaymentAttemptRequest
{
    public Guid ParkingSessionId { get; set; }
    public Guid TariffSnapshotId { get; set; }
    public string PaymentProvider { get; set; } = string.Empty;
}