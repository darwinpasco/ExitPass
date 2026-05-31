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
/// Persisted or process-local processing record used for idempotency.
/// </summary>
/// <param name="EventId">Source event identifier.</param>
/// <param name="ExitAuthorizationId">Consumed exit authorization identifier.</param>
/// <param name="GateAuthorizationConsumptionId">Central PMS gate consumption identifier.</param>
/// <param name="TariffSnapshotId">Paid tariff snapshot carried by the handoff.</param>
/// <param name="ResultCode">Deterministic processing result code.</param>
/// <param name="ProcessedAtUtc">Processing timestamp.</param>
public sealed record GateAuthorizationConsumedProcessingRecord(
    Guid EventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid TariffSnapshotId,
    string ResultCode,
    DateTimeOffset ProcessedAtUtc);

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
