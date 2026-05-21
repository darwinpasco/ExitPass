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

    /// <summary>
    /// Canonical vendor system identifier for the persisted or reused Central PMS parking session.
    /// </summary>
    public string VendorSystemId { get; init; } = string.Empty;

    /// <summary>
    /// Business-friendly site group name resolved from canonical site data.
    /// </summary>
    public string? SiteGroupName { get; init; }

    /// <summary>
    /// Business-friendly site name resolved from canonical site data.
    /// </summary>
    public string? SiteName { get; init; }

    /// <summary>
    /// Current payment status display value derived from authoritative payment attempts.
    /// </summary>
    public string PaymentStatus { get; init; } = "Not Started";
}
