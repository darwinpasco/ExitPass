using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

namespace ExitPass.CentralPms.Infrastructure.VendorPaymentAcknowledgments;

/// <summary>
/// Environment-backed guard for mutating Vendor PMS confirmation calls.
/// </summary>
public sealed class EnvironmentVendorPaymentConfirmationGuard : IVendorPaymentConfirmationGuard
{
    /// <inheritdoc />
    public bool IsConfirmPaymentEnabled(string vendorSystemCode)
    {
        return HikCentralOptions.ReadConfirmPaymentEnabledFromEnvironment();
    }

    /// <inheritdoc />
    public string DisabledMessage(string vendorSystemCode)
    {
        return $"{HikCentralOptions.ConfirmPaymentEnabledEnvironmentVariable} is false.";
    }
}
