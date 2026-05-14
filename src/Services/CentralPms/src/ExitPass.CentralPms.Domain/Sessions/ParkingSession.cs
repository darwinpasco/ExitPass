namespace ExitPass.CentralPms.Domain.Sessions;

/// <summary>
/// Canonical parking session used as the authority anchor for payment and exit authorization.
/// </summary>
public sealed class ParkingSession
{
    /// <summary>
    /// Canonical session identifier used across payment attempts and exit authorization.
    /// </summary>
    public Guid ParkingSessionId { get; private set; }

    /// <summary>
    /// Site group that owns the parking session.
    /// </summary>
    public string SiteGroupId { get; private set; } = string.Empty;

    /// <summary>
    /// Site at which the session was created.
    /// </summary>
    public string SiteId { get; private set; } = string.Empty;

    /// <summary>
    /// Upstream PMS system that supplied the session reference.
    /// </summary>
    public string VendorSystemCode { get; private set; } = string.Empty;

    /// <summary>
    /// Vendor-side session reference retained for traceability.
    /// </summary>
    public string VendorSessionRef { get; private set; } = string.Empty;

    /// <summary>
    /// Identifier type used to associate the vehicle or ticket with the session.
    /// </summary>
    public string IdentifierType { get; private set; } = string.Empty;

    /// <summary>
    /// Plate number when the session is plate-based.
    /// </summary>
    public string? PlateNumber { get; private set; }

    /// <summary>
    /// Ticket number when the session is ticket-based.
    /// </summary>
    public string? TicketNumber { get; private set; }

    /// <summary>
    /// Entry timestamp used to support tariff calculation and traceability.
    /// </summary>
    public DateTimeOffset EntryTimestamp { get; private set; }

    /// <summary>
    /// Current session state used to determine payment attempt eligibility.
    /// </summary>
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

    /// <summary>
    /// Returns whether the session state permits payment attempt creation or reuse.
    /// </summary>
    public bool IsEligibleForPaymentAttempt()
    {
        return SessionStatus is ParkingSessionStatus.PaymentRequired or ParkingSessionStatus.PaymentInProgress;
    }
}
