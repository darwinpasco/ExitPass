namespace ExitPass.CentralPms.Contracts.Common;

public sealed class ErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public bool Retryable { get; set; }
    public Dictionary<string, object?> Details { get; set; } = new();
}