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

        return errors;
    }
}
