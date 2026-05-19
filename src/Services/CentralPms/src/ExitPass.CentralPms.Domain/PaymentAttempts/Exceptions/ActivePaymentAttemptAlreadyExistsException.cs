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
    /// <param name="paymentAttemptId">Active payment attempt that owns the reservation.</param>
    public ActivePaymentAttemptAlreadyExistsException(Guid parkingSessionId, Guid paymentAttemptId)
        : base($"An active payment attempt already exists for parking session '{parkingSessionId}'.")
    {
        ParkingSessionId = parkingSessionId;
        PaymentAttemptId = paymentAttemptId;
    }

    /// <summary>
    /// Parking session that already has an active payment attempt in the v1.2 control chain.
    /// </summary>
    public Guid ParkingSessionId { get; }

    /// <summary>
    /// Active payment attempt that currently owns the v1.2 one-active-attempt reservation.
    /// </summary>
    public Guid PaymentAttemptId { get; }
}
