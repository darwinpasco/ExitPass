using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only Operator Console facade for fiscal issuance status visibility.
/// </summary>
public interface IOperatorConsoleFiscalIssuanceStatusService
{
    /// <summary>
    /// Evaluates/persists Operator Console view audit and returns safe fiscal status when access is allowed.
    /// </summary>
    Task<OperatorConsoleFiscalIssuanceStatusResult> GetAsync(
        OperatorConsoleFiscalIssuanceStatusQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Query for read-only Operator Console fiscal issuance status viewing.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceStatusQuery(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid FiscalIssuanceReferenceId,
    Guid CorrelationId);

/// <summary>
/// Result for read-only Operator Console fiscal issuance status viewing.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceStatusResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    FiscalIssuanceStatusReadModel? Status,
    Guid CorrelationId);
