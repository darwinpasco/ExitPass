namespace ExitPass.CentralPms.Domain.Sessions.Exceptions;

public sealed class ParkingSessionNotFoundException : Exception
{
    public ParkingSessionNotFoundException(Guid parkingSessionId)
        : base($"Parking session '{parkingSessionId}' was not found.")
    {
        ParkingSessionId = parkingSessionId;
    }

    public Guid ParkingSessionId { get; }
}