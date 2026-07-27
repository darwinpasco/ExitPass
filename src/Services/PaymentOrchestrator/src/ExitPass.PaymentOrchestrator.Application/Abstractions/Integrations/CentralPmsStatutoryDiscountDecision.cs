namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Central PMS statutory-discount decision request fields accepted from WebPay after server-side normalization.
/// </summary>
public sealed record CentralPmsStatutoryDiscountDecisionRequest(
    Guid RequestReference,
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
    IReadOnlyList<CentralPmsStatutoryDiscountEvidenceReference>? EvidenceReferences,
    bool RequesterAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    Guid? OriginalTariffSnapshotId);

/// <summary>
/// Metadata-only evidence reference forwarded to Central PMS.
/// </summary>
public sealed record CentralPmsStatutoryDiscountEvidenceReference(
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus);

/// <summary>
/// Durable Central PMS statutory-discount decision readback used by WebPay orchestration.
/// </summary>
public sealed record CentralPmsStatutoryDiscountDecision(
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
    long? VatExclusiveBasisAmountMinorUnits,
    long? VatAmountMinorUnits,
    string? VatTreatment,
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
    string? SafeErrorCode,
    string DecisionCommandStatus,
    string? DecisionResultStatus,
    bool DecisionRetryable,
    string DecisionRecoveryClassification,
    string? DecisionRecoveryAction,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId,
    bool ApplicationRequested,
    string ApplicationCommandStatus,
    string ApplicationResultClassification,
    string? ApplicationSemanticHashSourceVersion,
    bool ApplicationRetryable,
    string ApplicationRecoveryClassification,
    string? ApplicationRecoveryAction,
    string OverallResultClassification,
    bool OneShotComplete,
    Guid? SiteId,
    Guid? SiteGroupId,
    bool PayableBasisReady,
    string PayableBasisReadinessStatus,
    string? PayableBasisReadinessAction);
