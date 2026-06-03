namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Reads and writes metadata-only statutory discount evidence for Operator Console drafts.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountEvidenceRepository
{
    /// <summary>
    /// Gets minimal draft context for evidence access and deterministic 404 behavior.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountEvidenceDraftContext?> GetDraftContextAsync(
        Guid draftId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures metadata-only evidence and updates the draft evidence satisfied flag.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult> CaptureAsync(
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists evidence metadata for one draft.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountEvidenceListResult> ListAsync(
        Guid draftId,
        Guid correlationId,
        CancellationToken cancellationToken);
}

