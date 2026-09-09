namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Requests DB-backed issuance of a single-use exit authorization from one explicit completion basis.
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
/// - Paid ExitAuthorization issuance remains bound to canonical payment finality
/// - Statutory zero-payable issuance carries no fabricated payment ancestry
/// - Issuance requests must carry correlation metadata for end-to-end traceability
/// </summary>
public sealed record IssueExitAuthorizationCommand(
    Guid ParkingSessionId,
    Guid? PaymentAttemptId,
    Guid RequestedByUserId,
    Guid CorrelationId,
    string CompletionBasis = CompletionBasisCodes.PaymentFinality,
    CompletionAuthority? CompletionAuthority = null,
    Guid? FiscalIssuanceReferenceId = null);
