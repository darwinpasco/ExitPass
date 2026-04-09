namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents a deterministic payment confirmation conflict.
/// </summary>
public sealed class PaymentConfirmationConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentConfirmationConflictException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable API error code.</param>
    /// <param name="message">Conflict message.</param>
    public PaymentConfirmationConflictException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the stable API error code.
    /// </summary>
    public string ErrorCode { get; }
}
