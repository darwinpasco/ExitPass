namespace ExitPass.CentralPms.Domain.Sessions.Exceptions;

/// <summary>
/// Indicates that the parking session authority anchor for a payment attempt was not found.
/// </summary>
public sealed class ParkingSessionNotFoundException : Exception
{
    /// <summary>
    /// Creates the exception for the missing parking session.
    /// </summary>
    /// <param name="parkingSessionId">Parking session identifier that could not be found.</param>
    public ParkingSessionNotFoundException(Guid parkingSessionId)
        : base($"Parking session '{parkingSessionId}' was not found.")
    {
        ParkingSessionId = parkingSessionId;
    }

    /// <summary>
    /// Missing parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; }
}
