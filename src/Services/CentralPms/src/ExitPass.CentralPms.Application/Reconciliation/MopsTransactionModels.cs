namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Command to import a MoPS continuity transaction as reconciliation evidence.
/// </summary>
public sealed record ImportMopsTransactionCommand(
    Guid SiteId,
    Guid? SiteGroupId,
    Guid? PaymentRailId,
    Guid? VendorSystemId,
    Guid? ParkingSessionId,
    Guid? LaneId,
    string SourceSystemCode,
    string? SourceTransactionRef,
    string? SourceBatchRef,
    string? CollectionReference,
    string? CurrencyCode,
    decimal? Amount,
    string? PaymentMethodLabel,
    string ContinuityReasonCode,
    DateTimeOffset CapturedAt,
    string? EvidenceRef,
    string? EvidenceHash,
    Guid? ActorUserId,
    Guid? ImportedByServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Query to list imported MoPS continuity transactions.
/// </summary>
public sealed record ListMopsTransactionsQuery(int Limit, Guid? SiteId, string? SourceSystemCode);

/// <summary>
/// Query to read a single MoPS continuity transaction.
/// </summary>
public sealed record ReadMopsTransactionQuery(Guid MopsTransactionRecordId);

/// <summary>
/// Result of a MoPS import command.
/// </summary>
public sealed record MopsImportResult(
    Guid MopsTransactionRecordId,
    Guid ReconciliationRunId,
    Guid ReconciliationItemId,
    string RecordStatus,
    string RunCode,
    bool WasDuplicate,
    Guid CorrelationId);

/// <summary>
/// MoPS transaction read model.
/// </summary>
public sealed record MopsTransactionRecord(
    Guid MopsTransactionRecordId,
    Guid? ReconciliationRunId,
    Guid? ReconciliationItemId,
    Guid SiteId,
    Guid? SiteGroupId,
    Guid? PaymentRailId,
    Guid? VendorSystemId,
    Guid? ParkingSessionId,
    Guid? LaneId,
    string SourceSystemCode,
    string? SourceTransactionRef,
    string? SourceBatchRef,
    string? CollectionReference,
    string? CurrencyCode,
    decimal? Amount,
    string? PaymentMethodLabel,
    string ContinuityReasonCode,
    string RecordStatus,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ImportedAt,
    string? EvidenceRef,
    Guid? CorrelationId);
