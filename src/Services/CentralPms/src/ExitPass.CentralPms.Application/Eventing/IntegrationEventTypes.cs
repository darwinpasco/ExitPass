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
}
