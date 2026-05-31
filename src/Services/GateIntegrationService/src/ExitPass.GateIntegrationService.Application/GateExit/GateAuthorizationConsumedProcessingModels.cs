namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Command to process a Central PMS GateAuthorizationConsumed handoff.
/// </summary>
/// <param name="Handoff">Validated or raw handoff payload.</param>
public sealed record ProcessGateAuthorizationConsumedCommand(GateAuthorizationConsumedHandoff Handoff);

/// <summary>
/// Result of processing a consumed authorization handoff.
/// </summary>
/// <param name="EventId">Source event identifier.</param>
/// <param name="ExitAuthorizationId">Consumed exit authorization identifier.</param>
/// <param name="GateAuthorizationConsumptionId">Central PMS gate consumption identifier.</param>
/// <param name="TariffSnapshotId">Paid tariff snapshot carried by the handoff.</param>
/// <param name="ResultCode">Deterministic processing result code.</param>
/// <param name="AdapterInvoked">Whether the vendor-neutral gate action adapter was invoked.</param>
/// <param name="AlreadyProcessed">Whether this result came from a prior processing record.</param>
/// <param name="ProcessedAtUtc">Processing timestamp.</param>
public sealed record GateAuthorizationConsumedProcessingResult(
    Guid EventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid TariffSnapshotId,
    string ResultCode,
    bool AdapterInvoked,
    bool AlreadyProcessed,
    DateTimeOffset ProcessedAtUtc);

/// <summary>
/// Durable processing status for consumed authorization handoffs.
/// </summary>
public enum GateAuthorizationConsumedProcessingStatus
{
    /// <summary>
    /// Processing has been started and the adapter may be invoked.
    /// </summary>
    Processing,

    /// <summary>
    /// Processing has completed successfully.
    /// </summary>
    Processed,

    /// <summary>
    /// Processing failed before a successful adapter result was recorded.
    /// </summary>
    Failed
}

/// <summary>
/// Result of opening or reading the durable processing state for a handoff.
/// </summary>
/// <param name="Record">Current durable processing record.</param>
/// <param name="CanInvokeAdapter">Whether this delivery may invoke the gate action adapter.</param>
/// <param name="AlreadyProcessed">Whether the event was already processed successfully.</param>
/// <param name="AlreadyInProgress">Whether another delivery has already started processing this event.</param>
public sealed record GateAuthorizationConsumedProcessingStart(
    GateAuthorizationConsumedProcessingRecord Record,
    bool CanInvokeAdapter,
    bool AlreadyProcessed,
    bool AlreadyInProgress);

/// <summary>
/// Persisted or process-local processing record used for idempotency.
/// </summary>
/// <param name="EventId">Source event identifier.</param>
/// <param name="ExitAuthorizationId">Consumed exit authorization identifier.</param>
/// <param name="GateAuthorizationConsumptionId">Central PMS gate consumption identifier.</param>
/// <param name="TariffSnapshotId">Paid tariff snapshot carried by the handoff.</param>
/// <param name="ResultCode">Deterministic processing result code.</param>
/// <param name="ProcessedAtUtc">Processing timestamp.</param>
/// <param name="ProcessingStatus">Current durable processing status.</param>
/// <param name="AttemptCount">Number of processing attempts recorded for this handoff.</param>
/// <param name="LastFailureCode">Last deterministic failure code, if any.</param>
/// <param name="LastFailureReason">Last failure reason, if any.</param>
public sealed record GateAuthorizationConsumedProcessingRecord(
    Guid EventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid TariffSnapshotId,
    string ResultCode,
    DateTimeOffset ProcessedAtUtc,
    GateAuthorizationConsumedProcessingStatus ProcessingStatus = GateAuthorizationConsumedProcessingStatus.Processed,
    int AttemptCount = 1,
    string? LastFailureCode = null,
    string? LastFailureReason = null)
{
    /// <summary>
    /// Stable durable idempotency key. Falls back to the consumption id when the event id is unavailable.
    /// </summary>
    public Guid ProcessingKey => EventId == Guid.Empty ? GateAuthorizationConsumptionId : EventId;
}

/// <summary>
/// Result from validating handoff site/lane/device scope.
/// </summary>
/// <param name="IsValid">Whether the scope is valid.</param>
/// <param name="ResultCode">Deterministic result code.</param>
/// <param name="Message">Human-readable validation message.</param>
public sealed record GateAuthorizationConsumedScopeValidationResult(
    bool IsValid,
    string ResultCode,
    string Message)
{
    /// <summary>
    /// Valid scope result.
    /// </summary>
    public static GateAuthorizationConsumedScopeValidationResult Valid() =>
        new(true, "SCOPE_VALID", "Gate handoff scope is valid.");

    /// <summary>
    /// Invalid scope result.
    /// </summary>
    public static GateAuthorizationConsumedScopeValidationResult Invalid(string resultCode, string message) =>
        new(false, resultCode, message);
}
