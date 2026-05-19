namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Application command for resolving a vendor parking session and tariff quote into Central PMS domain objects.
/// </summary>
public sealed class ResolveVendorParkingCommand
{
    /// <summary>
    /// Site group that owns the resolved Central PMS parking session.
    /// </summary>
    public string SiteGroupId { get; init; } = string.Empty;

    /// <summary>
    /// Site that owns the resolved Central PMS parking session.
    /// </summary>
    public string SiteId { get; init; } = string.Empty;

    /// <summary>
    /// Vehicle plate number used for vendor lookup.
    /// </summary>
    public string? PlateNumber { get; init; }

    /// <summary>
    /// Ticket reference used for vendor lookup when plate number is unavailable.
    /// </summary>
    public string? TicketReference { get; init; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
