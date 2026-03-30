namespace ExitPass.CentralPms.Api.Validation;

public sealed class CreatePaymentAttemptHeadersValidator
{
    public IReadOnlyList<string> Validate(string? idempotencyKey, string? correlationId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            errors.Add("Idempotency-Key header is required.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors.Add("X-Correlation-Id header is required.");
        }
        else if (!Guid.TryParse(correlationId, out _))
        {
            errors.Add("X-Correlation-Id must be a valid GUID.");
        }

        return errors;
    }
}