using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

namespace ExitPass.CentralPms.Api.Validation;

/// <summary>
/// Validates the public request body for create-payment-attempt API calls.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 10.2.4 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - Payment attempt creation requires a parking session identifier
/// - Payment attempt creation requires a tariff snapshot identifier
/// - Payment attempt creation requires a declared payment provider
/// </summary>
public sealed class CreatePaymentAttemptRequestValidator
{
    /// <summary>
    /// Validates the create-payment-attempt request body.
    /// </summary>
    /// <param name="request">Request payload submitted by the caller.</param>
    /// <returns>A list of validation errors. Empty means the request is valid.</returns>
    public IReadOnlyList<string> Validate(CreatePaymentAttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        if (request.ParkingSessionId == Guid.Empty)
        {
            errors.Add("ParkingSessionId is required.");
        }

        if (request.TariffSnapshotId == Guid.Empty)
        {
            errors.Add("TariffSnapshotId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentProvider))
        {
            errors.Add("PaymentProvider is required.");
        }

        if (request.InvoiceCustomerInformation is { } customer)
        {
            AddLengthError(customer.CustomerName, 160, "InvoiceCustomerInformation.CustomerName", errors);
            AddLengthError(customer.Address, 300, "InvoiceCustomerInformation.Address", errors);
            AddLengthError(customer.Tin, 40, "InvoiceCustomerInformation.Tin", errors);
            AddLengthError(customer.BusinessStyle, 160, "InvoiceCustomerInformation.BusinessStyle", errors);
            AddLengthError(customer.StatutoryIdNumber, 80, "InvoiceCustomerInformation.StatutoryIdNumber", errors);
        }

        return errors;
    }

    private static void AddLengthError(string? value, int maximum, string field, List<string> errors)
    {
        if (value?.Trim().Length > maximum)
        {
            errors.Add($"{field} must be {maximum} characters or fewer.");
        }
    }
}
