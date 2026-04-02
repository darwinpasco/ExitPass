namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Requests DB-backed issuance of a single-use exit authorization for a confirmed payment attempt.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization may only be issued after confirmed payment finality
/// - ExitAuthorization issuance must remain bound to the canonical payment attempt
/// - Issuance requests must carry correlation metadata for end-to-end traceability
/// </summary>
public sealed record IssueExitAuthorizationCommand(
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid RequestedByUserId,
    Guid CorrelationId);
