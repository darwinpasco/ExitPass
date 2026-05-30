namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Deterministic persistence failure raised while resolving the effective WebPay payable basis.
/// </summary>
public sealed class VendorParkingResolutionPersistenceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VendorParkingResolutionPersistenceException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable error code for API mapping.</param>
    /// <param name="message">Diagnostic message.</param>
    public VendorParkingResolutionPersistenceException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Stable error code for API response mapping.
    /// </summary>
    public string ErrorCode { get; }
}
