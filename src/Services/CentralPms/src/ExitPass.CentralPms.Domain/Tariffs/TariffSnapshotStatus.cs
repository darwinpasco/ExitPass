namespace ExitPass.CentralPms.Domain.Tariffs;

/// <summary>
/// Lifecycle state for a tariff snapshot that may back a payment attempt.
/// </summary>
public enum TariffSnapshotStatus
{
    /// <summary>
    /// Snapshot can be bound to a payment attempt if it is not expired or already consumed.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Snapshot has been replaced by a newer quote.
    /// </summary>
    Superseded = 2,

    /// <summary>
    /// Snapshot is past its payable validity window.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Snapshot has already been bound to a payment attempt.
    /// </summary>
    Consumed = 4,

    /// <summary>
    /// Snapshot was invalidated and cannot be used for payment.
    /// </summary>
    Invalidated = 5
}
