namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Consumes an issued exit authorization through the canonical DB-backed control path.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization may be consumed only once
/// - Consumption must remain bound to an existing ExitAuthorization record
/// - Consumption requests must carry correlation metadata for traceability
/// </summary>
public sealed record ConsumeExitAuthorizationCommand(
    Guid ExitAuthorizationId,
    Guid RequestedByUserId,
    Guid CorrelationId);
