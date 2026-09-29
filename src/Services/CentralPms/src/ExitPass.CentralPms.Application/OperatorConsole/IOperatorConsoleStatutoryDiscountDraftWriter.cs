namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Persists privacy-minimized Operator Console statutory discount validation drafts.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountDraftWriter
{
    /// <summary>
    /// Finds an existing reusable statutory discount validation draft and its persisted policy snapshot.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult?> FindReusableAsync(
        Guid parkingSessionId,
        string entitlementType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists one draft statutory discount validation row.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken);
}
