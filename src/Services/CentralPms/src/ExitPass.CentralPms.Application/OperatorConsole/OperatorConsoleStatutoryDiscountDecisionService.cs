namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console statutory discount validation decision service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before a statutory discount validation decision is written.
/// - Denied access does not read or mutate statutory discount validation drafts.
/// - This service never applies a discount or mutates tariff, payable, payment, provider, gate, coupon, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDecisionService : IOperatorConsoleStatutoryDiscountDecisionService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.DecideStatutoryDiscount;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleStatutoryDiscountDecisionWriter _decisionWriter;

    /// <summary>
    /// Creates an Operator Console statutory discount validation decision service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDecisionService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountDecisionWriter decisionWriter)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _decisionWriter = decisionWriter ?? throw new ArgumentNullException(nameof(decisionWriter));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDecisionResult> DecideAsync(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var decision = Validate(command);
        var targetStatus = decision == "APPROVE" ? "APPROVED" : "REJECTED";

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId,
                command.SiteGroupId,
                command.OperatorShiftId,
                WorkflowCode,
                ControlledActionCode,
                ParkingSessionId: null,
                EvidenceAccessIntent: null,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
        if (!persistedEvaluation.Allowed)
        {
            return DeniedResult(command, persistedEvaluation, decision);
        }

        var persistedDecision = await _decisionWriter.PersistAsync(
            new OperatorConsoleStatutoryDiscountDecisionPersistenceCommand(
                command.DraftId,
                decision,
                targetStatus,
                NormalizeOptional(command.DecisionReasonCode),
                command.UserId,
                command.CorrelationId),
            cancellationToken);

        return new OperatorConsoleStatutoryDiscountDecisionResult(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            persistedDecision.DecisionAccepted,
            persistedDecision.DecisionPersisted,
            persistedDecision.DraftId,
            persistedDecision.ParkingSessionId,
            persistedDecision.EntitlementType,
            persistedDecision.PreviousValidationStatus,
            persistedDecision.CurrentValidationStatus,
            persistedDecision.Decision,
            persistedDecision.DecisionReasonCode,
            persistedDecision.AlreadyDecided,
            persistedDecision.DecisionChanged,
            persistedDecision.IneligibilityReason,
            persistedDecision.ErrorCode,
            persistedEvaluation.CorrelationId);
    }

    private static OperatorConsoleStatutoryDiscountDecisionResult DeniedResult(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        string decision) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: false,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            DraftId: command.DraftId,
            ParkingSessionId: null,
            EntitlementType: null,
            PreviousValidationStatus: null,
            CurrentValidationStatus: null,
            Decision: decision,
            DecisionReasonCode: NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            persistedEvaluation.CorrelationId);

    private static string Validate(OperatorConsoleStatutoryDiscountDecisionCommand command)
    {
        ValidateGuid(command.DraftId, nameof(command.DraftId));
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }

        if (!command.ReviewerAttestation)
        {
            throw new ArgumentException("ReviewerAttestation must be true.", nameof(command.ReviewerAttestation));
        }

        var decision = Normalize(command.Decision);
        if (string.IsNullOrWhiteSpace(decision))
        {
            throw new ArgumentException("Decision is required.", nameof(command.Decision));
        }

        if (decision is not ("APPROVE" or "REJECT"))
        {
            throw new ArgumentException("Decision must be APPROVE or REJECT.", nameof(command.Decision));
        }

        if (decision == "REJECT" && string.IsNullOrWhiteSpace(command.DecisionReasonCode))
        {
            throw new ArgumentException("DecisionReasonCode is required for REJECT.", nameof(command.DecisionReasonCode));
        }

        return decision;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
