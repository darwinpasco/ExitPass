namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only repository for Operator Console statutory discount policy resolution.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository
{
    /// <summary>
    /// Resolves a production-safe policy for the requested site and entitlement type.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountPolicyResolutionReadResult> ResolveAsync(
        OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest request,
        CancellationToken cancellationToken);
}
