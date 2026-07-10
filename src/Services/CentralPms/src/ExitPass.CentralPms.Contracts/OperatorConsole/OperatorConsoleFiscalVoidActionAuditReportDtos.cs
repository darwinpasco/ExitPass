namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Read-only fiscal void action audit review response.
/// </summary>
public sealed record OperatorConsoleFiscalVoidActionAuditReportResponse(
    IReadOnlyList<OperatorConsoleFiscalVoidActionAuditReportItem> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Safe fiscal void action audit review item.
/// </summary>
public sealed record OperatorConsoleFiscalVoidActionAuditReportItem(
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
