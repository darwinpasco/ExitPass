namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Application boundary for consuming an ExitAuthorization through Central PMS.
///
/// BRD:
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.6 Consume Exit Authorization
///
/// Invariants Enforced:
/// - ExitAuthorization consumption is mediated through Central PMS application logic
/// - Consumption remains an explicit use case, not an incidental HTTP-side effect
/// </summary>
public interface IConsumeExitAuthorizationUseCase
{
    /// <summary>
    /// Consumes an exit authorization through the Central PMS application boundary.
    /// </summary>
    /// <param name="command">Consumption command containing identifiers and trace metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The application-level consume result.</returns>
    Task<ConsumeExitAuthorizationResult> ExecuteAsync(
        ConsumeExitAuthorizationCommand command,
        CancellationToken cancellationToken);
}
