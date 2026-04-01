namespace ExitPass.CentralPms.Domain.Sessions;

public sealed class ParkingSession
{
    public Guid ParkingSessionId { get; private set; }
    public string SiteGroupId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string VendorSystemCode { get; private set; } = string.Empty;
    public string VendorSessionRef { get; private set; } = string.Empty;
    public string IdentifierType { get; private set; } = string.Empty;
    public string? PlateNumber { get; private set; }
    public string? TicketNumber { get; private set; }
    public DateTimeOffset EntryTimestamp { get; private set; }
    public ParkingSessionStatus SessionStatus { get; private set; }

    private ParkingSession()
    {
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    ///
    /// Invariants Enforced:
    /// - the canonical control-layer ParkingSession must be rehydrated from durable storage
    /// - the payment initiation path must evaluate session eligibility from canonical session state
    /// </summary>
    public static ParkingSession Rehydrate(
        Guid parkingSessionId,
        string siteGroupId,
        string siteId,
        string vendorSystemCode,
        string vendorSessionRef,
        string identifierType,
        string? plateNumber,
        string? ticketNumber,
        DateTimeOffset entryTimestamp,
        ParkingSessionStatus sessionStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteGroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorSystemCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorSessionRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifierType);

        return new ParkingSession
        {
            ParkingSessionId = parkingSessionId,
            SiteGroupId = siteGroupId,
            SiteId = siteId,
            VendorSystemCode = vendorSystemCode,
            VendorSessionRef = vendorSessionRef,
            IdentifierType = identifierType,
            PlateNumber = plateNumber,
            TicketNumber = ticketNumber,
            EntryTimestamp = entryTimestamp,
            SessionStatus = sessionStatus
        };
    }

    public bool IsEligibleForPaymentAttempt()
    {
        return SessionStatus is ParkingSessionStatus.PaymentRequired or ParkingSessionStatus.PaymentInProgress;
    }
}
