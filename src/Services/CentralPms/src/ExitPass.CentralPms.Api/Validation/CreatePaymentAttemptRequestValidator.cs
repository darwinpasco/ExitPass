using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

namespace ExitPass.CentralPms.Api.Validation;

public sealed class CreatePaymentAttemptRequestValidator
{
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