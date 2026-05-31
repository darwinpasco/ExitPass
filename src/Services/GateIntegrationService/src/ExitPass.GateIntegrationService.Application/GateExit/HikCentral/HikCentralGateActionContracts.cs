namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// HikCentral Access Control door operation targeted by the gate command contract.
/// </summary>
public enum HikCentralDoorControlType
{
    /// <summary>
    /// Keep the access point open.
    /// </summary>
    RemainOpen = 0,

    /// <summary>
    /// Close the access point.
    /// </summary>
    Close = 1,

    /// <summary>
    /// Open the access point once.
    /// </summary>
    Open = 2,

    /// <summary>
    /// Keep the access point closed.
    /// </summary>
    RemainClosed = 3
}

/// <summary>
/// HikCentral Access Control door direction targeted by the gate command contract.
/// </summary>
public enum HikCentralDoorControlDirection
{
    /// <summary>
    /// Entry direction.
    /// </summary>
    Entry = 0,

    /// <summary>
    /// Exit direction.
    /// </summary>
    Exit = 1
}

/// <summary>
/// Canonical HikCentral gate action outcome classification.
/// </summary>
public enum HikCentralGateActionOutcome
{
    /// <summary>
    /// HikCentral accepted the access point operation.
    /// </summary>
    Succeeded,

    /// <summary>
    /// HikCentral returned a retryable failure.
    /// </summary>
    RetryableFailure,

    /// <summary>
    /// HikCentral returned a non-retryable failure.
    /// </summary>
    TerminalFailure,

    /// <summary>
    /// The call timed out before a definitive vendor result was received.
    /// </summary>
    Timeout,

    /// <summary>
    /// Authentication, signature, token, or authorization failed.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// Adapter or HikCentral endpoint configuration is incomplete or invalid.
    /// </summary>
    Misconfigured,

    /// <summary>
    /// HikCentral or its gateway is unavailable.
    /// </summary>
    VendorUnavailable,

    /// <summary>
    /// HikCentral rejected the request shape or target resource.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// HikCentral returned a failure that is not yet specifically classified.
    /// </summary>
    Unknown
}

/// <summary>
/// HikCentral vendor request projected from a vendor-neutral gate command.
/// </summary>
public sealed record HikCentralGateActionRequest(
    Guid CommandId,
    Guid SourceProcessingId,
    Guid SourceEventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid? GateDeviceId,
    string? GateDeviceIdentifier,
    Guid? LaneId,
    Guid? SiteId,
    Guid? VendorSystemId,
    Guid CorrelationId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ConsumedAtUtc,
    int CommandAttemptNumber,
    string DoorIndexCode,
    HikCentralDoorControlType ControlType,
    HikCentralDoorControlDirection ControlDirection);

/// <summary>
/// Per-access-point result returned by HikCentral door control.
/// </summary>
public sealed record HikCentralDoorControlResult(
    string DoorIndexCode,
    int ControlResultCode,
    string? ControlResultDescription);

/// <summary>
/// Classified HikCentral gate action response.
/// </summary>
public sealed record HikCentralGateActionResponse(
    HikCentralGateActionOutcome Outcome,
    string? VendorRequestId,
    string? VendorCorrelationId,
    string? VendorResponseCode,
    string? VendorResponseMessage,
    string RawStatusCategory,
    bool Retryable,
    bool TerminalFailure,
    string DiagnosticMessage,
    DateTimeOffset ResponseTimestampUtc,
    IReadOnlyList<HikCentralDoorControlResult> DoorResults);

/// <summary>
/// Raw HikCentral response envelope shape used by the classifier.
/// </summary>
public sealed record HikCentralGateActionEnvelope(
    string? Code,
    string? Message,
    IReadOnlyList<HikCentralDoorControlResult> DoorResults);

/// <summary>
/// Transport-level outcome passed to the classifier without performing live HTTP.
/// </summary>
public sealed record HikCentralGateActionTransportResult(
    int? HttpStatusCode,
    HikCentralGateActionEnvelope? Envelope,
    string? VendorRequestId,
    string? VendorCorrelationId,
    bool TimedOut,
    bool VendorUnavailable,
    string? TransportError,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Contract for a future HikCentral gate action client implementation.
/// </summary>
public interface IHikCentralGateActionClient
{
    /// <summary>
    /// Executes a HikCentral gate action. No live implementation is registered in this slice.
    /// </summary>
    Task<HikCentralGateActionResponse> ExecuteAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken);
}
