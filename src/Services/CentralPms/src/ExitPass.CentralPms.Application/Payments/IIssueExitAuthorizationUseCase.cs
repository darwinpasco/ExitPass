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
    /// <summary>
    /// Issues an exit authorization through Central PMS after confirmed payment finality.
    /// </summary>
    /// <param name="command">Issuance command containing session, payment attempt, actor, and trace identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The application-level issuance result.</returns>
    Task<IssueExitAuthorizationResult> ExecuteAsync(
        IssueExitAuthorizationCommand command,
        CancellationToken cancellationToken);
}
