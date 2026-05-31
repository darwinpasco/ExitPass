namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Deterministic business conflict raised before gate authorization consumption is persisted.
/// </summary>
public sealed class ExitAuthorizationConsumeConflictException : InvalidOperationException
{
    /// <summary>
    /// Creates a consume conflict with a stable API error code.
    /// </summary>
    public ExitAuthorizationConsumeConflictException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? "GATE_CONSUME_NOT_ALLOWED"
            : errorCode;
    }

    /// <summary>
    /// Stable API error code for the deterministic conflict.
    /// </summary>
    public string ErrorCode { get; }
}
