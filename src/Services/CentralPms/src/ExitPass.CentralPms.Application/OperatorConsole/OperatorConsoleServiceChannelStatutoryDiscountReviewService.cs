using ExitPass.CentralPms.Application.StatutoryDiscounts;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console review linkage for service-channel statutory-discount decisions.
/// </summary>
public sealed class OperatorConsoleServiceChannelStatutoryDiscountReviewService
    : IOperatorConsoleServiceChannelStatutoryDiscountReviewService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ReviewActionCode = OperatorConsoleActionCodes.ViewStatutoryDiscountDraft;
    private const string DecideActionCode = OperatorConsoleActionCodes.DecideStatutoryDiscount;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IStatutoryDiscountServiceChannelReviewRepository _reviewRepository;
    private readonly IStatutoryDiscountStagedCommandService _stagedCommandService;

    public OperatorConsoleServiceChannelStatutoryDiscountReviewService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IStatutoryDiscountServiceChannelReviewRepository reviewRepository,
        IStatutoryDiscountStagedCommandService stagedCommandService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _reviewRepository = reviewRepository ?? throw new ArgumentNullException(nameof(reviewRepository));
        _stagedCommandService = stagedCommandService ?? throw new ArgumentNullException(nameof(stagedCommandService));
    }

    public async Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
        StatutoryDiscountServiceChannelReviewQueueQuery query,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(accessContext);

        var access = await EvaluateAndPersistAsync(accessContext, ReviewActionCode, ParkingSessionId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!access.Allowed)
        {
            throw new UnauthorizedAccessException("Operator Console service-channel statutory discount review access was denied.");
        }

        return await _reviewRepository.ListAsync(query with
        {
            SiteId = query.SiteId ?? access.SiteContext.SiteId,
            SiteGroupId = query.SiteGroupId ?? access.SiteContext.SiteGroupId,
            Page = query.Page <= 0 ? 1 : query.Page,
            PageSize = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 100),
            SourceChannel = NormalizeOptional(query.SourceChannel),
            EntitlementType = NormalizeOptional(query.EntitlementType),
            CorrelationId = access.CorrelationId
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        ValidateGuid(statutoryDiscountDecisionCommandId, nameof(statutoryDiscountDecisionCommandId));
        ArgumentNullException.ThrowIfNull(accessContext);

        var access = await EvaluateAndPersistAsync(accessContext, ReviewActionCode, ParkingSessionId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!access.Allowed)
        {
            throw new UnauthorizedAccessException("Operator Console service-channel statutory discount review access was denied.");
        }

        var detail = await _reviewRepository.GetAsync(statutoryDiscountDecisionCommandId, access.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        if (detail is null || !SiteAllowed(detail, access))
        {
            return null;
        }

        return detail;
    }

    public async Task<StatutoryDiscountServiceChannelReviewDecisionResult> DecideAsync(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedDecision = Validate(command);

        var accessContext = new OperatorConsoleReviewAccessContext(
            command.UserId,
            command.OperatorDeviceBindingId,
            command.OperatorShiftId,
            command.SiteId,
            command.SiteGroupId,
            command.CorrelationId,
            command.IdempotencyKey);
        var access = await EvaluateAndPersistAsync(accessContext, DecideActionCode, ParkingSessionId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!access.Allowed)
        {
            return Denied(command, access, requestedDecision);
        }

        var detail = await _reviewRepository.GetAsync(command.StatutoryDiscountDecisionCommandId, access.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return NotAccepted(command, access, requestedDecision, "STATUTORY_DISCOUNT_REVIEW_NOT_FOUND");
        }

        if (!SiteAllowed(detail, access))
        {
            return NotAccepted(command, access, requestedDecision, "OPERATOR_CONSOLE_SITE_SCOPE_DENIED", detail);
        }

        var canonical = await _stagedCommandService.GetDecisionAsync(command.StatutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
        if (canonical is null)
        {
            return NotAccepted(command, access, requestedDecision, "STATUTORY_DISCOUNT_DECISION_NOT_FOUND", detail);
        }

        var targetResult = requestedDecision == "APPROVE"
            ? StatutoryDiscountDecisionV2ResultStates.Approved
            : StatutoryDiscountDecisionV2ResultStates.Rejected;

        if (canonical.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Completed)
        {
            if (!string.Equals(canonical.DecisionResultStatus, targetResult, StringComparison.Ordinal))
            {
                return Conflict(command, access, requestedDecision, canonical, detail);
            }

            var terminalDetail = detail;
            if (string.Equals(detail.ReviewStatus, StatutoryDiscountServiceChannelReviewStatuses.PendingReview, StringComparison.Ordinal))
            {
                terminalDetail = await _reviewRepository.RecordReviewCompletionAsync(
                        canonical.StatutoryDiscountDecisionCommandId,
                        command.UserId,
                        command.OperatorDeviceBindingId,
                        command.OperatorShiftId,
                        access.EvaluationId,
                        requestedDecision,
                        canonical.ReasonCode ?? NormalizeOptional(command.DecisionReasonCode),
                        command.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Existing(command, access, requestedDecision, canonical, terminalDetail);
        }

        if (canonical.CommandStatus is not StatutoryDiscountDecisionV2CommandStates.AwaitingReview ||
            canonical.DecisionResultStatus is not StatutoryDiscountDecisionV2ResultStates.NotDecided)
        {
            return NotAccepted(command, access, requestedDecision, "STATUTORY_DISCOUNT_DECISION_NOT_AWAITING_REVIEW", detail);
        }

        var validationLinkage = requestedDecision == "APPROVE"
            ? await _reviewRepository.EnsureApprovedValidationLinkageAsync(
                    command.StatutoryDiscountDecisionCommandId,
                    command.UserId,
                    NormalizeOptional(command.DecisionReasonCode),
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (requestedDecision == "APPROVE" && validationLinkage is null)
        {
            return NotAccepted(
                command,
                access,
                requestedDecision,
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_FACTS_UNAVAILABLE",
                detail);
        }

        var completed = requestedDecision == "APPROVE"
            ? await _stagedCommandService.CompleteDecisionApprovedAsync(
                    command.StatutoryDiscountDecisionCommandId,
                    validationLinkage!.StatutoryDiscountValidationId,
                    validationLinkage.OriginalTariffSnapshotId,
                    validationLinkage.AppliedPolicyReferenceId,
                    validationLinkage.FallbackPolicyReferenceId,
                    validationLinkage.PolicyResolutionBasis,
                    validationLinkage.LocalOrdinanceApplied,
                    ToTariffFacts(validationLinkage),
                    NormalizeOptional(command.DecisionReasonCode),
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false)
            : await _stagedCommandService.CompleteDecisionRejectedAsync(
                    command.StatutoryDiscountDecisionCommandId,
                    NormalizeOptional(command.DecisionReasonCode),
                    safeErrorCode: null,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!string.Equals(completed.DecisionResultStatus, targetResult, StringComparison.Ordinal))
        {
            return Conflict(command, access, requestedDecision, completed, detail);
        }

        var review = await _reviewRepository.RecordReviewCompletionAsync(
                completed.StatutoryDiscountDecisionCommandId,
                command.UserId,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                access.EvaluationId,
                requestedDecision,
                NormalizeOptional(command.DecisionReasonCode),
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(review.ReviewerDecision, requestedDecision, StringComparison.Ordinal))
        {
            return Conflict(command, access, requestedDecision, completed, review);
        }

        return new StatutoryDiscountServiceChannelReviewDecisionResult(
            access.EvaluationId,
            true,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            DecisionAccepted: true,
            DecisionPersisted: true,
            completed.StatutoryDiscountDecisionCommandId,
            completed.ParkingSessionId,
            completed.SourceChannel,
            completed.EntitlementType,
            canonical.CommandStatus,
            completed.CommandStatus,
            canonical.DecisionResultStatus,
            completed.DecisionResultStatus,
            review.ReviewStatus,
            requestedDecision,
            completed.ReasonCode,
            AlreadyDecided: false,
            DecisionChanged: true,
            IneligibilityReason: null,
            ErrorCode: null,
            command.CorrelationId);
    }

    private async Task<OperatorConsoleAccessEvaluationResult> EvaluateAndPersistAsync(
        OperatorConsoleReviewAccessContext context,
        string actionCode,
        Guid? ParkingSessionId,
        CancellationToken cancellationToken)
    {
        var evaluation = await _accessEvaluationService.EvaluateAsync(
                new OperatorConsoleAccessEvaluationCommand(
                    context.UserId,
                    context.OperatorDeviceBindingId,
                    context.SiteId,
                    context.SiteGroupId,
                    context.OperatorShiftId,
                    WorkflowCode,
                    actionCode,
                    ParkingSessionId,
                    EvidenceAccessIntent: null,
                    context.IdempotencyKey,
                    context.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);

        return await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool SiteAllowed(
        StatutoryDiscountServiceChannelReviewDetail detail,
        OperatorConsoleAccessEvaluationResult access)
    {
        if (access.SiteContext.SiteId.HasValue && detail.SiteId.HasValue && access.SiteContext.SiteId != detail.SiteId)
        {
            return false;
        }

        if (access.SiteContext.SiteGroupId.HasValue && detail.SiteGroupId.HasValue && access.SiteContext.SiteGroupId != detail.SiteGroupId)
        {
            return false;
        }

        return true;
    }

    private static StatutoryDiscountServiceChannelReviewDecisionResult Denied(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        OperatorConsoleAccessEvaluationResult access,
        string decision) =>
        new(
            access.EvaluationId,
            AccessAllowed: false,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            command.StatutoryDiscountDecisionCommandId,
            ParkingSessionId: Guid.Empty,
            SourceChannel: string.Empty,
            EntitlementType: string.Empty,
            PreviousCommandStatus: string.Empty,
            CurrentCommandStatus: string.Empty,
            PreviousDecisionResultStatus: string.Empty,
            CurrentDecisionResultStatus: string.Empty,
            ReviewStatus: string.Empty,
            decision,
            NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            access.CorrelationId);

    private static StatutoryDiscountServiceChannelReviewDecisionResult NotAccepted(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        OperatorConsoleAccessEvaluationResult access,
        string decision,
        string errorCode,
        StatutoryDiscountServiceChannelReviewDetail? detail = null) =>
        new(
            access.EvaluationId,
            AccessAllowed: true,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            DecisionAccepted: false,
            DecisionPersisted: false,
            command.StatutoryDiscountDecisionCommandId,
            detail?.ParkingSessionId ?? Guid.Empty,
            detail?.SourceChannel ?? string.Empty,
            detail?.EntitlementType ?? string.Empty,
            detail?.CommandStatus ?? string.Empty,
            detail?.CommandStatus ?? string.Empty,
            detail?.DecisionResultStatus ?? string.Empty,
            detail?.DecisionResultStatus ?? string.Empty,
            detail?.ReviewStatus ?? string.Empty,
            decision,
            NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: errorCode,
            ErrorCode: errorCode,
            access.CorrelationId);

    private static StatutoryDiscountServiceChannelReviewDecisionResult Existing(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        OperatorConsoleAccessEvaluationResult access,
        string decision,
        StatutoryDiscountDecisionV2Record canonical,
        StatutoryDiscountServiceChannelReviewDetail detail) =>
        new(
            access.EvaluationId,
            AccessAllowed: true,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            DecisionAccepted: true,
            DecisionPersisted: true,
            canonical.StatutoryDiscountDecisionCommandId,
            canonical.ParkingSessionId,
            canonical.SourceChannel,
            canonical.EntitlementType,
            canonical.CommandStatus,
            canonical.CommandStatus,
            canonical.DecisionResultStatus,
            canonical.DecisionResultStatus,
            detail.ReviewStatus,
            decision,
            canonical.ReasonCode ?? NormalizeOptional(command.DecisionReasonCode),
            AlreadyDecided: true,
            DecisionChanged: false,
            IneligibilityReason: null,
            ErrorCode: null,
            access.CorrelationId);

    private static StatutoryDiscountServiceChannelReviewDecisionResult Conflict(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        OperatorConsoleAccessEvaluationResult access,
        string decision,
        StatutoryDiscountDecisionV2Record canonical,
        StatutoryDiscountServiceChannelReviewDetail detail) =>
        Existing(command, access, decision, canonical, detail) with
        {
            DecisionAccepted = false,
            DecisionPersisted = false,
            IneligibilityReason = "STATUTORY_DISCOUNT_DECISION_ALREADY_COMPLETED",
            ErrorCode = "STATUTORY_DISCOUNT_DECISION_ALREADY_COMPLETED"
        };

    private static string Validate(StatutoryDiscountServiceChannelReviewDecisionCommand command)
    {
        ValidateGuid(command.StatutoryDiscountDecisionCommandId, nameof(command.StatutoryDiscountDecisionCommandId));
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

        var decision = NormalizeOptional(command.Decision);
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static StatutoryDiscountDecisionV2TariffFacts ToTariffFacts(
        StatutoryDiscountServiceChannelValidationLinkage linkage)
    {
        var computation = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            new OperatorConsoleStatutoryDiscountComputationRequest(
                linkage.GrossAmountMinorUnits,
                linkage.EntitlementType,
                linkage.BenefitType,
                linkage.DiscountBaseScope));

        if (!computation.Accepted)
        {
            throw new InvalidOperationException("Reviewed service-channel statutory-discount payable-basis facts are not computable.");
        }

        return new StatutoryDiscountDecisionV2TariffFacts(
            computation.GrossAmountMinorUnits,
            computation.VatExclusiveAmountMinorUnits,
            computation.VatAmountMinorUnits,
            computation.StatutoryDiscountAmountMinorUnits,
            computation.FinalPayableAmountMinorUnits,
            linkage.Currency);
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
