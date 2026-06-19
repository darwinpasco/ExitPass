namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Dispatches bounded retries for due Vendor PMS paid-state acknowledgments.
/// </summary>
public interface IVendorPaymentAcknowledgmentRetryDispatcherService
{
    /// <summary>
    /// Processes one bounded batch of RETRY_PENDING Vendor PMS acknowledgments due for retry.
    /// </summary>
    Task<VendorPaymentAcknowledgmentRetryDispatchResult> DispatchDueAsync(
        DispatchVendorPaymentAcknowledgmentRetriesCommand command,
        CancellationToken cancellationToken);
}
