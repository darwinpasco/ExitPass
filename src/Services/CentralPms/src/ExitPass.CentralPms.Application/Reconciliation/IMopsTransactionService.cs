namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Application service for MoPS continuity transaction imports.
/// </summary>
public interface IMopsTransactionService
{
    /// <summary>Imports one MoPS continuity transaction as reconciliation evidence.</summary>
    Task<MopsImportResult> ImportAsync(
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Lists imported MoPS continuity transaction records.</summary>
    Task<IReadOnlyList<MopsTransactionRecord>> ListAsync(
        ListMopsTransactionsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reads one imported MoPS continuity transaction record.</summary>
    Task<MopsTransactionRecord> ReadAsync(
        ReadMopsTransactionQuery query,
        CancellationToken cancellationToken);
}
