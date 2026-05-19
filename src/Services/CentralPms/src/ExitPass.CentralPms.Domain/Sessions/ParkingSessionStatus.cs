namespace ExitPass.CentralPms.Domain.Sessions;

/// <summary>
/// Lifecycle state of the parking session that anchors payment authority.
/// </summary>
public enum ParkingSessionStatus
{
    /// <summary>
    /// Session is open and has not yet required payment.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Session requires payment before exit can be authorized.
    /// </summary>
    PaymentRequired = 2,

    /// <summary>
    /// A payment attempt is in progress for the session.
    /// </summary>
    PaymentInProgress = 3,

    /// <summary>
    /// Session payment has been confirmed by Central PMS.
    /// </summary>
    Paid = 4,

    /// <summary>
    /// Central PMS has issued an exit authorization for the paid session.
    /// </summary>
    ExitAuthorized = 5,

    /// <summary>
    /// Session has exited through the gate control chain.
    /// </summary>
    Exited = 6,

    /// <summary>
    /// Session is closed and no longer eligible for payment attempt creation.
    /// </summary>
    Closed = 7
}
