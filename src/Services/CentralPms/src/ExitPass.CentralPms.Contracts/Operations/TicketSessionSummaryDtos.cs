namespace ExitPass.CentralPms.Contracts.Operations;

/// <summary>
/// Request body for retrieving an ops-facing ticket session summary.
/// </summary>
public sealed class TicketSessionSummaryRequest
{
    /// <summary>
    /// Ticket number supplied by an operator or customer channel.
    /// </summary>
    public string? TicketNumber { get; init; }

    /// <summary>
    /// Vendor card number alias for ticket-based lookup.
    /// </summary>
    public string? CardNum { get; init; }

    /// <summary>
    /// Optional Central PMS site scope.
    /// </summary>
    public Guid? SiteId { get; init; }

    /// <summary>
    /// Optional Central PMS site group scope.
    /// </summary>
    public Guid? SiteGroupId { get; init; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Ops-facing ticket/session/tariff/payment/vendor summary.
/// </summary>
public sealed class TicketSessionSummaryResponse
{
    /// <summary>
    /// Ticket number used for lookup.
    /// </summary>
    public string TicketNumber { get; init; } = string.Empty;

    /// <summary>
    /// Card number alias used for lookup when supplied.
    /// </summary>
    public string? CardNum { get; init; }

    /// <summary>
    /// Plate license returned by the vendor PMS or Unknown when unavailable.
    /// </summary>
    public string PlateLicense { get; init; } = "Unknown";

    /// <summary>
    /// Parking entry timestamp from vendor PMS.
    /// </summary>
    public DateTimeOffset? ParkingInTime { get; init; }

    /// <summary>
    /// Parking duration in seconds when supplied by the vendor PMS.
    /// </summary>
    public int? ParkingDurationSeconds { get; init; }

    /// <summary>
    /// Fee in minor currency units.
    /// </summary>
    public long? FeeMinorUnits { get; init; }

    /// <summary>
    /// Currency for the fee.
    /// </summary>
    public string? CurrencyCode { get; init; }

    /// <summary>
    /// Provider-neutral fee rule type when available.
    /// </summary>
    public string? FeeRuleType { get; init; }

    /// <summary>
    /// Vendor fee rule or tariff version reference.
    /// </summary>
    public string? FeeRuleIndexCode { get; init; }

    /// <summary>
    /// Vendor fee rule display name.
    /// </summary>
    public string? FeeRuleName { get; init; }

    /// <summary>
    /// Current vendor session status.
    /// </summary>
    public string? VendorSessionStatus { get; init; }

    /// <summary>
    /// Provider-neutral vendor system identifier, for example HIKCENTRAL or FAKE_PMS.
    /// </summary>
    public string? VendorSystemCode { get; init; }

    /// <summary>
    /// Provider-neutral vendor confirmation or diagnostic result code.
    /// </summary>
    public string? VendorConfirmationCode { get; init; }

    /// <summary>
    /// Provider-neutral vendor status or diagnostic message, when available.
    /// </summary>
    public string? VendorMessage { get; init; }

    /// <summary>
    /// Latest Central PMS parking session identifier when this ticket has local state.
    /// </summary>
    public Guid? ParkingSessionId { get; init; }

    /// <summary>
    /// Latest Central PMS payment attempt identifier when available.
    /// </summary>
    public Guid? PaymentAttemptId { get; init; }

    /// <summary>
    /// Latest Central PMS payment attempt status when available.
    /// </summary>
    public string? PaymentAttemptStatus { get; init; }

    /// <summary>
    /// Human-readable payment status derived from Central PMS payment state.
    /// </summary>
    public string? PaymentStatus { get; init; }

    /// <summary>
    /// Latest Central PMS payment confirmation status when available.
    /// </summary>
    public string? PaymentConfirmationStatus { get; init; }

    /// <summary>
    /// Vendor payment confirmation status when Central PMS has a durable value for it.
    /// </summary>
    public string? VendorConfirmationStatus { get; init; }

    /// <summary>
    /// Vendor payment confirmation timestamp when Central PMS has a durable value for it.
    /// </summary>
    public DateTimeOffset? VendorConfirmationTimestamp { get; init; }

    /// <summary>
    /// Optional status and diagnostic notes.
    /// </summary>
    public IReadOnlyList<TicketSessionSummaryDiagnosticDto> Diagnostics { get; init; } = Array.Empty<TicketSessionSummaryDiagnosticDto>();

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Compact non-raw diagnostic detail for ticket session summary calls.
/// </summary>
public sealed record TicketSessionSummaryDiagnosticDto(
    string Code,
    string Message,
    string Source,
    bool Retryable,
    string? VendorSystemCode = null,
    string? VendorConfirmationCode = null,
    string? VendorMessage = null,
    Guid? CorrelationId = null);
