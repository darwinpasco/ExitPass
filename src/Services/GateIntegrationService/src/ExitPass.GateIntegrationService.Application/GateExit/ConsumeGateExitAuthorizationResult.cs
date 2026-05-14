namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Result of a gate exit authorization consume attempt.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
///
/// Invariants Enforced:
/// - Gate open state is reported separately from Central PMS authorization state.
/// - No financial or payment-finality authority is represented by this result.
/// </summary>
public sealed record ConsumeGateExitAuthorizationResult(
    bool GateOpened,
    string ResultCode,
    string AuthorizationStatus,
    Guid ExitAuthorizationId,
    DateTimeOffset? ConsumedAt);
