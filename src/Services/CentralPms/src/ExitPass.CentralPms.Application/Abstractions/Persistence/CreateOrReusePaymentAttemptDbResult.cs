namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

public sealed class CreateOrReusePaymentAttemptDbResult
{
    public Guid PaymentAttemptId { get; init; }
    public Guid ParkingSessionId { get; init; }
    public Guid TariffSnapshotId { get; init; }
    public string AttemptStatus { get; init; } = string.Empty;
    public string PaymentProviderCode { get; init; } = string.Empty;
    public bool WasReused { get; init; }
    public string OutcomeCode { get; init; } = string.Empty;
    public string? FailureCode { get; init; }
    public decimal GrossAmountSnapshot { get; init; }
    public decimal StatutoryDiscountSnapshot { get; init; }
    public decimal CouponDiscountSnapshot { get; init; }
    public decimal NetAmountDueSnapshot { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string? TariffVersionReference { get; init; }
    public string? IdempotencyKey { get; init; }
}