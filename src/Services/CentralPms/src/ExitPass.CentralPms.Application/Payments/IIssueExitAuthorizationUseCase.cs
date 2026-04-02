namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Application boundary for issuing a canonical ExitAuthorization from a confirmed payment attempt.
///
/// BRD:
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.5 Issue Exit Authorization
///
/// Invariants Enforced:
/// - ExitAuthorization issuance is mediated through Central PMS application logic
/// - Issuance remains an explicit use case, not an incidental side effect at the HTTP boundary
/// </summary>
public interface IIssueExitAuthorizationUseCase
{
    Task<IssueExitAuthorizationResult> ExecuteAsync(
        IssueExitAuthorizationCommand command,
        CancellationToken cancellationToken);
}
