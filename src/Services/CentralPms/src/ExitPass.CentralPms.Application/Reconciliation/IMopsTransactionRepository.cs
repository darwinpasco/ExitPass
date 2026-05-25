namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Repository boundary for MoPS continuity transaction import and readback.
/// </summary>
public interface IMopsTransactionRepository
{
    /// <summary>Imports one MoPS transaction and creates the supported reconciliation item linkage.</summary>
    Task<MopsImportResult> ImportAsync(
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Lists imported MoPS transactions.</summary>
    Task<IReadOnlyList<MopsTransactionRecord>> ListAsync(
        ListMopsTransactionsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reads one imported MoPS transaction.</summary>
    Task<MopsTransactionRecord> ReadAsync(
        ReadMopsTransactionQuery query,
        CancellationToken cancellationToken);
}
