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

    /// <summary>
    /// Resolves an operator-friendly fiscal lookup query, evaluates/persists view audit, and returns safe status when found.
    /// </summary>
    Task<OperatorConsoleFiscalIssuanceStatusResult> LookupAsync(
        OperatorConsoleFiscalIssuanceLookupQuery query,
        CancellationToken cancellationToken) =>
        Guid.TryParse(query.Query.Trim(), out var fiscalIssuanceReferenceId)
            ? GetAsync(
                new OperatorConsoleFiscalIssuanceStatusQuery(
                    query.UserId,
                    query.OperatorDeviceBindingId,
                    query.SiteId,
                    query.SiteGroupId,
                    query.OperatorShiftId,
                    fiscalIssuanceReferenceId,
                    query.CorrelationId),
                cancellationToken)
            : Task.FromResult(new OperatorConsoleFiscalIssuanceStatusResult(
                AccessEvaluationId: Guid.Empty,
                AccessAllowed: true,
                AccessDecision: "ALLOWED",
                AccessDenialReasons: Array.Empty<string>(),
                AccessPersisted: false,
                Status: null,
                query.CorrelationId,
                SafeErrorCode: "FISCAL_ISSUANCE_LOOKUP_NOT_FOUND",
                SafeErrorPosture: "Fiscal status lookup did not match a fiscal issuance reference."));
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
/// Query for operator-friendly fiscal issuance status lookup.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceLookupQuery(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    string Query,
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
    Guid CorrelationId,
    string? SafeErrorCode = null,
    string? SafeErrorPosture = null,
    bool LookupAmbiguous = false);
