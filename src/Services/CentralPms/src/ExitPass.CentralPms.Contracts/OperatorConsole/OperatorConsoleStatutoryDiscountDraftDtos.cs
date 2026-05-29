namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for drafting a statutory discount validation through the Operator Console.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftRequest(
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
/// Response body for an access-gated Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftResponse(
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
