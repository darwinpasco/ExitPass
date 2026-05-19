namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Database routine result for Central PMS payment attempt creation or reuse.
/// </summary>
public sealed class CreateOrReusePaymentAttemptDbResult
{
    /// <summary>
    /// Canonical payment attempt identifier created or reused by the routine.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Parking session bound to the payment attempt.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot bound to the payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Payment attempt lifecycle status returned by Central PMS storage.
    /// </summary>
    public string AttemptStatus { get; init; } = string.Empty;

    /// <summary>
    /// Provider or rail code persisted with the attempt.
    /// </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the routine reused an existing idempotent attempt.
    /// </summary>
    public bool WasReused { get; init; }

    /// <summary>
    /// Routine outcome code describing create, reuse, conflict, or rejection.
    /// </summary>
    public string OutcomeCode { get; init; } = string.Empty;

    /// <summary>
    /// Failure code when the routine rejected the request.
    /// </summary>
    public string? FailureCode { get; init; }

    /// <summary>
    /// Gross amount snapshot persisted for the attempt.
    /// </summary>
    public decimal GrossAmountSnapshot { get; init; }

    /// <summary>
    /// Statutory discount amount persisted for the attempt.
    /// </summary>
    public decimal StatutoryDiscountSnapshot { get; init; }

    /// <summary>
    /// Coupon discount amount persisted for the attempt.
    /// </summary>
    public decimal CouponDiscountSnapshot { get; init; }

    /// <summary>
    /// Net amount due persisted for provider collection.
    /// </summary>
    public decimal NetAmountDueSnapshot { get; init; }

    /// <summary>
    /// Currency code persisted with the payable amount.
    /// </summary>
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>
    /// Tariff version reference persisted for auditability.
    /// </summary>
    public string? TariffVersionReference { get; init; }

    /// <summary>
    /// Idempotency key persisted with the attempt.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
