namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Processes Vendor PMS paid-state acknowledgments after ExitPass payment finality is persisted.
/// </summary>
public interface IVendorPaymentAcknowledgmentWorkflow
{
    /// <summary>
    /// Creates or reuses a durable Vendor PMS acknowledgment and attempts vendor confirmation when enabled.
    /// </summary>
    /// <param name="command">Post-finality acknowledgment command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes after durable status is recorded.</returns>
    Task ProcessAsync(
        VendorPaymentAcknowledgmentWorkflowCommand command,
        CancellationToken cancellationToken);
}
