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
    Guid? OriginalTariffSnapshotId,
    bool? BeneficiaryResidencySatisfied = null);

/// <summary>
/// Channel-safe statutory parking local-ordinance availability request.
/// </summary>
public sealed record StatutoryDiscountParkingAvailabilityRequestDto(
    Guid RequestReference,
    Guid ParkingSessionId,
    string? RequestedEntitlementType,
    bool? BeneficiaryResidencySatisfied);

/// <summary>
/// Channel-safe statutory parking local-ordinance availability response.
/// </summary>
public sealed record StatutoryDiscountParkingAvailabilityResponse(
    Guid RequestReference,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? JurisdictionId,
    string? JurisdictionCode,
    string? JurisdictionDisplayName,
    string AvailabilityStatus,
    bool StatutoryParkingBenefitAvailable,
    IReadOnlyList<string> CoveredEntitlementTypes,
    string? RequestedEntitlementType,
    Guid? PolicyVersionId,
    string? PolicyCode,
    string? PolicyVersion,
    string? OrdinanceNumber,
    string? OrdinanceTitle,
    string? PolicyDisplayName,
    string? VerificationStatus,
    string? PublicationStatus,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? ResidencyRequirement,
    IReadOnlyList<StatutoryDiscountPolicyEvidenceRequirementDto> RequiredEvidenceTypes,
    string? ParkingServiceApplicability,
    string? BenefitEffectClassification,
    string? BenefitEffectSupportStatus,
    bool? OfficialSourceAvailable,
    bool? OrdinanceTextAvailable,
    bool? OrdinanceNumberAvailable,
    string? SafeReasonCode,
    bool Retryable,
    string RemediationAction,
    Guid CorrelationId);

/// <summary>
/// Safe evidence requirement metadata for an available statutory parking policy.
/// </summary>
public sealed record StatutoryDiscountPolicyEvidenceRequirementDto(
    string EvidenceType,
    string RequirementStatus,
    string SafeRequirementLabel,
    string? SafeRequirementNotes);

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
/// <param name="SiteId">Durable site identifier linked to the reviewed service-channel decision, when available.</param>
/// <param name="SiteGroupId">Durable site-group identifier linked to the reviewed service-channel decision, when available.</param>
/// <param name="VatExclusiveBasisAmountMinorUnits">Authoritative VAT-exclusive statutory-discount basis in minor units, when available.</param>
/// <param name="VatAmountMinorUnits">Authoritative VAT amount in minor units, when available.</param>
/// <param name="VatTreatment">Authoritative VAT treatment classification for the statutory-discount basis, when available.</param>
/// <param name="PayableBasisReady">Indicates whether the statutory-discount payable basis is durably applied and ready for payment-readiness checks.</param>
/// <param name="PayableBasisReadinessStatus">Channel-safe payable-basis readiness status derived from durable decision and application state.</param>
/// <param name="PayableBasisReadinessAction">Channel-safe next action for the current payable-basis readiness status, when applicable.</param>
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
    string? SafeErrorCode,
    string DecisionCommandStatus = "COMPLETED",
    string? DecisionResultStatus = null,
    bool DecisionRetryable = false,
    string DecisionRecoveryClassification = "NONE",
    string? DecisionRecoveryAction = null,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId = null,
    bool ApplicationRequested = false,
    string ApplicationCommandStatus = "NOT_REQUESTED",
    string ApplicationResultClassification = "NOT_REQUESTED",
    string? ApplicationSemanticHashSourceVersion = null,
    bool ApplicationRetryable = false,
    string ApplicationRecoveryClassification = "NONE",
    string? ApplicationRecoveryAction = null,
    string OverallResultClassification = "ACCEPTED",
    bool OneShotComplete = true,
    Guid? SiteId = null,
    Guid? SiteGroupId = null,
    long? VatExclusiveBasisAmountMinorUnits = null,
    long? VatAmountMinorUnits = null,
    string? VatTreatment = null,
    bool PayableBasisReady = false,
    string PayableBasisReadinessStatus = "NOT_READY",
    string? PayableBasisReadinessAction = null);
