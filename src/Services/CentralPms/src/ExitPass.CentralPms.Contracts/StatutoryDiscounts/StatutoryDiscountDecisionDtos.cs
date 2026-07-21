namespace ExitPass.CentralPms.Contracts.StatutoryDiscounts;

/// <summary>
/// Channel-neutral Central PMS statutory-discount decision request.
/// </summary>
public sealed record StatutoryDiscountDecisionRequest(
    Guid RequestReference,
    string SourceChannel,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string IdDocumentType,
    string IssuingAuthority,
    DateOnly? ExpiryDate,
    string MaskedIdReference,
    bool EvidenceCaptureRequested,
    IReadOnlyList<StatutoryDiscountEvidenceReferenceRequest>? EvidenceReferences,
    Guid ActorUserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    bool RequesterAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    string? Decision,
    string? DecisionReasonCode,
    Guid? ReviewerUserId,
    bool ReviewerAttestation,
    bool ApplyPayableBasis,
    Guid? OriginalTariffSnapshotId);

/// <summary>
/// Metadata-only evidence reference accepted by the shared statutory-discount facade.
/// </summary>
public sealed record StatutoryDiscountEvidenceReferenceRequest(
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus);

/// <summary>
/// Canonical Central PMS statutory-discount result and readback response.
/// </summary>
public sealed record StatutoryDiscountDecisionResponse(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid? StatutoryDiscountValidationId,
    Guid ParkingSessionId,
    string SourceChannel,
    string EntitlementType,
    string DecisionStatus,
    string? PolicyResolutionBasis,
    Guid? AppliedPolicyReferenceId,
    Guid? FallbackPolicyReferenceId,
    bool LocalOrdinanceApplied,
    long? GrossAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetPayableAmountMinorUnits,
    string? Currency,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? ReasonCode,
    string? ErrorCode,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? AppliedAt,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string CommandStatus,
    string ClientResultStatus,
    string ResultClassification,
    string SemanticHashSourceVersion,
    bool Retryable,
    string RecoveryClassification,
    string? RecoveryAction,
    string? SafeErrorCode);
