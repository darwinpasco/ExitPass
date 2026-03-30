namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

public sealed class ProviderHandoffDto
{
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}