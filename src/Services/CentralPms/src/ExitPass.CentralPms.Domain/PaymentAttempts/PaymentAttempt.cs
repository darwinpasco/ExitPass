namespace ExitPass.CentralPms.Domain.PaymentAttempts;

public sealed class PaymentAttempt
{
    public Guid PaymentAttemptId { get; private set; }
    public Guid ParkingSessionId { get; private set; }
    public Guid TariffSnapshotId { get; private set; }
    public PaymentProvider PaymentProvider { get; private set; } = PaymentProvider.GCash;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public PaymentAttemptStatus AttemptStatus { get; private set; }
    public decimal GrossAmountSnapshot { get; private set; }
    public decimal StatutoryDiscountSnapshot { get; private set; }
    public decimal CouponDiscountSnapshot { get; private set; }
    public decimal NetAmountDueSnapshot { get; private set; }
    public decimal? BaseFeeAmountSnapshot { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string? TariffVersionReference { get; private set; }
    public Guid? CouponApplicationId { get; private set; }
    public Guid? StatutoryValidationId { get; private set; }
    public string? ProviderRedirectUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderInitiatedAt { get; private set; }
    public DateTimeOffset? FinalizedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public string UpdatedBy { get; private set; } = string.Empty;
    public long RowVersion { get; private set; }

    public static PaymentAttempt Create(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        PaymentProvider paymentProvider,
        string idempotencyKey,
        decimal grossAmountSnapshot,
        decimal statutoryDiscountSnapshot,
        decimal couponDiscountSnapshot,
        decimal netAmountDueSnapshot,
        decimal? baseFeeAmountSnapshot,
        string currencyCode,
        string? tariffVersionReference,
        DateTimeOffset createdAt,
        string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        return new PaymentAttempt
        {
            PaymentAttemptId = paymentAttemptId,
            ParkingSessionId = parkingSessionId,
            TariffSnapshotId = tariffSnapshotId,
            PaymentProvider = paymentProvider,
            IdempotencyKey = idempotencyKey,
            AttemptStatus = PaymentAttemptStatus.Initiated,
            GrossAmountSnapshot = grossAmountSnapshot,
            StatutoryDiscountSnapshot = statutoryDiscountSnapshot,
            CouponDiscountSnapshot = couponDiscountSnapshot,
            NetAmountDueSnapshot = netAmountDueSnapshot,
            BaseFeeAmountSnapshot = baseFeeAmountSnapshot,
            CurrencyCode = currencyCode,
            TariffVersionReference = tariffVersionReference,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
            RowVersion = 1
        };
    }

    public void MarkPendingProvider(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.PendingProvider);
        AttemptStatus = PaymentAttemptStatus.PendingProvider;
        ProviderInitiatedAt = at;
        UpdatedBy = updatedBy;
    }

    public void MarkConfirmed(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Confirmed);
        AttemptStatus = PaymentAttemptStatus.Confirmed;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    public void MarkFailed(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Failed);
        AttemptStatus = PaymentAttemptStatus.Failed;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    public void MarkExpired(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Expired);
        AttemptStatus = PaymentAttemptStatus.Expired;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    public void Cancel(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Cancelled);
        AttemptStatus = PaymentAttemptStatus.Cancelled;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    public bool IsTerminal()
    {
        return AttemptStatus is PaymentAttemptStatus.Confirmed
            or PaymentAttemptStatus.Failed
            or PaymentAttemptStatus.Expired
            or PaymentAttemptStatus.Cancelled;
    }

    public bool IsActive()
    {
        return AttemptStatus is PaymentAttemptStatus.Initiated or PaymentAttemptStatus.PendingProvider;
    }

    public bool CanTransitionTo(PaymentAttemptStatus nextStatus)
    {
        if (IsTerminal())
        {
            return false;
        }

        return AttemptStatus switch
        {
            PaymentAttemptStatus.Initiated => nextStatus is PaymentAttemptStatus.PendingProvider or PaymentAttemptStatus.Confirmed or PaymentAttemptStatus.Failed or PaymentAttemptStatus.Expired or PaymentAttemptStatus.Cancelled,
            PaymentAttemptStatus.PendingProvider => nextStatus is PaymentAttemptStatus.Confirmed or PaymentAttemptStatus.Failed or PaymentAttemptStatus.Expired or PaymentAttemptStatus.Cancelled,
            _ => false
        };
    }

    private void EnsureTransitionAllowed(PaymentAttemptStatus nextStatus)
    {
        if (!CanTransitionTo(nextStatus))
        {
            throw new InvalidOperationException($"Transition from {AttemptStatus} to {nextStatus} is not allowed.");
        }
    }
}