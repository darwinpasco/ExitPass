using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Persistence request for a vendor-resolved parking session and tariff snapshot.
/// </summary>
public sealed class PersistVendorParkingResolutionRequest
{
    /// <summary>
    /// Provider-neutral vendor-resolved parking session mapped to Central PMS domain state.
    /// </summary>
    public ParkingSession ParkingSession { get; init; } = null!;

    /// <summary>
    /// Provider-neutral vendor-resolved tariff snapshot mapped to Central PMS domain state.
    /// </summary>
    public TariffSnapshot TariffSnapshot { get; init; } = null!;

    /// <summary>
    /// Correlation identifier for the vendor-to-payment flow.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
