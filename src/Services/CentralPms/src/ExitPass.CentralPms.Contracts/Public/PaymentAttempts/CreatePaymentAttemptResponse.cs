namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

public sealed class CreatePaymentAttemptResponse
{
    public Guid PaymentAttemptId { get; set; }
    public string AttemptStatus { get; set; } = string.Empty;
    public string PaymentProvider { get; set; } = string.Empty;
    public bool WasReused { get; set; }
    public ProviderHandoffDto ProviderHandoff { get; set; } = new();
}