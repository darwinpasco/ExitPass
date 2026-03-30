namespace ExitPass.CentralPms.Application.PaymentAttempts.Results;

public sealed class ProviderHandoffResult
{
    public string Type { get; init; } = string.Empty;
    public string? Url { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}