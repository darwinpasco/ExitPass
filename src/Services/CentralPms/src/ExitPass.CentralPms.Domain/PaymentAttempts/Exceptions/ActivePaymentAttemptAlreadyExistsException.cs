namespace ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;

public sealed class ActivePaymentAttemptAlreadyExistsException : Exception
{
    public ActivePaymentAttemptAlreadyExistsException(Guid parkingSessionId)
        : base($"An active payment attempt already exists for parking session '{parkingSessionId}'.")
    {
        ParkingSessionId = parkingSessionId;
    }

    public Guid ParkingSessionId { get; }
}