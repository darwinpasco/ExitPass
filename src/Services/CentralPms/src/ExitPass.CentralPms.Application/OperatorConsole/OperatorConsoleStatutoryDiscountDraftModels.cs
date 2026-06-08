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
    bool EvidenceRequired,
    bool EvidenceReferenceCreated,
    Guid? EvidenceReferenceId,
    bool ReusedExistingDraft,
    OperatorConsoleResolvedStatutoryDiscountPolicy? Policy,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId,
    string PolicyReadinessClassification = OperatorConsolePolicyReadinessClassifications.NotReady,
    bool RequiresManualReview = false,
    string? PolicyReadinessReason = null,
    string? OperatorMessage = null);

/// <summary>
/// Persistence command for the privacy-minimized statutory discount validation draft row.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftPersistenceCommand(
    Guid ParkingSessionId,
    string EntitlementType,
    bool EvidenceRequired,
    string? ReasonCode,
    Guid RequestedByUserId,
    Guid CorrelationId,
    OperatorConsoleResolvedStatutoryDiscountPolicy Policy);

/// <summary>
/// Persistence result for a statutory discount validation draft row.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftPersistenceResult(
    Guid DraftId,
    string ValidationStatus,
    bool Persisted,
    bool ReusedExistingDraft,
    bool EvidenceRequired,
    bool EvidenceReferenceCreated,
    Guid? EvidenceReferenceId,
    OperatorConsoleResolvedStatutoryDiscountPolicy? Policy);

/// <summary>
/// Raised when an existing statutory discount validation blocks a new draft but is not reusable as an active draft.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftAlreadyExistsException : InvalidOperationException
{
    /// <summary>
    /// Creates a duplicate statutory discount draft exception.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDraftAlreadyExistsException(Guid parkingSessionId, string entitlementType)
        : base("An active statutory discount validation already exists for the parking session and entitlement type.")
    {
        ParkingSessionId = parkingSessionId;
        EntitlementType = entitlementType;
    }

    /// <summary>
    /// Parking session blocked by an existing statutory discount validation.
    /// </summary>
    public Guid ParkingSessionId { get; }

    /// <summary>
    /// Entitlement type blocked by an existing statutory discount validation.
    /// </summary>
    public string EntitlementType { get; }
}
