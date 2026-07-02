namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Stable Central PMS integration event type names.
/// </summary>
public static class IntegrationEventTypes
{
    /// <summary>
    /// Emitted when Central PMS resolves and persists a vendor parking session and tariff snapshot.
    /// </summary>
    public const string VendorParkingResolved = "VendorParkingResolved";

    /// <summary>
    /// Emitted when Central PMS creates a new payment attempt.
    /// </summary>
    public const string PaymentAttemptCreated = "PaymentAttemptCreated";

    /// <summary>
    /// Emitted when Central PMS reuses an existing payment attempt.
    /// </summary>
    public const string PaymentAttemptReused = "PaymentAttemptReused";

    /// <summary>
    /// Emitted when Central PMS records canonical payment confirmation evidence.
    /// </summary>
    public const string PaymentConfirmationRecorded = "PaymentConfirmationRecorded";

    /// <summary>
    /// Emitted when Central PMS confirms a payment attempt.
    /// </summary>
    public const string PaymentAttemptConfirmed = "PaymentAttemptConfirmed";

    /// <summary>
    /// Emitted when Central PMS accepts a verified provider finality report.
    /// </summary>
    public const string PaymentFinalityReportedToCentralPms = "PaymentFinalityReportedToCentralPms";

    /// <summary>
    /// Emitted when Central PMS issues an exit authorization.
    /// </summary>
    public const string ExitAuthorizationIssued = "ExitAuthorizationIssued";

    /// <summary>
    /// Emitted as non-enforcing diagnostic evidence when Central PMS evaluates fiscal gating shadow posture during ExitAuthorization issuance.
    /// </summary>
    public const string ExitAuthorizationFiscalGatingShadowObserved = "ExitAuthorizationFiscalGatingShadowObserved";

    /// <summary>
    /// Emitted when Central PMS consumes an exit authorization.
    /// </summary>
    public const string GateAuthorizationConsumed = "GateAuthorizationConsumed";

    /// <summary>
    /// Emitted when Central PMS rejects a duplicate consume for an already-consumed exit authorization.
    /// </summary>
    public const string DuplicateGateConsumeRejected = "DuplicateGateConsumeRejected";
}
