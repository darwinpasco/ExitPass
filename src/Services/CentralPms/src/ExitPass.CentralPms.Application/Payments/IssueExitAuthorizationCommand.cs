namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Requests DB-backed issuance of a single-use exit authorization for an explicit completion authority.
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
/// - Payment completion remains bound to the canonical payment attempt and confirmation
/// - Statutory zero-payable completion carries no payment identifiers
/// - Issuance requests must carry correlation metadata for end-to-end traceability
/// </summary>
public sealed record IssueExitAuthorizationCommand(
    Guid ParkingSessionId,
    Guid? PaymentAttemptId,
    Guid RequestedByUserId,
    Guid CorrelationId,
    string CompletionBasis = CompletionBasisCodes.PaymentFinality,
    CompletionAuthority? CompletionAuthority = null,
    bool FiscalPrerequisiteSatisfied = false);
