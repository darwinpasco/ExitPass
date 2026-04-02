namespace ExitPass.CentralPms.Api.Validation;

/// <summary>
/// Validates required public API headers for payment-attempt creation requests.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 10.2.4 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - Public payment-attempt creation requires an idempotency key
/// - Public payment-attempt creation requires a valid correlation identifier
/// </summary>
public sealed class CreatePaymentAttemptHeadersValidator
{
    /// <summary>
    /// Validates idempotency and correlation headers for the create-payment-attempt API.
    /// </summary>
    /// <param name="idempotencyKey">Caller-supplied idempotency key header value.</param>
    /// <param name="correlationId">Caller-supplied correlation identifier header value.</param>
    /// <returns>A list of validation errors. Empty means the headers are valid.</returns>
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
