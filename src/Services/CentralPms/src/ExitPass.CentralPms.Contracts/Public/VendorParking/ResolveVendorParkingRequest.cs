namespace ExitPass.CentralPms.Contracts.Public.VendorParking;

/// <summary>
/// Public API request for resolving provider-neutral vendor parking session and tariff data.
/// </summary>
public sealed class ResolveVendorParkingRequest
{
    /// <summary>
    /// Site group that owns the Central PMS parking session.
    /// </summary>
    public string SiteGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Site that owns the Central PMS parking session.
    /// </summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>
    /// Provider-neutral vendor system identifier requested by the caller.
    /// </summary>
    public string VendorSystemId { get; set; } = string.Empty;

    /// <summary>
    /// Vehicle plate number used for lookup.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Ticket reference used for lookup when plate number is unavailable.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
