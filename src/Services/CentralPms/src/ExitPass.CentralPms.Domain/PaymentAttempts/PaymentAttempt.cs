namespace ExitPass.CentralPms.Domain.PaymentAttempts;

/// <summary>
/// Central PMS payment attempt bound to one parking session and one valid tariff snapshot.
/// </summary>
public sealed class PaymentAttempt
{
    /// <summary>
    /// Canonical identifier used to correlate provider outcomes, finalization, and exit authorization issuance.
    /// </summary>
    public Guid PaymentAttemptId { get; private set; }

    /// <summary>
    /// Parking session authority anchor for this payment attempt.
    /// </summary>
    public Guid ParkingSessionId { get; private set; }

    /// <summary>
    /// Tariff snapshot that fixes the payable amount for this attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; private set; }

    /// <summary>
    /// Provider or payment rail selected for collecting the amount due.
    /// </summary>
    public PaymentProvider PaymentProvider { get; private set; } = PaymentProvider.GCash;

    /// <summary>
    /// Client supplied key that allows deterministic replay only for the same semantic request.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// Current Central PMS status; provider verification drives terminal payment finality.
    /// </summary>
    public PaymentAttemptStatus AttemptStatus { get; private set; }

    /// <summary>
    /// Gross fee captured from the tariff snapshot at attempt creation.
    /// </summary>
    public decimal GrossAmountSnapshot { get; private set; }

    /// <summary>
    /// Statutory discount amount captured from the tariff snapshot.
    /// </summary>
    public decimal StatutoryDiscountSnapshot { get; private set; }

    /// <summary>
    /// Coupon discount amount captured from the tariff snapshot.
    /// </summary>
    public decimal CouponDiscountSnapshot { get; private set; }

    /// <summary>
    /// Net amount due captured from the tariff snapshot and submitted for payment.
    /// </summary>
    public decimal NetAmountDueSnapshot { get; private set; }

    /// <summary>
    /// Base fee amount captured when the tariff snapshot provides one.
    /// </summary>
    public decimal? BaseFeeAmountSnapshot { get; private set; }

    /// <summary>
    /// Currency code captured with the payable amount.
    /// </summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>
    /// Tariff version reference used to trace how the payment amount was quoted.
    /// </summary>
    public string? TariffVersionReference { get; private set; }

    /// <summary>
    /// Coupon application identifier included when the tariff snapshot applied coupon value.
    /// </summary>
    public Guid? CouponApplicationId { get; private set; }

    /// <summary>
    /// Statutory validation identifier included when the tariff snapshot applied statutory value.
    /// </summary>
    public Guid? StatutoryValidationId { get; private set; }

    /// <summary>
    /// Provider handoff URL when the selected rail requires a redirect.
    /// </summary>
    public string? ProviderRedirectUrl { get; private set; }

    /// <summary>
    /// Time the payment attempt was created by Central PMS.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Time the payment attempt was handed to the provider.
    /// </summary>
    public DateTimeOffset? ProviderInitiatedAt { get; private set; }

    /// <summary>
    /// Time the payment attempt reached a terminal status.
    /// </summary>
    public DateTimeOffset? FinalizedAt { get; private set; }

    /// <summary>
    /// Actor that created the attempt.
    /// </summary>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Actor that last changed the attempt status.
    /// </summary>
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Persistence row version used for concurrency control.
    /// </summary>
    public long RowVersion { get; private set; }

    /// <summary>
    /// Creates a new initiated payment attempt from a valid parking session and tariff snapshot.
    /// </summary>
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

    /// <summary>
    /// Marks the attempt as handed to the provider while Central PMS waits for a verified outcome.
    /// </summary>
    public void MarkPendingProvider(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.PendingProvider);
        AttemptStatus = PaymentAttemptStatus.PendingProvider;
        ProviderInitiatedAt = at;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Marks the attempt confirmed after provider outcome verification and Central PMS finalization.
    /// </summary>
    public void MarkConfirmed(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Confirmed);
        AttemptStatus = PaymentAttemptStatus.Confirmed;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Marks the attempt failed without issuing payment finality for exit.
    /// </summary>
    public void MarkFailed(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Failed);
        AttemptStatus = PaymentAttemptStatus.Failed;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Marks the attempt expired when confirmed provider finality did not arrive in time.
    /// </summary>
    public void MarkExpired(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Expired);
        AttemptStatus = PaymentAttemptStatus.Expired;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Cancels the attempt before confirmed provider finality.
    /// </summary>
    public void Cancel(DateTimeOffset at, string updatedBy)
    {
        EnsureTransitionAllowed(PaymentAttemptStatus.Cancelled);
        AttemptStatus = PaymentAttemptStatus.Cancelled;
        FinalizedAt = at;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Returns whether the attempt can no longer transition to another payment status.
    /// </summary>
    public bool IsTerminal()
    {
        return AttemptStatus is PaymentAttemptStatus.Confirmed
            or PaymentAttemptStatus.Failed
            or PaymentAttemptStatus.Expired
            or PaymentAttemptStatus.Cancelled;
    }

    /// <summary>
    /// Returns whether the attempt still blocks another active attempt for the parking session.
    /// </summary>
    public bool IsActive()
    {
        return AttemptStatus is PaymentAttemptStatus.Initiated or PaymentAttemptStatus.PendingProvider;
    }

    /// <summary>
    /// Returns whether the requested status transition preserves the v1.2 payment attempt lifecycle.
    /// </summary>
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
