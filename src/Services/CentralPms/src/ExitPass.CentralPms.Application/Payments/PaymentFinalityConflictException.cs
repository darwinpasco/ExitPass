namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents a deterministic payment finality conflict.
/// </summary>
public sealed class PaymentFinalityConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentFinalityConflictException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable API error code.</param>
    /// <param name="message">Conflict message.</param>
    public PaymentFinalityConflictException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the stable API error code.
    /// </summary>
    public string ErrorCode { get; }
}
