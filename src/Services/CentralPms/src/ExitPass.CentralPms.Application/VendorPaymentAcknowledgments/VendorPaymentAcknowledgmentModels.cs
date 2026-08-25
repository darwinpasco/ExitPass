namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Controlled Vendor PMS acknowledgment statuses.
/// </summary>
public static class VendorPaymentAcknowledgmentStatuses
{
    /// <summary>Created after ExitPass payment finality and waiting for Vendor PMS acknowledgment.</summary>
    public const string Pending = "PENDING";

    /// <summary>Vendor PMS accepted the paid-state acknowledgment.</summary>
    public const string Confirmed = "CONFIRMED";

    /// <summary>Vendor PMS acknowledgment failed without a scheduled retry.</summary>
    public const string Failed = "FAILED";

    /// <summary>Vendor PMS acknowledgment was skipped because the local confirmation guard was disabled.</summary>
    public const string SkippedDisabled = "SKIPPED_DISABLED";

    /// <summary>Vendor PMS acknowledgment failed and is eligible for later retry execution.</summary>
    public const string RetryPending = "RETRY_PENDING";

    /// <summary>Vendor PMS acknowledgment was cancelled before completion.</summary>
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Command to create a durable Vendor PMS acknowledgment record after ExitPass payment finality.
/// </summary>
public sealed record CreateVendorPaymentAcknowledgmentCommand(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid? ParkingSessionId,
    string VendorSystemCode,
    string? VendorSessionRef,
    string? TicketNumber,
    string? CardNum,
    long? RequestFeeMinorUnits,
    string? RequestCurrencyCode,
    string? IdempotencyKey,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Command to mark Vendor PMS acknowledgment as confirmed.
/// </summary>
public sealed record MarkVendorPaymentAcknowledgmentConfirmedCommand(
    Guid VendorPaymentAcknowledgmentId,
    string? VendorCode,
    string? VendorMessage,
    long? ConfirmedFeeMinorUnits,
    DateTimeOffset? VendorConfirmedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Command to mark Vendor PMS acknowledgment as failed or retry-pending.
/// </summary>
public sealed record MarkVendorPaymentAcknowledgmentFailedCommand(
    Guid VendorPaymentAcknowledgmentId,
    string? VendorCode,
    string? VendorMessage,
    DateTimeOffset LastAttemptedAt,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Command to mark Vendor PMS acknowledgment as skipped because confirmation is disabled.
/// </summary>
public sealed record MarkVendorPaymentAcknowledgmentSkippedDisabledCommand(
    Guid VendorPaymentAcknowledgmentId,
    string? VendorMessage,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Durable Vendor PMS acknowledgment read model.
/// </summary>
public sealed record VendorPaymentAcknowledgmentRecord(
    Guid VendorPaymentAcknowledgmentId,
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid? ParkingSessionId,
    string VendorSystemCode,
    string? VendorSessionRef,
    string? TicketNumber,
    string? CardNum,
    string AcknowledgmentStatus,
    string? VendorCode,
    string? VendorMessage,
    long? RequestFeeMinorUnits,
    string? RequestCurrencyCode,
    long? ConfirmedFeeMinorUnits,
    DateTimeOffset? VendorConfirmedAt,
    int AttemptCount,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? NextRetryAt,
    string? IdempotencyKey,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Immutable payment/session basis used to acknowledge paid state to the Vendor PMS.
/// </summary>
public sealed record VendorPaymentAcknowledgmentBasis(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid ParkingSessionId,
    string VendorSystemCode,
    string? VendorSessionRef,
    string? TicketNumber,
    string? CardNum,
    long RequestFeeMinorUnits,
    string RequestCurrencyCode)
{
    /// <summary>Canonical masked plate retained by the immutable parking session.</summary>
    public string? PlateNumber { get; init; }
    public Guid SiteId { get; init; }
    public Guid SiteGroupId { get; init; }
    public Guid VendorSystemId { get; init; }
    public Guid SourceAdapterIdentityId { get; init; }
}

/// <summary>
/// Command to process the Vendor PMS paid-state acknowledgment after ExitPass finality.
/// </summary>
public sealed record VendorPaymentAcknowledgmentWorkflowCommand(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid ParkingSessionId,
    Guid CorrelationId);

/// <summary>
/// Command to dispatch a bounded batch of due Vendor PMS acknowledgment retries.
/// </summary>
public sealed record DispatchVendorPaymentAcknowledgmentRetriesCommand(int BatchSize);

/// <summary>
/// Result for one Vendor PMS acknowledgment retry dispatch item.
/// </summary>
public sealed record VendorPaymentAcknowledgmentRetryDispatchItemResult(
    Guid VendorPaymentAcknowledgmentId,
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid? ParkingSessionId,
    string VendorSystemCode,
    string InitialStatus,
    string? FinalStatus,
    bool Succeeded,
    bool Skipped,
    string? FailureCode,
    Guid? CorrelationId);

/// <summary>
/// Result for a bounded Vendor PMS acknowledgment retry dispatch.
/// </summary>
public sealed record VendorPaymentAcknowledgmentRetryDispatchResult(
    int RequestedBatchSize,
    int DueCount,
    int ProcessedCount,
    int ConfirmedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<VendorPaymentAcknowledgmentRetryDispatchItemResult> Items);

/// <summary>
/// Bounded ops query for durable Vendor PMS payment acknowledgments.
/// </summary>
public sealed record SearchVendorPaymentAcknowledgmentsQuery(
    string? AcknowledgmentStatus,
    string? VendorSystemCode,
    Guid? PaymentAttemptId,
    Guid? PaymentConfirmationId,
    Guid? ParkingSessionId,
    string? TicketNumber,
    string? CardNum,
    Guid? CorrelationId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    DateTimeOffset? LastAttemptedFrom,
    DateTimeOffset? LastAttemptedTo,
    bool NextRetryDueOnly,
    DateTimeOffset UtcNow,
    int PageIndex,
    int PageSize);

/// <summary>
/// Status bucket counts for a filtered Vendor PMS acknowledgment query.
/// </summary>
public sealed record VendorPaymentAcknowledgmentStatusBucketCounts(
    int Pending,
    int RetryPending,
    int Failed,
    int Confirmed,
    int SkippedDisabled,
    int Cancelled);

/// <summary>
/// Bounded ops search result for durable Vendor PMS payment acknowledgments.
/// </summary>
public sealed record VendorPaymentAcknowledgmentSearchResult(
    IReadOnlyList<VendorPaymentAcknowledgmentRecord> Items,
    VendorPaymentAcknowledgmentStatusBucketCounts StatusBuckets,
    int PageIndex,
    int PageSize,
    bool HasMore);

/// <summary>
/// Raised when the database rejects a duplicate or conflicting Vendor PMS acknowledgment record.
/// </summary>
public sealed class VendorPaymentAcknowledgmentConflictException : Exception
{
    /// <summary>Creates a conflict exception.</summary>
    public VendorPaymentAcknowledgmentConflictException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Stable application error code.</summary>
    public string ErrorCode { get; }
}
