using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryDiscounts;

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
    private readonly IOperatorConsoleStatutoryDiscountReadService _readService;
    private readonly IOperatorConsoleStatutoryDiscountEvidenceRepository _evidenceRepository;
    private readonly IStatutoryDiscountStagedCommandService _stagedCommandService;

    /// <summary>
    /// Creates an Operator Console statutory discount validation decision service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDecisionService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountDecisionWriter decisionWriter,
        IOperatorConsoleStatutoryDiscountReadService readService,
        IOperatorConsoleStatutoryDiscountEvidenceRepository evidenceRepository,
        IStatutoryDiscountStagedCommandService stagedCommandService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _decisionWriter = decisionWriter ?? throw new ArgumentNullException(nameof(decisionWriter));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _evidenceRepository = evidenceRepository ?? throw new ArgumentNullException(nameof(evidenceRepository));
        _stagedCommandService = stagedCommandService ?? throw new ArgumentNullException(nameof(stagedCommandService));
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

        if (!command.CanonicalDecisionAlreadyHandled)
        {
            return await DecideWithCanonicalDecisionAsync(
                    command,
                    decision,
                    targetStatus,
                    persistedEvaluation,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<OperatorConsoleStatutoryDiscountDecisionResult> DecideWithCanonicalDecisionAsync(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        string decision,
        string targetStatus,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        CancellationToken cancellationToken)
    {
        var detail = await _readService.GetDraftAsync(
                new OperatorConsoleStatutoryDiscountDraftDetailQuery(command.DraftId, command.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);

        if (detail is null)
        {
            return NotFoundResult(command, persistedEvaluation, decision);
        }

        var precheck = PrecheckDecisionability(command, detail, decision, targetStatus);
        if (precheck is not null)
        {
            return ResultFromPrecheck(command, persistedEvaluation, detail, decision, precheck);
        }

        if (detail.StatutoryDiscountDecisionCommandId.HasValue &&
            IsTerminalStatus(detail.ValidationStatus))
        {
            var existingDecision = await _stagedCommandService.GetDecisionAsync(
                    detail.StatutoryDiscountDecisionCommandId.Value,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existingDecision is not null &&
                IsSameTerminalDecision(existingDecision.DecisionResultStatus, targetStatus))
            {
                return ResultFromExistingCanonical(command, persistedEvaluation, detail, decision, existingDecision);
            }
        }

        var evidence = await _evidenceRepository.ListAsync(command.DraftId, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        var canonicalCommand = ToDecisionV2Command(command, detail, evidence, decision);
        var start = await _stagedCommandService.CreateOrResolveDecisionAsync(canonicalCommand, cancellationToken)
            .ConfigureAwait(false);

        if (start.SemanticConflict)
        {
            throw new OperatorConsoleStatutoryDiscountDecisionConflictException(
                command.DraftId,
                "CANONICAL_SEMANTIC_CONFLICT",
                decision);
        }

        if (start.Record is null)
        {
            return ResultFromCanonicalUnavailable(command, persistedEvaluation, detail, decision, start.SafeErrorCode);
        }

        if (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Completed)
        {
            if (!IsSameTerminalDecision(start.Record.DecisionResultStatus, targetStatus))
            {
                throw new OperatorConsoleStatutoryDiscountDecisionConflictException(
                    command.DraftId,
                    start.Record.DecisionResultStatus,
                    decision);
            }

            return ResultFromExistingCanonical(command, persistedEvaluation, detail, decision, start.Record);
        }

        if (start.Existing &&
            (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Received or StatutoryDiscountDecisionV2CommandStates.Processing) &&
            !string.Equals(start.Record.IdempotencyKey, canonicalCommand.IdempotencyKey, StringComparison.Ordinal))
        {
            return ResultFromCanonicalUnavailable(
                command,
                persistedEvaluation,
                detail,
                decision,
                "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS");
        }

        var processing = await _stagedCommandService.MarkDecisionProcessingAsync(
                start.Record.StatutoryDiscountDecisionCommandId,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        OperatorConsoleStatutoryDiscountDecisionPersistenceResult persistedDecision;
        try
        {
            persistedDecision = await _decisionWriter.PersistAsync(
                    new OperatorConsoleStatutoryDiscountDecisionPersistenceCommand(
                        command.DraftId,
                        decision,
                        targetStatus,
                        NormalizeOptional(command.DecisionReasonCode),
                        command.UserId,
                        command.CorrelationId),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await _stagedCommandService.RecordDecisionFailureAsync(
                    processing.StatutoryDiscountDecisionCommandId,
                    retryable: true,
                    "OPERATOR_CONSOLE_LEGACY_DECISION_TEMPORARILY_UNAVAILABLE",
                    command.CorrelationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        var completed = await CompleteCanonicalDecisionAsync(
                processing.StatutoryDiscountDecisionCommandId,
                command,
                detail,
                persistedDecision,
                targetStatus,
                cancellationToken)
            .ConfigureAwait(false);

        return ToResult(persistedEvaluation, persistedDecision, completed.StatutoryDiscountDecisionCommandId);
    }

    private async Task<StatutoryDiscountDecisionV2Record> CompleteCanonicalDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        OperatorConsoleStatutoryDiscountDecisionPersistenceResult persistedDecision,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        if (persistedDecision.DecisionAccepted &&
            string.Equals(persistedDecision.CurrentValidationStatus, "APPROVED", StringComparison.Ordinal))
        {
            return await _stagedCommandService.CompleteDecisionApprovedAsync(
                    statutoryDiscountDecisionCommandId,
                    persistedDecision.DraftId ?? command.DraftId,
                    detail.OriginalTariffSnapshotId,
                    detail.StatutoryDiscountPolicyId,
                    fallbackPolicyReferenceId: null,
                    detail.PolicyResolutionBasis,
                    !string.IsNullOrWhiteSpace(detail.OrdinanceReference),
                    ToTariffFacts(detail),
                    persistedDecision.DecisionReasonCode,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (persistedDecision.DecisionAccepted &&
            string.Equals(persistedDecision.CurrentValidationStatus, "REJECTED", StringComparison.Ordinal))
        {
            return await _stagedCommandService.CompleteDecisionRejectedAsync(
                    statutoryDiscountDecisionCommandId,
                    persistedDecision.DecisionReasonCode,
                    safeErrorCode: null,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await _stagedCommandService.RecordDecisionFailureAsync(
                statutoryDiscountDecisionCommandId,
                retryable: false,
                persistedDecision.ErrorCode ?? persistedDecision.IneligibilityReason ?? $"OPERATOR_CONSOLE_DECISION_NOT_{targetStatus}",
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static OperatorConsoleStatutoryDiscountDecisionResult ToResult(
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        OperatorConsoleStatutoryDiscountDecisionPersistenceResult persistedDecision,
        Guid? statutoryDiscountDecisionCommandId) =>
        new(
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
            persistedEvaluation.CorrelationId,
            statutoryDiscountDecisionCommandId);

    private static StatutoryDiscountDecisionV2Command ToDecisionV2Command(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        OperatorConsoleStatutoryDiscountEvidenceListResult evidence,
        string decision) =>
        new(
            detail.DraftId,
            StatutoryDiscountSourceChannels.OperatorConsole,
            detail.ParkingSessionId,
            detail.SiteId,
            detail.SiteGroupId,
            detail.TicketReference,
            detail.PlateNumber,
            NormalizeRequired(detail.EntitlementType, nameof(detail.EntitlementType)),
            new StatutoryDiscountDecisionV2BeneficiaryMetadata(
                BeneficiaryReference: null,
                NormalizeRequired(detail.EntitlementType, nameof(detail.EntitlementType)),
                ClaimantRole: null,
                BeneficiaryCount: 1),
            HasIdentityFacts(detail)
                ? new StatutoryDiscountDecisionV2IdentityMetadata(
                    detail.IdDocumentType,
                    detail.IssuingAuthority,
                    detail.ExpiryDate,
                    detail.MaskedIdReference,
                    IdentityReferenceHash: null)
                : null,
            evidence.Items.Select(item => new StatutoryDiscountDecisionV2EvidenceReference(
                    item.EvidenceType,
                    item.CaptureMethod,
                    item.StorageReference,
                    item.ReferenceNumberMasked,
                    item.VerificationStatus,
                    VerificationReference: null,
                    VerifiedAt: null))
                .ToArray(),
            new StatutoryDiscountDecisionV2AttestationFacts(
                detail.RequesterAttestation ?? true,
                AttestationReference: null,
                detail.DecisionReasonCode,
                command.ReviewerAttestation),
            detail.RequestedByUserId ?? command.UserId,
            command.UserId,
            command.OperatorDeviceBindingId,
            command.OperatorShiftId,
            new StatutoryDiscountDecisionV2DecisionFacts(
                decision,
                NormalizeOptional(command.DecisionReasonCode),
                SafeErrorCode: null),
            PolicyResolutionReferenceId: detail.StatutoryDiscountPolicyId,
            AppliedPolicyReferenceId: detail.StatutoryDiscountPolicyId,
            FallbackPolicyReferenceId: null,
            detail.PolicyResolutionBasis,
            !string.IsNullOrWhiteSpace(detail.OrdinanceReference),
            detail.OriginalTariffSnapshotId,
            ToTariffFacts(detail),
            DeriveLegacyDecisionStageIdempotencyKey(command.IdempotencyKey, detail.ParkingSessionId),
            command.CorrelationId);

    private static StatutoryDiscountDecisionV2TariffFacts? ToTariffFacts(
        OperatorConsoleStatutoryDiscountDraftDetailResult detail) =>
        new(
            detail.OriginalAmountMinorUnits,
            detail.VatExclusiveAmountMinorUnits,
            detail.VatAmountMinorUnits,
            detail.StatutoryDiscountAmountMinorUnits,
            detail.FinalPayableAmountMinorUnits ?? detail.PayableAmountMinorUnits,
            detail.CurrencyCode);

    private static string DeriveLegacyDecisionStageIdempotencyKey(string idempotencyKey, Guid parkingSessionId)
    {
        var source = $"operator-console-statutory-discount-decision-v2:{parkingSessionId:N}:{idempotencyKey.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"operator-console-decision-v2:sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string? PrecheckDecisionability(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        string decision,
        string targetStatus)
    {
        if (detail.ValidationStatus == targetStatus)
        {
            return null;
        }

        if (detail.ValidationStatus is "APPROVED" or "REJECTED")
        {
            throw new OperatorConsoleStatutoryDiscountDecisionConflictException(
                command.DraftId,
                detail.ValidationStatus,
                decision);
        }

        if (detail.ValidationStatus is not ("REQUESTED" or "PENDING_OPERATOR_REVIEW"))
        {
            return "STATUTORY_DISCOUNT_DRAFT_NOT_DECISIONABLE";
        }

        if (detail.RequestedByUserId == command.UserId)
        {
            return "REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT";
        }

        if (targetStatus == "APPROVED" &&
            detail.EvidenceRequired &&
            !detail.EvidenceCaptured)
        {
            return "EVIDENCE_REQUIRED_NOT_CAPTURED";
        }

        return null;
    }

    private static OperatorConsoleStatutoryDiscountDecisionResult ResultFromPrecheck(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        string decision,
        string errorCode) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            detail.DraftId,
            detail.ParkingSessionId,
            detail.EntitlementType,
            detail.ValidationStatus,
            detail.ValidationStatus,
            decision,
            NormalizeOptional(command.DecisionReasonCode) ?? detail.DecisionReasonCode,
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: errorCode == "STATUTORY_DISCOUNT_DRAFT_NOT_DECISIONABLE"
                ? "DRAFT_NOT_DECISIONABLE"
                : errorCode,
            ErrorCode: errorCode,
            persistedEvaluation.CorrelationId,
            detail.StatutoryDiscountDecisionCommandId);

    private static OperatorConsoleStatutoryDiscountDecisionResult ResultFromExistingCanonical(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        string decision,
        StatutoryDiscountDecisionV2Record canonical) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DecisionAccepted: true,
            DecisionPersisted: true,
            detail.DraftId,
            detail.ParkingSessionId,
            detail.EntitlementType,
            detail.ValidationStatus,
            ToValidationStatus(canonical.DecisionResultStatus),
            decision,
            canonical.ReasonCode ?? NormalizeOptional(command.DecisionReasonCode) ?? detail.DecisionReasonCode,
            AlreadyDecided: true,
            DecisionChanged: false,
            IneligibilityReason: null,
            ErrorCode: null,
            persistedEvaluation.CorrelationId,
            canonical.StatutoryDiscountDecisionCommandId);

    private static OperatorConsoleStatutoryDiscountDecisionResult ResultFromCanonicalUnavailable(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        string decision,
        string? errorCode) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            detail.DraftId,
            detail.ParkingSessionId,
            detail.EntitlementType,
            detail.ValidationStatus,
            detail.ValidationStatus,
            decision,
            NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: errorCode ?? "STATUTORY_DISCOUNT_DECISION_NOT_AVAILABLE",
            ErrorCode: errorCode ?? "STATUTORY_DISCOUNT_DECISION_NOT_AVAILABLE",
            persistedEvaluation.CorrelationId,
            detail.StatutoryDiscountDecisionCommandId);

    private static OperatorConsoleStatutoryDiscountDecisionResult NotFoundResult(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        string decision) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            command.DraftId,
            ParkingSessionId: null,
            EntitlementType: null,
            PreviousValidationStatus: null,
            CurrentValidationStatus: null,
            decision,
            NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "DRAFT_NOT_FOUND",
            ErrorCode: "DRAFT_NOT_FOUND",
            persistedEvaluation.CorrelationId);

    private static bool HasIdentityFacts(OperatorConsoleStatutoryDiscountDraftDetailResult detail) =>
        !string.IsNullOrWhiteSpace(detail.IdDocumentType) ||
        !string.IsNullOrWhiteSpace(detail.IssuingAuthority) ||
        detail.ExpiryDate.HasValue ||
        !string.IsNullOrWhiteSpace(detail.MaskedIdReference);

    private static bool IsTerminalStatus(string? status) =>
        status is "APPROVED" or "REJECTED";

    private static bool IsSameTerminalDecision(string decisionResultStatus, string targetStatus) =>
        decisionResultStatus switch
        {
            StatutoryDiscountDecisionV2ResultStates.Approved => targetStatus == "APPROVED",
            StatutoryDiscountDecisionV2ResultStates.Rejected => targetStatus == "REJECTED",
            _ => false
        };

    private static string ToValidationStatus(string decisionResultStatus) =>
        decisionResultStatus switch
        {
            StatutoryDiscountDecisionV2ResultStates.Approved => "APPROVED",
            StatutoryDiscountDecisionV2ResultStates.Rejected => "REJECTED",
            _ => decisionResultStatus
        };

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim().ToUpperInvariant();
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
