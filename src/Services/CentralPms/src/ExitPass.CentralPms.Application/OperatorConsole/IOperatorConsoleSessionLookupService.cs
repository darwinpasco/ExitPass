namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated read-only Operator Console parking session lookup service.
/// </summary>
public interface IOperatorConsoleSessionLookupService
{
    /// <summary>
    /// Evaluates Operator Console access and, when allowed, reads parking session context.
    /// </summary>
    Task<OperatorConsoleSessionLookupResult> LookupAsync(
        OperatorConsoleSessionLookupCommand command,
        CancellationToken cancellationToken);
}
