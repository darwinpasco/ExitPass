namespace ExitPass.CentralPms.Contracts.Common;

/// <summary>
/// Standard API error envelope returned by Central PMS endpoints.
///
/// BRD:
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 9.16 Monitoring and Administration
///
/// SDD:
/// - 10 API Architecture
///
/// Invariants Enforced:
/// - Error responses carry a stable machine-readable error code
/// - Correlation identifiers are always returned for traceability
/// - Optional error details may be omitted when not applicable
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Stable machine-readable application error code.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Correlation identifier for tracing the request across services.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Indicates whether the caller may retry the request safely.
    /// </summary>
    public bool Retryable { get; set; }

    /// <summary>
    /// Stable client-consumable outcome classification for endpoints that expose recoverable command workflows.
    /// </summary>
    public string? ClientResultStatus { get; set; }

    /// <summary>
    /// Stable recovery classification for callers that need deterministic retry or correction behavior.
    /// </summary>
    public string? RecoveryClassification { get; set; }

    /// <summary>
    /// Safe machine-readable recovery action for clients.
    /// </summary>
    public string? RecoveryAction { get; set; }

    /// <summary>
    /// Optional structured error details.
    /// </summary>
    public Dictionary<string, object?>? Details { get; set; }
}
