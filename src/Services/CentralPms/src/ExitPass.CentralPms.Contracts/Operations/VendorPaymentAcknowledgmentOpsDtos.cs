namespace ExitPass.CentralPms.Contracts.Operations;

/// <summary>
/// Read-only ops search request for Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentSearchRequest
{
    /// <summary>Status filter, for example PENDING, RETRY_PENDING, FAILED, CONFIRMED, SKIPPED_DISABLED, or CANCELLED.</summary>
    public string? AcknowledgmentStatus { get; init; }

    /// <summary>Provider-neutral vendor system code, for example HIKCENTRAL.</summary>
    public string? VendorSystemCode { get; init; }

    /// <summary>ExitPass payment attempt identifier.</summary>
    public Guid? PaymentAttemptId { get; init; }

    /// <summary>ExitPass payment confirmation identifier.</summary>
    public Guid? PaymentConfirmationId { get; init; }

    /// <summary>Central PMS parking session identifier.</summary>
    public Guid? ParkingSessionId { get; init; }

    /// <summary>Ticket number captured for the vendor acknowledgment.</summary>
    public string? TicketNumber { get; init; }

    /// <summary>Vendor card number or ticket alias captured for the acknowledgment.</summary>
    public string? CardNum { get; init; }

    /// <summary>End-to-end correlation identifier.</summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>Inclusive lower bound for acknowledgment creation time.</summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>Inclusive upper bound for acknowledgment creation time.</summary>
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>Inclusive lower bound for last vendor attempt time.</summary>
    public DateTimeOffset? LastAttemptedFrom { get; init; }

    /// <summary>Inclusive upper bound for last vendor attempt time.</summary>
    public DateTimeOffset? LastAttemptedTo { get; init; }

    /// <summary>When true, returns only retry-pending acknowledgments that are due now.</summary>
    public bool NextRetryDueOnly { get; init; }

    /// <summary>Zero-based page index.</summary>
    public int? PageIndex { get; init; }

    /// <summary>Requested page size. The server bounds the effective value.</summary>
    public int? PageSize { get; init; }
}

/// <summary>
/// Paginated ops search response for Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentSearchResponse
{
    /// <summary>Current page of matching acknowledgments.</summary>
    public IReadOnlyList<VendorPaymentAcknowledgmentSummary> Items { get; init; } = Array.Empty<VendorPaymentAcknowledgmentSummary>();

    /// <summary>Status bucket counts for the filtered result set.</summary>
    public VendorPaymentAcknowledgmentStatusBuckets StatusBuckets { get; init; } = new();

    /// <summary>Zero-based page index returned.</summary>
    public int PageIndex { get; init; }

    /// <summary>Effective page size returned.</summary>
    public int PageSize { get; init; }

    /// <summary>True when another page is available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>
/// Ops detail response for one Vendor PMS payment acknowledgment.
/// </summary>
public sealed class VendorPaymentAcknowledgmentDetailResponse : VendorPaymentAcknowledgmentSummary
{
    /// <summary>Derived safe diagnostics for operations troubleshooting.</summary>
    public IReadOnlyList<VendorPaymentAcknowledgmentDiagnosticDto> Diagnostics { get; init; } =
        Array.Empty<VendorPaymentAcknowledgmentDiagnosticDto>();
}

/// <summary>
/// Safe, read-only Vendor PMS payment acknowledgment summary.
/// </summary>
public class VendorPaymentAcknowledgmentSummary
{
    /// <summary>Durable vendor payment acknowledgment identifier.</summary>
    public Guid VendorPaymentAcknowledgmentId { get; init; }

    /// <summary>ExitPass payment attempt identifier.</summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>ExitPass payment confirmation identifier.</summary>
    public Guid PaymentConfirmationId { get; init; }

    /// <summary>Central PMS parking session identifier when available.</summary>
    public Guid? ParkingSessionId { get; init; }

    /// <summary>Provider-neutral vendor system code.</summary>
    public string VendorSystemCode { get; init; } = string.Empty;

    /// <summary>Vendor session reference captured with the acknowledgment.</summary>
    public string? VendorSessionRef { get; init; }

    /// <summary>Ticket number captured with the acknowledgment.</summary>
    public string? TicketNumber { get; init; }

    /// <summary>Vendor card number or ticket alias captured with the acknowledgment.</summary>
    public string? CardNum { get; init; }

    /// <summary>Durable acknowledgment status.</summary>
    public string AcknowledgmentStatus { get; init; } = string.Empty;

    /// <summary>Dashboard-friendly status bucket.</summary>
    public string StatusBucket { get; init; } = string.Empty;

    /// <summary>Safe vendor result code.</summary>
    public string? VendorCode { get; init; }

    /// <summary>Safe vendor result message.</summary>
    public string? VendorMessage { get; init; }

    /// <summary>Requested fee in minor currency units.</summary>
    public long? RequestFeeMinorUnits { get; init; }

    /// <summary>Requested fee currency code.</summary>
    public string? RequestCurrencyCode { get; init; }

    /// <summary>Vendor-confirmed fee in minor currency units when available.</summary>
    public long? ConfirmedFeeMinorUnits { get; init; }

    /// <summary>Vendor-confirmed timestamp when available.</summary>
    public DateTimeOffset? VendorConfirmedAt { get; init; }

    /// <summary>Number of vendor acknowledgment attempts recorded.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Most recent vendor acknowledgment attempt timestamp.</summary>
    public DateTimeOffset? LastAttemptedAt { get; init; }

    /// <summary>Next scheduled retry timestamp when retry is pending.</summary>
    public DateTimeOffset? NextRetryAt { get; init; }

    /// <summary>End-to-end correlation identifier when recorded.</summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>Record creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Record update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Filtered status bucket counts for Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentStatusBuckets
{
    /// <summary>Pending acknowledgment count.</summary>
    public int Pending { get; init; }

    /// <summary>Retry-pending acknowledgment count.</summary>
    public int RetryPending { get; init; }

    /// <summary>Failed acknowledgment count.</summary>
    public int Failed { get; init; }

    /// <summary>Confirmed acknowledgment count.</summary>
    public int Confirmed { get; init; }

    /// <summary>Skipped-disabled acknowledgment count.</summary>
    public int SkippedDisabled { get; init; }

    /// <summary>Cancelled acknowledgment count.</summary>
    public int Cancelled { get; init; }
}

/// <summary>
/// Derived safe diagnostic detail for ops troubleshooting.
/// </summary>
public sealed record VendorPaymentAcknowledgmentDiagnosticDto(
    string Code,
    string Message,
    string Source,
    bool Retryable,
    Guid? CorrelationId = null);
