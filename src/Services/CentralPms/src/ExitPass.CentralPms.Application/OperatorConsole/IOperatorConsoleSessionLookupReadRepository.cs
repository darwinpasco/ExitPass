namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Reads parking session context for Operator Console session lookup.
/// </summary>
public interface IOperatorConsoleSessionLookupReadRepository
{
    /// <summary>
    /// Finds a parking session by an exact supported lookup identifier.
    /// </summary>
    Task<OperatorConsoleSessionReadModel?> FindAsync(
        OperatorConsoleSessionLookupReadRequest request,
        CancellationToken cancellationToken);
}
