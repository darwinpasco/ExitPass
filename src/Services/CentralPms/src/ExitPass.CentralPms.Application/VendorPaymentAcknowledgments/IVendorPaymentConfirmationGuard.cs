namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Guard for mutating Vendor PMS paid-state confirmation calls.
/// </summary>
public interface IVendorPaymentConfirmationGuard
{
    /// <summary>
    /// Returns whether Central PMS may call the Vendor PMS confirmation operation.
    /// </summary>
    /// <param name="vendorSystemCode">Provider-neutral vendor system code.</param>
    /// <returns><see langword="true"/> when confirmation is enabled.</returns>
    bool IsConfirmPaymentEnabled(string vendorSystemCode);

    /// <summary>
    /// Safe diagnostic message when confirmation is disabled.
    /// </summary>
    /// <param name="vendorSystemCode">Provider-neutral vendor system code.</param>
    /// <returns>Safe diagnostic message.</returns>
    string DisabledMessage(string vendorSystemCode);
}
