namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Query for read-only fiscal void action audit review rows.
/// </summary>
public sealed record OperatorConsoleFiscalVoidActionAuditReportQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorUserId,
    Guid? FiscalIssuanceReferenceId,
    string? FiscalDocumentNumber,
    string? ResultClass,
    Guid? CorrelationIdFilter,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Read-only fiscal void action audit review result.
/// </summary>
public sealed record OperatorConsoleFiscalVoidActionAuditReportResult(
    IReadOnlyList<OperatorConsoleFiscalVoidActionAuditReportItemResult> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Safe fiscal void action audit review row.
/// </summary>
public sealed record OperatorConsoleFiscalVoidActionAuditReportItemResult(
    Guid ActionLogEntryId,
    DateTimeOffset ActionTimestamp,
    string ActionCode,
    string ResultClass,
    Guid OperatorUserId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid FiscalIssuanceReferenceId,
    string? FiscalDocumentNumber,
    Guid? PosServerFiscalDocumentId,
    string? ReasonCode,
    string? ReasonText,
    Guid CorrelationId,
    string? OperatorActionRequestId,
    string? PosServerResultClassification,
    string? SafeDenialOrErrorPosture,
    string? SourceModule,
    bool? PaymentFinalityChanged,
    bool? ExitAuthorizationIssued,
    bool? GateBehaviorTriggered,
    bool? RefundOrReversalCreated,
    bool? HikCentralCalled,
    bool? PaymentProviderCalled,
    bool? RenderingGenerated,
    bool? ReplacementFiscalDocumentCreated,
    bool? NewFiscalNumberAllocated,
    bool? FiscalSequenceChangedByCentralPms);
