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
    /// Optional structured error details.
    /// </summary>
    public Dictionary<string, object?>? Details { get; set; }
}
