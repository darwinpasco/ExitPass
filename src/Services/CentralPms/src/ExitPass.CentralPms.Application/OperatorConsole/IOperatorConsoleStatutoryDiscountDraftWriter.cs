namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Persists privacy-minimized Operator Console statutory discount validation drafts.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountDraftWriter
{
    /// <summary>
    /// Persists one draft statutory discount validation row.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken);
}
