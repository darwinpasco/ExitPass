namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated metadata-only statutory discount evidence intake service.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountEvidenceService
{
    /// <summary>
    /// Captures metadata-only evidence for a statutory discount draft.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?> CaptureAsync(
        OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists metadata-only evidence for a statutory discount draft.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountEvidenceListResult?> ListAsync(
        OperatorConsoleStatutoryDiscountEvidenceListQuery query,
        CancellationToken cancellationToken);
}

