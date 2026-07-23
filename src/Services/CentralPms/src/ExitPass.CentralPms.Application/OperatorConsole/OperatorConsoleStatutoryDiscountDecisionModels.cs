namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for an access-gated Operator Console statutory discount validation decision.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionCommand(
    Guid DraftId,
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    string Decision,
    string? DecisionReasonCode,
    string? DecisionNotes,
    bool ReviewerAttestation,
    string IdempotencyKey,
    Guid CorrelationId,
    bool CanonicalDecisionAlreadyHandled = false);

/// <summary>
/// Result for an access-gated Operator Console statutory discount validation decision.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool DecisionAccepted,
    bool DecisionPersisted,
    Guid? DraftId,
    Guid? ParkingSessionId,
    string? EntitlementType,
    string? PreviousValidationStatus,
    string? CurrentValidationStatus,
    string? Decision,
    string? DecisionReasonCode,
    bool AlreadyDecided,
    bool DecisionChanged,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId,
    Guid? StatutoryDiscountDecisionCommandId = null);

/// <summary>
/// Persistence command for a statutory discount validation decision state transition.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionPersistenceCommand(
    Guid DraftId,
    string Decision,
    string TargetValidationStatus,
    string? DecisionReasonCode,
    Guid DecidedByUserId,
    Guid CorrelationId);

/// <summary>
/// Persistence result for a statutory discount validation decision state transition.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionPersistenceResult(
    bool Found,
    bool DecisionAccepted,
    bool DecisionPersisted,
    Guid? DraftId,
    Guid? ParkingSessionId,
    string? EntitlementType,
    string? PreviousValidationStatus,
    string? CurrentValidationStatus,
    string? Decision,
    string? DecisionReasonCode,
    bool AlreadyDecided,
    bool DecisionChanged,
    string? IneligibilityReason,
    string? ErrorCode);

/// <summary>
/// Safe stored facts used to construct the canonical staged decision-v2 command for a legacy Operator Console draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionCanonicalFacts(
    Guid DraftId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string? ValidationStatus,
    Guid? StatutoryDiscountDecisionCommandId,
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? MaskedIdReference,
    bool? RequesterAttestation,
    string? AttestationNotes,
    bool EvidenceRequired,
    bool EvidenceCaptured,
    Guid? RequestedByUserId,
    string? ReasonCode,
    Guid? AppliedPolicyReferenceId,
    string? PolicyResolutionBasis,
    bool LocalOrdinanceApplied,
    Guid? OriginalTariffSnapshotId,
    long? OriginalAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? CurrencyCode,
    IReadOnlyList<OperatorConsoleStatutoryDiscountDecisionEvidenceFact> EvidenceReferences);

/// <summary>
/// Safe evidence metadata included in canonical decision-v2 semantics.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionEvidenceFact(
    string EvidenceType,
    string CaptureMethod,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus);

/// <summary>
/// Raised when a statutory discount validation already has a conflicting terminal decision.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDecisionConflictException : InvalidOperationException
{
    /// <summary>
    /// Creates a conflicting statutory discount decision exception.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDecisionConflictException(Guid draftId, string currentStatus, string requestedDecision)
        : base("The statutory discount validation already has a conflicting terminal decision.")
    {
        DraftId = draftId;
        CurrentStatus = currentStatus;
        RequestedDecision = requestedDecision;
    }

    /// <summary>
    /// Draft blocked by the existing terminal decision.
    /// </summary>
    public Guid DraftId { get; }

    /// <summary>
    /// Current terminal validation status.
    /// </summary>
    public string CurrentStatus { get; }

    /// <summary>
    /// Requested review decision.
    /// </summary>
    public string RequestedDecision { get; }
}
