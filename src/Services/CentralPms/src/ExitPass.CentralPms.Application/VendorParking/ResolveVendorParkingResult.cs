using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Result of resolving provider-neutral vendor parking data into Central PMS domain objects.
/// </summary>
/// <param name="Outcome">Central PMS resolution outcome.</param>
/// <param name="ParkingSession">Mapped Central PMS parking session when resolution succeeds.</param>
/// <param name="TariffSnapshot">Mapped Central PMS tariff snapshot when resolution succeeds.</param>
/// <param name="ErrorCode">Stable error code when resolution fails.</param>
/// <param name="Retryable">Whether the lookup can be retried.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="VendorSystemId">Provider-neutral vendor system identifier when available.</param>
public sealed record ResolveVendorParkingResult(
    ResolveVendorParkingOutcome Outcome,
    ParkingSession? ParkingSession,
    TariffSnapshot? TariffSnapshot,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId,
    string? VendorSystemId)
{
    /// <summary>
    /// Creates a successful vendor parking resolution result.
    /// </summary>
    /// <param name="parkingSession">Mapped Central PMS parking session.</param>
    /// <param name="tariffSnapshot">Mapped Central PMS tariff snapshot.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="vendorSystemId">Provider-neutral vendor system identifier.</param>
    /// <returns>A successful resolution result.</returns>
    public static ResolveVendorParkingResult Resolved(
        ParkingSession parkingSession,
        TariffSnapshot tariffSnapshot,
        Guid correlationId,
        string vendorSystemId)
    {
        return new ResolveVendorParkingResult(
            ResolveVendorParkingOutcome.Resolved,
            parkingSession,
            tariffSnapshot,
            null,
            false,
            correlationId,
            vendorSystemId);
    }

    /// <summary>
    /// Creates a failed vendor parking resolution result.
    /// </summary>
    /// <param name="outcome">Central PMS resolution outcome.</param>
    /// <param name="errorCode">Stable error code.</param>
    /// <param name="retryable">Whether the lookup can be retried.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="vendorSystemId">Provider-neutral vendor system identifier when available.</param>
    /// <returns>A failed resolution result.</returns>
    public static ResolveVendorParkingResult Failed(
        ResolveVendorParkingOutcome outcome,
        string errorCode,
        bool retryable,
        Guid correlationId,
        string? vendorSystemId = null)
    {
        return new ResolveVendorParkingResult(outcome, null, null, errorCode, retryable, correlationId, vendorSystemId);
    }
}
