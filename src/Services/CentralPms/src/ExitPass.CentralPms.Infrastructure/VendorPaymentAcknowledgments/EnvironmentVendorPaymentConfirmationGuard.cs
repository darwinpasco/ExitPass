using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

namespace ExitPass.CentralPms.Infrastructure.VendorPaymentAcknowledgments;

/// <summary>
/// Environment-backed guard for mutating Vendor PMS confirmation calls.
/// </summary>
public sealed class EnvironmentVendorPaymentConfirmationGuard : IVendorPaymentConfirmationGuard
{
    public const string ConfirmPaymentEnabledEnvironmentVariable = "VENDOR_PMS_CONFIRM_PAYMENT_ENABLED";

    /// <inheritdoc />
    public bool IsConfirmPaymentEnabled(string vendorSystemCode)
    {
        return string.Equals(Environment.GetEnvironmentVariable(ConfirmPaymentEnabledEnvironmentVariable),
            "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string DisabledMessage(string vendorSystemCode)
    {
        return $"{ConfirmPaymentEnabledEnvironmentVariable} is false.";
    }
}
