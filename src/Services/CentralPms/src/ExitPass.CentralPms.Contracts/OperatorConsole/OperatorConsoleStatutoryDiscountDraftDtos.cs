namespace ExitPass.CentralPms.Contracts.OperatorConsole;

using System.Text.Json;

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
    bool EvidenceRequired,
    bool EvidenceReferenceCreated,
    Guid? EvidenceReferenceId,
    bool ReusedExistingDraft,
    Guid? StatutoryDiscountPolicyId,
    Guid? ResolvedJurisdictionId,
    string? PolicyResolutionBasis,
    string? PolicyCode,
    string? PolicyName,
    string? LegalBasisReference,
    string? OrdinanceReference,
    string? NationalLawReference,
    string? VerificationStatus,
    string? BenefitType,
    int? FreeDurationMinutes,
    string? SucceedingHoursDiscountRule,
    string? DiscountBaseScope,
    string? StackingPolicy,
    JsonElement? PolicySnapshot,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId,
    string? PolicyReadinessClassification = null,
    bool RequiresManualReview = false,
    string? PolicyReadinessReason = null,
    string? OperatorMessage = null);
