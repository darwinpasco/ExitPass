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
    /// Optional caller-resolved vendor system identifier from the WebPay context.
    /// </summary>
    public Guid? RequestedVendorSystemId { get; init; }

    /// <summary>Immutable Site Adapter identity that supplied the session and tariff evidence.</summary>
    public Guid? SourceAdapterIdentityId { get; init; }

    /// <summary>
    /// Correlation identifier for the vendor-to-payment flow.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
