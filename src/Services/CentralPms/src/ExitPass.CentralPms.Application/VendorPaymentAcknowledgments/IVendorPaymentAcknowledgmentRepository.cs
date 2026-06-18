namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Persistence boundary for Vendor PMS payment acknowledgment state.
/// </summary>
public interface IVendorPaymentAcknowledgmentRepository
{
    /// <summary>Loads immutable payment/session data required for Vendor PMS acknowledgment.</summary>
    Task<VendorPaymentAcknowledgmentBasis?> LoadBasisAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken);

    /// <summary>Creates the durable pending acknowledgment record.</summary>
    Task<VendorPaymentAcknowledgmentRecord> CreatePendingAsync(
        CreateVendorPaymentAcknowledgmentCommand command,
        CancellationToken cancellationToken);

    /// <summary>Marks a pending acknowledgment as confirmed by the Vendor PMS.</summary>
    Task<VendorPaymentAcknowledgmentRecord> MarkConfirmedAsync(
        MarkVendorPaymentAcknowledgmentConfirmedCommand command,
        CancellationToken cancellationToken);

    /// <summary>Marks an acknowledgment as failed with safe vendor diagnostics.</summary>
    Task<VendorPaymentAcknowledgmentRecord> MarkFailedAsync(
        MarkVendorPaymentAcknowledgmentFailedCommand command,
        CancellationToken cancellationToken);

    /// <summary>Marks an acknowledgment as intentionally skipped because confirmation is disabled.</summary>
    Task<VendorPaymentAcknowledgmentRecord> MarkSkippedDisabledAsync(
        MarkVendorPaymentAcknowledgmentSkippedDisabledCommand command,
        CancellationToken cancellationToken);

    /// <summary>Reads one durable acknowledgment record by identifier.</summary>
    Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
        Guid vendorPaymentAcknowledgmentId,
        CancellationToken cancellationToken);

    /// <summary>Reads one durable acknowledgment record by payment confirmation and vendor system.</summary>
    Task<VendorPaymentAcknowledgmentRecord?> ReadByPaymentConfirmationAsync(
        Guid paymentConfirmationId,
        string vendorSystemCode,
        CancellationToken cancellationToken);

    /// <summary>Reads the latest durable acknowledgment record for a payment attempt.</summary>
    Task<VendorPaymentAcknowledgmentRecord?> ReadLatestByPaymentAttemptAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}
