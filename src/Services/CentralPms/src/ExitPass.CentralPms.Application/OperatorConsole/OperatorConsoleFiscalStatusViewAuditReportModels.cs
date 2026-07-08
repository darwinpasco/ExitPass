namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Query for read-only fiscal status view-audit report rows.
/// </summary>
public sealed record OperatorConsoleFiscalStatusViewAuditReportQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorUserId,
    Guid? FiscalIssuanceReferenceId,
    string? ResultClass,
    Guid? CorrelationIdFilter,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Read-only fiscal status view-audit report result.
/// </summary>
public sealed record OperatorConsoleFiscalStatusViewAuditReportResult(
    IReadOnlyList<OperatorConsoleFiscalStatusViewAuditReportItemResult> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Safe fiscal status view-audit report row.
/// </summary>
public sealed record OperatorConsoleFiscalStatusViewAuditReportItemResult(
    Guid ActionLogEntryId,
    DateTimeOffset ActionTimestamp,
    string ActionCode,
    string ResultClass,
    Guid OperatorUserId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid FiscalIssuanceReferenceId,
    Guid CorrelationId,
    string? SafeDenialOrErrorPosture,
    string? SourceModule);
