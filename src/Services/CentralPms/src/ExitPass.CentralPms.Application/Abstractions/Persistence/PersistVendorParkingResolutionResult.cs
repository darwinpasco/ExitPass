using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Persistence result for a vendor-resolved parking session and tariff snapshot.
/// </summary>
public sealed class PersistVendorParkingResolutionResult
{
    /// <summary>
    /// Persisted or reused Central PMS parking session.
    /// </summary>
    public ParkingSession ParkingSession { get; init; } = null!;

    /// <summary>
    /// Persisted or reused Central PMS tariff snapshot.
    /// </summary>
    public TariffSnapshot TariffSnapshot { get; init; } = null!;

    /// <summary>
    /// Indicates whether an existing Central PMS parking session was reused.
    /// </summary>
    public bool ParkingSessionWasReused { get; init; }

    /// <summary>
    /// Indicates whether an existing Central PMS tariff snapshot was reused.
    /// </summary>
    public bool TariffSnapshotWasReused { get; init; }
}
