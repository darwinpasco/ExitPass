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

    public bool IsEligibleForPaymentAttempt()
    {
        return SessionStatus is ParkingSessionStatus.PaymentRequired or ParkingSessionStatus.PaymentInProgress;
    }
}