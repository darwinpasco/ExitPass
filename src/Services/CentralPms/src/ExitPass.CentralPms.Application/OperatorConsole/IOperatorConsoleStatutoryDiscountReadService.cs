namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read service for Operator Console statutory discount validation drafts.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountReadService
{
    /// <summary>
    /// Lists statutory discount validation drafts for the Operator Console queue.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftQueueResult> ListDraftsAsync(
        OperatorConsoleStatutoryDiscountDraftQueueQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one statutory discount validation draft detail.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> GetDraftAsync(
        OperatorConsoleStatutoryDiscountDraftDetailQuery query,
        CancellationToken cancellationToken);
}
