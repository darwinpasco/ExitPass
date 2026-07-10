using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Operator Console facade for controlled fiscal document void initiation.
/// </summary>
public interface IOperatorConsoleFiscalIssuanceVoidService
{
    /// <summary>
    /// Evaluates Operator Console access, persists action-log posture, and invokes the fiscal void command when allowed.
    /// </summary>
    Task<OperatorConsoleFiscalIssuanceVoidResult> VoidAsync(
        OperatorConsoleFiscalIssuanceVoidCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Operator Console fiscal void command.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceVoidCommand(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid FiscalIssuanceReferenceId,
    Guid OperatorActionRequestId,
    string? ReasonCode,
    string? ReasonText,
    string? ConfirmationText,
    Guid CorrelationId);

/// <summary>
/// Operator Console fiscal void result.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceVoidResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    FiscalIssuanceVoidCommandResponse? VoidResult,
    Guid CorrelationId);
