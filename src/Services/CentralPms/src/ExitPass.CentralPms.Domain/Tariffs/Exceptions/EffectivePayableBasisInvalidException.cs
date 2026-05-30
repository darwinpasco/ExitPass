namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that an APPLIED statutory discount payable basis exists but cannot be used safely for payment.
/// </summary>
public sealed class EffectivePayableBasisInvalidException : Exception
{
    /// <summary>
    /// Creates the exception for an invalid effective payable-basis state.
    /// </summary>
    public EffectivePayableBasisInvalidException(
        Guid parkingSessionId,
        Guid? statutoryDiscountApplicationId,
        string? reasonCode)
        : base($"Effective payable basis for parking session '{parkingSessionId}' is invalid.")
    {
        ParkingSessionId = parkingSessionId;
        StatutoryDiscountApplicationId = statutoryDiscountApplicationId;
        ReasonCode = reasonCode;
    }

    /// <summary>
    /// Parking session whose effective payable basis is invalid.
    /// </summary>
    public Guid ParkingSessionId { get; }

    /// <summary>
    /// APPLIED statutory discount application involved in the invalid state, when known.
    /// </summary>
    public Guid? StatutoryDiscountApplicationId { get; }

    /// <summary>
    /// Deterministic invalid-state reason.
    /// </summary>
    public string? ReasonCode { get; }
}
