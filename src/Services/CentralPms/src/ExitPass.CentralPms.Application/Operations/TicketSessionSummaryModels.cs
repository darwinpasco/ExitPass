namespace ExitPass.CentralPms.Application.Operations;

/// <summary>
/// Command for retrieving an ops-facing ticket session summary.
/// </summary>
public sealed record TicketSessionSummaryCommand(
    string? TicketNumber,
    string? CardNum,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid CorrelationId);

/// <summary>
/// Ticket summary result outcome.
/// </summary>
public enum TicketSessionSummaryOutcome
{
    /// <summary>
    /// Ticket summary was composed successfully.
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// Request was invalid.
    /// </summary>
    InvalidRequest = 1,

    /// <summary>
    /// No vendor session exists for the ticket.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// Ticket lookup is ambiguous.
    /// </summary>
    Ambiguous = 3,

    /// <summary>
    /// Vendor PMS returned a business rejection or malformed data.
    /// </summary>
    VendorError = 4,

    /// <summary>
    /// Vendor adapter or vendor PMS is unavailable.
    /// </summary>
    AdapterUnavailable = 5
}

/// <summary>
/// Application result for ticket session summary.
/// </summary>
public sealed record TicketSessionSummaryResult(
    TicketSessionSummaryOutcome Outcome,
    TicketSessionSummaryReadModel? Summary,
    string? ErrorCode,
    bool Retryable,
    IReadOnlyList<TicketSessionSummaryDiagnostic> Diagnostics,
    Guid CorrelationId)
{
    /// <summary>
    /// Creates a successful summary result.
    /// </summary>
    public static TicketSessionSummaryResult Resolved(
        TicketSessionSummaryReadModel summary,
        IReadOnlyList<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId) =>
        new(TicketSessionSummaryOutcome.Resolved, summary, null, false, diagnostics, correlationId);

    /// <summary>
    /// Creates a failed summary result.
    /// </summary>
    public static TicketSessionSummaryResult Failed(
        TicketSessionSummaryOutcome outcome,
        string errorCode,
        bool retryable,
        IReadOnlyList<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId) =>
        new(outcome, null, errorCode, retryable, diagnostics, correlationId);
}

/// <summary>
/// Provider-neutral summary model composed from vendor and Central PMS state.
/// </summary>
public sealed record TicketSessionSummaryReadModel(
    string TicketNumber,
    string? CardNum,
    string PlateLicense,
    DateTimeOffset? ParkingInTime,
    int? ParkingDurationSeconds,
    long? FeeMinorUnits,
    string? CurrencyCode,
    string? FeeRuleType,
    string? FeeRuleIndexCode,
    string? FeeRuleName,
    string? VendorSessionStatus,
    string? VendorSystemCode,
    string? VendorConfirmationCode,
    string? VendorMessage,
    Guid? ParkingSessionId,
    Guid? PaymentAttemptId,
    string? PaymentAttemptStatus,
    string? PaymentStatus,
    string? PaymentConfirmationStatus,
    string? VendorConfirmationStatus,
    DateTimeOffset? VendorConfirmationTimestamp);

/// <summary>
/// Read-only local payment/session status for a ticket.
/// </summary>
public sealed record TicketSessionLocalStatusReadModel(
    Guid ParkingSessionId,
    Guid? PaymentAttemptId,
    string? PaymentAttemptStatus,
    string? PaymentStatus,
    string? PaymentConfirmationStatus,
    string? VendorConfirmationStatus,
    DateTimeOffset? VendorConfirmationTimestamp);

/// <summary>
/// Local Central PMS ticket status lookup result.
/// </summary>
public sealed record TicketSessionLocalStatusResult(
    TicketSessionLocalStatusOutcome Outcome,
    TicketSessionLocalStatusReadModel? Status);

/// <summary>
/// Outcome for local Central PMS ticket status lookup.
/// </summary>
public enum TicketSessionLocalStatusOutcome
{
    /// <summary>
    /// One local status record was found.
    /// </summary>
    Found = 0,

    /// <summary>
    /// No local status was found.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// More than one matching local session exists.
    /// </summary>
    Ambiguous = 2
}

/// <summary>
/// Compact non-raw diagnostic entry.
/// </summary>
public sealed record TicketSessionSummaryDiagnostic(
    string Code,
    string Message,
    string Source,
    bool Retryable,
    string? VendorSystemCode = null,
    string? VendorConfirmationCode = null,
    string? VendorMessage = null,
    Guid? CorrelationId = null);
