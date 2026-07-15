namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Application boundary for one secret-free HikCentral gate action attempt.
/// </summary>
public interface IHikCentralGateActionAdapter
{
    /// <summary>
    /// Attempts one HikCentral gate action and returns safe outcome metadata.
    /// </summary>
    Task<HikCentralGateActionResult> ExecuteAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safe execution context for a HikCentral gate action attempt.
/// </summary>
public sealed record HikCentralGateActionRequest(
    Guid GateCommandId,
    Guid GateAuthorizationConsumptionId,
    Guid ExitAuthorizationId,
    Guid GateDeviceId,
    Guid VendorSystemId,
    Guid? SiteId,
    Guid? LaneId,
    string TargetResourceCode,
    string VendorOperation,
    Guid CorrelationId,
    DateTimeOffset RequestedAt);

/// <summary>
/// Secret-free result metadata for a HikCentral gate action attempt.
/// </summary>
public sealed record HikCentralGateActionResult(
    string VendorCode,
    string RequestMethod,
    string VendorOperation,
    string TargetResourceCode,
    string ActionOutcome,
    bool Retryable,
    bool FailureRecorded,
    int DurationMs,
    bool TimedOut,
    bool VendorUnavailable,
    bool TransportFailure,
    int? HttpStatusCode,
    string? VendorResultCode,
    string? VendorResultMessage,
    Guid RequestCorrelationId,
    string? VendorCorrelationId,
    DateTimeOffset RequestedAt,
    DateTimeOffset RespondedAt);

/// <summary>
/// Stable constants shared by HikCentral gate action adapter implementations.
/// </summary>
public static class HikCentralGateActionConstants
{
    /// <summary>
    /// Canonical vendor code for HikCentral gate action attempts.
    /// </summary>
    public const string VendorCode = "HIKCENTRAL";

    /// <summary>
    /// POST-only request posture for candidate HikCentral control APIs.
    /// </summary>
    public const string RequestMethod = "POST";

    /// <summary>
    /// Endpoint-neutral operation label for the canonical gate command action.
    /// </summary>
    public const string OpenGateOperation = "OPEN_GATE";

    /// <summary>
    /// Canonical successful audit outcome.
    /// </summary>
    public const string OutcomeSucceeded = "SUCCEEDED";

    /// <summary>
    /// Canonical retryable failure audit outcome.
    /// </summary>
    public const string OutcomeRetryableFailure = "RETRYABLE_FAILURE";

    /// <summary>
    /// Canonical terminal failure audit outcome.
    /// </summary>
    public const string OutcomeTerminalFailure = "TERMINAL_FAILURE";

    /// <summary>
    /// Canonical timeout audit outcome.
    /// </summary>
    public const string OutcomeTimeout = "TIMEOUT";

    /// <summary>
    /// Canonical vendor unavailable audit outcome.
    /// </summary>
    public const string OutcomeVendorUnavailable = "VENDOR_UNAVAILABLE";

    /// <summary>
    /// Canonical transport failure audit outcome.
    /// </summary>
    public const string OutcomeTransportFailure = "TRANSPORT_FAILURE";
}

/// <summary>
/// Controlled rejection for invalid HikCentral gate action adapter requests.
/// </summary>
public sealed class HikCentralGateActionRejectedException : Exception
{
    /// <summary>
    /// Creates a controlled HikCentral gate action rejection.
    /// </summary>
    public HikCentralGateActionRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
    }

    /// <summary>
    /// Controlled error code.
    /// </summary>
    public string ErrorCode { get; }
}
