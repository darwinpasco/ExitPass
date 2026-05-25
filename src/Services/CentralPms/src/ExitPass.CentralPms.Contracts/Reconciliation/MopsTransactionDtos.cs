namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for importing one MoPS continuity transaction record.
/// </summary>
public sealed record ImportMopsTransactionRequest(
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
    Guid? ImportedByServiceIdentityId);

/// <summary>
/// Response body returned after a MoPS transaction import is accepted or replayed.
/// </summary>
public sealed record ImportMopsTransactionResponse(
    Guid MopsTransactionRecordId,
    Guid ReconciliationRunId,
    Guid ReconciliationItemId,
    string RecordStatus,
    string RunCode,
    bool WasDuplicate,
    Guid CorrelationId);

/// <summary>
/// Paged list of imported MoPS transaction records.
/// </summary>
public sealed record MopsTransactionsResponse(IReadOnlyList<MopsTransactionSummary> Records);

/// <summary>
/// MoPS transaction read model.
/// </summary>
public sealed record MopsTransactionSummary(
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
