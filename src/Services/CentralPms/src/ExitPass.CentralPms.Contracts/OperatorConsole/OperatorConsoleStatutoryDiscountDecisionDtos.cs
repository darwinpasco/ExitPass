namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for approving or rejecting an Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionRequest(
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
    Guid CorrelationId);

/// <summary>
/// Response body for an access-gated Operator Console statutory discount validation decision.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDecisionResponse(
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
