namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for an access-gated Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftCommand(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string IdDocumentType,
    string IssuingAuthority,
    DateOnly? ExpiryDate,
    string MaskedIdReference,
    string? EntitlementFingerprint,
    bool EvidenceCaptureRequested,
    string? EvidenceAccessIntent,
    bool OperatorAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Result for an access-gated Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool DraftAccepted,
    bool DraftPersisted,
    Guid? DraftId,
    Guid? ParkingSessionId,
    string? EntitlementType,
    string? ValidationStatus,
    bool EvidenceCaptureRequired,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId);

/// <summary>
/// Persistence command for the privacy-minimized statutory discount validation draft row.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftPersistenceCommand(
    Guid ParkingSessionId,
    string EntitlementType,
    bool EvidenceRequired,
    string? ReasonCode,
    Guid RequestedByUserId,
    Guid CorrelationId);

/// <summary>
/// Persistence result for a statutory discount validation draft row.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftPersistenceResult(
    Guid DraftId,
    string ValidationStatus,
    bool Persisted);
