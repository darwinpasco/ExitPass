namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Handles access-gated Operator Console statutory discount validation drafts.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountDraftService
{
    /// <summary>
    /// Validates and drafts a statutory discount validation request after access evaluation.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
        OperatorConsoleStatutoryDiscountDraftCommand command,
        CancellationToken cancellationToken);
}
