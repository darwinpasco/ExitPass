namespace ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;

/// <summary>
/// Indicates that Central PMS rejected a payment attempt because the parking session already has an active attempt.
/// </summary>
public sealed class ActivePaymentAttemptAlreadyExistsException : Exception
{
    /// <summary>
    /// Creates the conflict exception for the parking session that must retain a single active payment attempt.
    /// </summary>
    /// <param name="parkingSessionId">Parking session whose active attempt prevents a competing payment attempt.</param>
    public ActivePaymentAttemptAlreadyExistsException(Guid parkingSessionId)
        : base($"An active payment attempt already exists for parking session '{parkingSessionId}'.")
    {
        ParkingSessionId = parkingSessionId;
    }

    /// <summary>
    /// Parking session that already has an active payment attempt in the v1.2 control chain.
    /// </summary>
    public Guid ParkingSessionId { get; }
}
