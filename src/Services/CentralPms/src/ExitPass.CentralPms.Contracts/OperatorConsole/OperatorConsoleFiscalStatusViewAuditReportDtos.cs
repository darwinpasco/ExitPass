namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Read-only fiscal status view-audit report response.
/// </summary>
public sealed record OperatorConsoleFiscalStatusViewAuditReportResponse(
    IReadOnlyList<OperatorConsoleFiscalStatusViewAuditReportItem> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Safe fiscal status view-audit report item.
/// </summary>
public sealed record OperatorConsoleFiscalStatusViewAuditReportItem(
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
