namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents a deterministic exit-authorization issuance conflict.
/// </summary>
public sealed class ExitAuthorizationIssuanceConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExitAuthorizationIssuanceConflictException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable API error code.</param>
    /// <param name="message">Conflict message.</param>
    public ExitAuthorizationIssuanceConflictException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the stable API error code.
    /// </summary>
    public string ErrorCode { get; }
}
