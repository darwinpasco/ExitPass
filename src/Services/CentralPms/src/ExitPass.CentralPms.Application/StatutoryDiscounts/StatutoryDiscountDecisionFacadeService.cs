using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.OperatorConsole;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Shared statutory-discount facade that orchestrates canonical staged commands while reusing the merged application path.
/// </summary>
public sealed class StatutoryDiscountDecisionFacadeService : IStatutoryDiscountDecisionFacadeService
{
    private static readonly HashSet<string> SupportedEntitlements = new(StringComparer.Ordinal)
    {
        "SENIOR_CITIZEN",
        "PWD"
    };

    private readonly IStatutoryDiscountStagedCommandService _stagedCommandService;
    private readonly IStatutoryDiscountDecisionFacadeRepository _historicalRepository;
    private readonly IOperatorConsoleStatutoryDiscountDraftService _draftService;
    private readonly IOperatorConsoleStatutoryDiscountEvidenceService _evidenceService;
    private readonly IOperatorConsoleStatutoryDiscountDecisionService _decisionService;
    private readonly IOperatorConsoleStatutoryDiscountApplyPayableBasisService _applyService;
    private readonly IOperatorConsoleStatutoryDiscountReadService _readService;

    public StatutoryDiscountDecisionFacadeService(
        IStatutoryDiscountStagedCommandService stagedCommandService,
        IStatutoryDiscountDecisionFacadeRepository historicalRepository,
        IOperatorConsoleStatutoryDiscountDraftService draftService,
        IOperatorConsoleStatutoryDiscountEvidenceService evidenceService,
        IOperatorConsoleStatutoryDiscountDecisionService decisionService,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService applyService,
        IOperatorConsoleStatutoryDiscountReadService readService)
    {
        _stagedCommandService = stagedCommandService ?? throw new ArgumentNullException(nameof(stagedCommandService));
        _historicalRepository = historicalRepository ?? throw new ArgumentNullException(nameof(historicalRepository));
        _draftService = draftService ?? throw new ArgumentNullException(nameof(draftService));
        _evidenceService = evidenceService ?? throw new ArgumentNullException(nameof(evidenceService));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
    }

    public async Task<StatutoryDiscountDecisionResult> SubmitAsync(
        StatutoryDiscountDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalized = NormalizeAndValidate(command);
        var decisionStageKey = DeriveStageIdempotencyKey(normalized.IdempotencyKey, "decision-v2", normalized.ParkingSessionId);
        var decisionStart = await _stagedCommandService.CreateOrResolveDecisionAsync(
                ToDecisionV2Command(normalized, decisionStageKey),
                cancellationToken)
            .ConfigureAwait(false);

        var pendingReviewIntake = IsServiceChannel(normalized.SourceChannel) && string.IsNullOrWhiteSpace(normalized.Decision);
        var decision = pendingReviewIntake
            ? await ResolvePendingReviewDecisionStageAsync(normalized, decisionStart, cancellationToken).ConfigureAwait(false)
            : await ResolveDecisionStageAsync(normalized, decisionStart, cancellationToken).ConfigureAwait(false);

        StatutoryDiscountPayableBasisApplicationV1Record? application = null;
        var applicationResultClassification = StatutoryDiscountApplicationStageStatuses.NotRequested;
        var applicationRequested = normalized.ApplyPayableBasis && !pendingReviewIntake;

        if (applicationRequested &&
            string.Equals(decision.DecisionResultStatus, StatutoryDiscountDecisionV2ResultStates.Approved, StringComparison.Ordinal))
        {
            var applicationStart = await CreateOrResolveApplicationStageAsync(normalized, decision, cancellationToken)
                .ConfigureAwait(false);
            applicationResultClassification = applicationStart.ResultClassification;
            application = await ResolveApplicationStageAsync(normalized, applicationStart, cancellationToken)
                .ConfigureAwait(false);
        }

        var overall = ResolveOverallClassification(decisionStart, decision, application, applicationResultClassification, applicationRequested);
        return decision.ToFacadeResult(application, applicationRequested, overall, normalized.CorrelationId);
    }

    public async Task<StatutoryDiscountDecisionResult?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountDecisionCommandId == Guid.Empty)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_DECISION_REFERENCE_REQUIRED",
                "Statutory discount decision reference is required.",
                isNotFound: true);
        }

        if (correlationId == Guid.Empty)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "CORRELATION_ID_REQUIRED",
                "Correlation id is required.");
        }

        var decision = await _stagedCommandService.GetDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
        if (decision is not null)
        {
            var application = await _stagedCommandService.GetApplicationByDecisionAsync(
                    statutoryDiscountDecisionCommandId,
                    cancellationToken)
                .ConfigureAwait(false);
            var applicationRequested = application is not null;
            var overall = decision.SemanticHashSourceVersion == StatutoryDiscountDecisionSemanticHash.SourceVersion
                ? StatutoryDiscountOneShotResultClassifications.HistoricalV1Replay
                : ResolveReadbackOverallClassification(decision, application, applicationRequested);
            return decision.ToFacadeResult(application, applicationRequested, overall, correlationId);
        }

        var historical = await _historicalRepository.GetAsync(statutoryDiscountDecisionCommandId, correlationId, cancellationToken)
            .ConfigureAwait(false);
        return historical?.ToResult();
    }

    private async Task<StatutoryDiscountDecisionV2Record> ResolveDecisionStageAsync(
        StatutoryDiscountDecisionCommand normalized,
        StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record> start,
        CancellationToken cancellationToken)
    {
        if (start.Record is null)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_NOT_AVAILABLE",
                "Statutory discount decision command is not available.");
        }

        if (start.SemanticConflict)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT",
                "A statutory-discount decision already exists for materially different decision facts.");
        }

        if (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Completed)
        {
            return start.Record;
        }

        if (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable)
        {
            return start.Record;
        }

        if (start.Existing &&
            (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Received
                or StatutoryDiscountDecisionV2CommandStates.Processing) &&
            !string.Equals(start.Record.IdempotencyKey, DeriveStageIdempotencyKey(normalized.IdempotencyKey, "decision-v2", normalized.ParkingSessionId), StringComparison.Ordinal))
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS",
                "A statutory-discount decision for this parking session and entitlement is already processing.");
        }

        var processing = await _stagedCommandService.MarkDecisionProcessingAsync(
                start.Record.StatutoryDiscountDecisionCommandId,
                normalized.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await ExecuteDecisionWorkflowAsync(normalized, processing, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _stagedCommandService.RecordDecisionFailureAsync(
                    processing.StatutoryDiscountDecisionCommandId,
                    retryable: true,
                    "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE",
                    normalized.CorrelationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<StatutoryDiscountDecisionV2Record> ExecuteDecisionWorkflowAsync(
        StatutoryDiscountDecisionCommand normalized,
        StatutoryDiscountDecisionV2Record processing,
        CancellationToken cancellationToken)
    {
        var draft = await _draftService.DraftAsync(ToDraftCommand(normalized), cancellationToken).ConfigureAwait(false);
        if (!draft.DraftAccepted || draft.DraftId is null)
        {
            return await _stagedCommandService.CompleteDecisionRejectedAsync(
                    processing.StatutoryDiscountDecisionCommandId,
                    normalized.DecisionReasonCode ?? normalized.ReasonCode,
                    draft.ErrorCode ?? draft.IneligibilityReason ?? "STATUTORY_DISCOUNT_DRAFT_NOT_ACCEPTED",
                    normalized.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var evidence in normalized.EvidenceReferences)
        {
            var evidenceResult = await _evidenceService.CaptureAsync(
                    ToEvidenceCommand(normalized, draft.DraftId.Value, evidence),
                    cancellationToken)
                .ConfigureAwait(false);

            if (evidenceResult is null || !evidenceResult.AccessAllowed || evidenceResult.ErrorCode is not null)
            {
                return await _stagedCommandService.CompleteDecisionRejectedAsync(
                        processing.StatutoryDiscountDecisionCommandId,
                        normalized.DecisionReasonCode ?? normalized.ReasonCode,
                        evidenceResult?.ErrorCode ?? "EVIDENCE_REFERENCE_NOT_ACCEPTED",
                        normalized.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var detail = await ReadDetailAsync(draft.DraftId.Value, normalized.CorrelationId, cancellationToken).ConfigureAwait(false);
        var decisionStatus = draft.ValidationStatus ?? "REQUESTED";
        var errorCode = draft.ErrorCode;
        var reasonCode = normalized.DecisionReasonCode ?? normalized.ReasonCode;

        if (!string.IsNullOrWhiteSpace(normalized.Decision))
        {
            var decision = await _decisionService.DecideAsync(
                    ToDecisionCommand(normalized, draft.DraftId.Value),
                    cancellationToken)
                .ConfigureAwait(false);
            decisionStatus = decision.CurrentValidationStatus ?? decision.Decision ?? decisionStatus;
            errorCode = decision.ErrorCode ?? decision.IneligibilityReason;
            reasonCode = decision.DecisionReasonCode ?? reasonCode;
            detail = await ReadDetailAsync(draft.DraftId.Value, normalized.CorrelationId, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(decisionStatus, "APPROVED", StringComparison.Ordinal))
        {
            return await _stagedCommandService.CompleteDecisionApprovedAsync(
                    processing.StatutoryDiscountDecisionCommandId,
                    draft.DraftId,
                    detail?.OriginalTariffSnapshotId ?? normalized.OriginalTariffSnapshotId,
                    detail?.StatutoryDiscountPolicyId ?? draft.Policy?.StatutoryDiscountPolicyId,
                    fallbackPolicyReferenceId: null,
                    detail?.PolicyResolutionBasis ?? draft.Policy?.PolicyResolutionBasis,
                    !string.IsNullOrWhiteSpace(detail?.OrdinanceReference),
                    ToTariffFacts(detail),
                    reasonCode,
                    normalized.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await _stagedCommandService.CompleteDecisionRejectedAsync(
                processing.StatutoryDiscountDecisionCommandId,
                reasonCode,
                errorCode ?? "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED",
                normalized.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StatutoryDiscountDecisionV2Record> ResolvePendingReviewDecisionStageAsync(
        StatutoryDiscountDecisionCommand normalized,
        StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record> start,
        CancellationToken cancellationToken)
    {
        if (start.Record is null)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_NOT_AVAILABLE",
                "Statutory discount decision command is not available.");
        }

        if (start.SemanticConflict)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT",
                "A statutory-discount decision already exists for materially different decision facts.");
        }

        if (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Completed
            or StatutoryDiscountDecisionV2CommandStates.AwaitingReview
            or StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable)
        {
            return start.Record;
        }

        if (start.Existing &&
            (start.Record.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Received
                or StatutoryDiscountDecisionV2CommandStates.Processing) &&
            !string.Equals(start.Record.IdempotencyKey, DeriveStageIdempotencyKey(normalized.IdempotencyKey, "decision-v2", normalized.ParkingSessionId), StringComparison.Ordinal))
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS",
                "A statutory-discount decision for this parking session and entitlement is already processing.");
        }

        return await _stagedCommandService.MarkDecisionAwaitingReviewAsync(
                start.Record.StatutoryDiscountDecisionCommandId,
                normalized.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>> CreateOrResolveApplicationStageAsync(
        StatutoryDiscountDecisionCommand normalized,
        StatutoryDiscountDecisionV2Record decision,
        CancellationToken cancellationToken)
    {
        var applicationStageKey = DeriveStageIdempotencyKey(
            normalized.IdempotencyKey,
            "payable-basis-application-v1",
            decision.StatutoryDiscountDecisionCommandId);
        var applicationCommand = ToApplicationV1Command(normalized, decision, applicationStageKey);
        var start = await _stagedCommandService.CreateOrResolveApplicationAsync(applicationCommand, cancellationToken)
            .ConfigureAwait(false);

        if (start.SemanticConflict)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT",
                "A statutory-discount payable-basis application already exists for materially different application facts.");
        }

        if (start.Record is null)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                start.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE",
                "Statutory discount payable-basis application command is not available.");
        }

        return start;
    }

    private async Task<StatutoryDiscountPayableBasisApplicationV1Record?> ResolveApplicationStageAsync(
        StatutoryDiscountDecisionCommand normalized,
        StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record> start,
        CancellationToken cancellationToken)
    {
        var record = start.Record!;
        if (start.Existing &&
            !start.Retryable &&
            (record.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
                or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing))
        {
            return record;
        }

        if (record.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable)
        {
            return record;
        }

        var processing = await _stagedCommandService.MarkApplicationProcessingAsync(
                record.StatutoryDiscountPayableBasisApplicationCommandId,
                normalized.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (processing.StatutoryDiscountValidationId is null)
        {
            return await _stagedCommandService.RecordApplicationFailureAsync(
                    processing.StatutoryDiscountPayableBasisApplicationCommandId,
                    retryable: false,
                    "STATUTORY_DISCOUNT_VALIDATION_REFERENCE_REQUIRED",
                    normalized.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            var apply = await _applyService.ApplyAsync(
                    ToApplyCommand(normalized, processing.StatutoryDiscountValidationId.Value, processing.IdempotencyKey),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!apply.ApplicationAccepted || apply.ErrorCode is not null)
            {
                return await _stagedCommandService.RecordApplicationFailureAsync(
                        processing.StatutoryDiscountPayableBasisApplicationCommandId,
                        retryable: false,
                        apply.ErrorCode ?? apply.IneligibilityReason ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_REJECTED",
                        normalized.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return await _stagedCommandService.CompleteApplicationAppliedAsync(
                    processing.StatutoryDiscountPayableBasisApplicationCommandId,
                    apply.PayableBasisApplicationId,
                    apply.AppliedTariffSnapshotId,
                    normalized.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await _stagedCommandService.RecordApplicationFailureAsync(
                    processing.StatutoryDiscountPayableBasisApplicationCommandId,
                    retryable: true,
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
                    normalized.CorrelationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> ReadDetailAsync(
        Guid draftId,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        await _readService.GetDraftAsync(
            new OperatorConsoleStatutoryDiscountDraftDetailQuery(draftId, correlationId),
            cancellationToken).ConfigureAwait(false);

    private static StatutoryDiscountDecisionV2Command ToDecisionV2Command(
        StatutoryDiscountDecisionCommand command,
        string stageIdempotencyKey)
    {
        var serviceChannel = IsServiceChannel(command.SourceChannel);

        return new(
            command.RequestReference,
            command.SourceChannel,
            command.ParkingSessionId,
            command.SiteId,
            command.SiteGroupId,
            command.TicketReference,
            command.PlateNumber,
            command.EntitlementType,
            new StatutoryDiscountDecisionV2BeneficiaryMetadata(
                BeneficiaryReference: null,
                command.EntitlementType,
                ClaimantRole: null,
                BeneficiaryCount: 1),
            new StatutoryDiscountDecisionV2IdentityMetadata(
                command.IdDocumentType,
                command.IssuingAuthority,
                command.ExpiryDate,
                command.MaskedIdReference,
                IdentityReferenceHash: null),
            command.EvidenceReferences.Select(evidence => new StatutoryDiscountDecisionV2EvidenceReference(
                    evidence.EvidenceType,
                    evidence.CaptureMethod,
                    evidence.StorageReference,
                    evidence.ReferenceNumberMasked,
                    evidence.VerificationStatus,
                    VerificationReference: null,
                    VerifiedAt: null))
                .ToArray(),
            new StatutoryDiscountDecisionV2AttestationFacts(
                command.RequesterAttestation,
                AttestationReference: null,
                command.ReasonCode,
                serviceChannel ? false : command.ReviewerAttestation),
            serviceChannel ? Guid.Empty : command.ActorUserId,
            serviceChannel ? null : command.ReviewerUserId,
            serviceChannel ? null : command.OperatorDeviceBindingId,
            serviceChannel ? null : command.OperatorShiftId,
            new StatutoryDiscountDecisionV2DecisionFacts(
                command.Decision ?? StatutoryDiscountDecisionV2ResultStates.NotDecided,
                command.DecisionReasonCode,
                SafeErrorCode: null),
            PolicyResolutionReferenceId: null,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: null,
            LocalOrdinanceApplied: false,
            command.OriginalTariffSnapshotId,
            OriginalTariffFacts: null,
            stageIdempotencyKey,
            command.CorrelationId);
    }

    private static StatutoryDiscountPayableBasisApplicationV1Command ToApplicationV1Command(
        StatutoryDiscountDecisionCommand command,
        StatutoryDiscountDecisionV2Record decision,
        string stageIdempotencyKey)
    {
        if (decision.StatutoryDiscountAmountMinorUnits is null ||
            decision.NetPayableAmountMinorUnits is null ||
            string.IsNullOrWhiteSpace(decision.Currency))
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_FACTS_UNAVAILABLE",
                "Approved statutory-discount payable-basis facts are unavailable.");
        }

        return new StatutoryDiscountPayableBasisApplicationV1Command(
            command.RequestReference,
            decision.StatutoryDiscountDecisionCommandId,
            decision.ParkingSessionId,
            command.SiteId,
            decision.EntitlementType,
            decision.StatutoryDiscountValidationId,
            decision.OriginalTariffSnapshotId ?? command.OriginalTariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            decision.AppliedPolicyReferenceId,
            decision.PolicyResolutionBasis,
            decision.StatutoryDiscountAmountMinorUnits.Value,
            decision.VatExclusiveAmountMinorUnits,
            decision.VatAmountMinorUnits,
            decision.NetPayableAmountMinorUnits.Value,
            decision.Currency,
            command.SourceChannel,
            stageIdempotencyKey,
            command.CorrelationId);
    }

    private static OperatorConsoleStatutoryDiscountDraftCommand ToDraftCommand(StatutoryDiscountDecisionCommand command) =>
        new(
            command.ActorUserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            command.ParkingSessionId,
            command.TicketReference,
            command.PlateNumber,
            command.EntitlementType,
            command.IdDocumentType,
            command.IssuingAuthority,
            command.ExpiryDate,
            command.MaskedIdReference,
            EntitlementFingerprint: null,
            command.EvidenceCaptureRequested,
            EvidenceAccessIntent: "SHARED_STATUTORY_DISCOUNT_DECISION",
            command.RequesterAttestation,
            command.AttestationNotes,
            command.ReasonCode,
            $"{command.IdempotencyKey}:draft",
            command.CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureCommand ToEvidenceCommand(
        StatutoryDiscountDecisionCommand command,
        Guid draftId,
        StatutoryDiscountEvidenceReference evidence) =>
        new(
            draftId,
            command.ActorUserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            evidence.EvidenceType,
            evidence.CaptureMethod,
            evidence.FileName,
            evidence.ContentType,
            evidence.SizeBytes,
            evidence.StorageReference,
            evidence.ReferenceNumberMasked,
            Notes: "shared-statutory-discount-facade",
            OperatorConfirmation: true,
            $"{command.IdempotencyKey}:evidence:{Normalize(evidence.EvidenceType)}:{NormalizeOptional(evidence.StorageReference) ?? "metadata"}",
            command.CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionCommand ToDecisionCommand(
        StatutoryDiscountDecisionCommand command,
        Guid draftId) =>
        new(
            draftId,
            command.ReviewerUserId ?? command.ActorUserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            command.Decision ?? string.Empty,
            command.DecisionReasonCode,
            DecisionNotes: null,
            command.ReviewerAttestation,
            $"{command.IdempotencyKey}:decision",
            command.CorrelationId,
            CanonicalDecisionAlreadyHandled: true);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisCommand ToApplyCommand(
        StatutoryDiscountDecisionCommand command,
        Guid validationId,
        string applicationStageIdempotencyKey) =>
        new(
            validationId,
            command.ReviewerUserId ?? command.ActorUserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            command.OriginalTariffSnapshotId,
            $"{applicationStageIdempotencyKey}:apply",
            command.CorrelationId);

    private static StatutoryDiscountDecisionV2TariffFacts? ToTariffFacts(
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail)
    {
        if (detail is null)
        {
            return null;
        }

        return new StatutoryDiscountDecisionV2TariffFacts(
            detail.OriginalAmountMinorUnits,
            detail.VatExclusiveAmountMinorUnits,
            detail.VatAmountMinorUnits,
            detail.StatutoryDiscountAmountMinorUnits,
            detail.FinalPayableAmountMinorUnits ?? detail.PayableAmountMinorUnits,
            detail.CurrencyCode);
    }

    private static string ResolveOverallClassification(
        StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record> decisionStart,
        StatutoryDiscountDecisionV2Record decision,
        StatutoryDiscountPayableBasisApplicationV1Record? application,
        string applicationStartClassification,
        bool applicationRequested)
    {
        if (decision.CommandStatus is StatutoryDiscountDecisionV2CommandStates.AwaitingReview)
        {
            return StatutoryDiscountOneShotResultClassifications.AwaitingReview;
        }

        if (applicationRequested &&
            application?.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable
                or StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable)
        {
            return StatutoryDiscountOneShotResultClassifications.Failed;
        }

        if (applicationRequested &&
            application?.CommandStatus is not StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return StatutoryDiscountOneShotResultClassifications.DecisionCompletedApplicationProcessing;
        }

        if (decisionStart.Existing &&
            (!applicationRequested ||
             applicationStartClassification is StatutoryDiscountPayableBasisApplicationV1ResultClassifications.IdempotentReplay ||
             application?.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied))
        {
            return StatutoryDiscountOneShotResultClassifications.IdempotentReplay;
        }

        return applicationRequested
            ? StatutoryDiscountOneShotResultClassifications.DecisionAndApplicationCompleted
            : StatutoryDiscountOneShotResultClassifications.DecisionOnlyCompleted;
    }

    private static string ResolveReadbackOverallClassification(
        StatutoryDiscountDecisionV2Record decision,
        StatutoryDiscountPayableBasisApplicationV1Record? application,
        bool applicationRequested)
    {
        if (decision.CommandStatus is StatutoryDiscountDecisionV2CommandStates.AwaitingReview)
        {
            return StatutoryDiscountOneShotResultClassifications.AwaitingReview;
        }

        if (decision.CommandStatus is StatutoryDiscountDecisionV2CommandStates.FailedRetryable
            or StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable)
        {
            return StatutoryDiscountOneShotResultClassifications.Failed;
        }

        if (applicationRequested && application?.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return StatutoryDiscountOneShotResultClassifications.DecisionAndApplicationCompleted;
        }

        if (applicationRequested)
        {
            return application?.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable
                    or StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable
                ? StatutoryDiscountOneShotResultClassifications.Failed
                : StatutoryDiscountOneShotResultClassifications.DecisionCompletedApplicationProcessing;
        }

        return StatutoryDiscountOneShotResultClassifications.DecisionOnlyCompleted;
    }

    private static string DeriveStageIdempotencyKey(string oneShotIdempotencyKey, string stage, Guid stageIdentity)
    {
        var source = $"statutory-discount-one-shot:{stage}:{stageIdentity:N}:{oneShotIdempotencyKey.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{stage}:sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static StatutoryDiscountDecisionCommand NormalizeAndValidate(StatutoryDiscountDecisionCommand command)
    {
        Require(command.RequestReference, "REQUEST_REFERENCE_REQUIRED", "Request reference is required.");
        Require(command.ParkingSessionId, "PARKING_SESSION_ID_REQUIRED", "Parking session id is required.");
        Require(command.ActorUserId, "ACTOR_USER_ID_REQUIRED", "Actor user id is required for the current statutory-discount application path.");
        Require(command.CorrelationId, "CORRELATION_ID_REQUIRED", "Correlation id is required.");
        Require(command.IdempotencyKey, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency key is required.");
        Require(command.MaskedIdReference, "MASKED_ID_REFERENCE_REQUIRED", "Masked ID reference is required.");

        var sourceChannel = StatutoryDiscountSourceChannels.Normalize(command.SourceChannel);
        if (!StatutoryDiscountSourceChannels.IsSupported(sourceChannel))
        {
            throw Rejected("UNSUPPORTED_SOURCE_CHANNEL", "Source channel must be OPERATOR_CONSOLE, WEBPAY, or ASSISTED_PAYMENT_TERMINAL.");
        }

        var entitlementType = Normalize(command.EntitlementType);
        if (!SupportedEntitlements.Contains(entitlementType))
        {
            throw Rejected("UNSUPPORTED_ENTITLEMENT_TYPE", "Only SENIOR_CITIZEN and PWD are supported in this slice.");
        }

        if (ContainsUnsafeNumericIdentifier(command.MaskedIdReference) ||
            command.EvidenceReferences.Any(evidence => ContainsUnsafeNumericIdentifier(evidence.ReferenceNumberMasked)))
        {
            throw Rejected("UNSAFE_IDENTIFIER_REJECTED", "Full statutory ID numbers are not accepted by the shared contract.");
        }

        var decision = NormalizeOptional(command.Decision);
        if (decision is not null && decision is not ("APPROVE" or "REJECT"))
        {
            throw Rejected("UNSUPPORTED_DECISION", "Decision must be APPROVE or REJECT when supplied.");
        }

        if (decision == "REJECT" && string.IsNullOrWhiteSpace(command.DecisionReasonCode))
        {
            throw Rejected("DECISION_REASON_REQUIRED", "Decision reason code is required for rejection.");
        }

        if (command.ApplyPayableBasis && decision != "APPROVE")
        {
            if (!IsServiceChannel(sourceChannel))
            {
                throw Rejected("APPROVAL_REQUIRED_FOR_PAYABLE_BASIS", "Payable-basis application requires an APPROVE decision.");
            }
        }

        if (decision is not null && !command.ReviewerAttestation)
        {
            throw Rejected("REVIEWER_ATTESTATION_REQUIRED", "Reviewer attestation is required for statutory-discount decisions.");
        }

        if (IsServiceChannel(sourceChannel) &&
            (decision is not null ||
             command.ReviewerUserId is not null ||
             command.ReviewerAttestation ||
             command.OperatorDeviceBindingId is not null ||
             command.OperatorShiftId is not null ||
             !string.IsNullOrWhiteSpace(command.DecisionReasonCode)))
        {
            throw Rejected(
                "STATUTORY_DISCOUNT_CHANNEL_FIELD_PROHIBITED",
                "Operator-only decision, reviewer, device, and shift fields are prohibited for this source channel.");
        }

        return command with
        {
            SourceChannel = sourceChannel,
            EntitlementType = entitlementType,
            IdDocumentType = Normalize(command.IdDocumentType),
            IssuingAuthority = Normalize(command.IssuingAuthority),
            MaskedIdReference = command.MaskedIdReference.Trim(),
            IdempotencyKey = command.IdempotencyKey.Trim(),
            TicketReference = NormalizeOptional(command.TicketReference),
            PlateNumber = NormalizeOptional(command.PlateNumber),
            Decision = decision,
            DecisionReasonCode = NormalizeOptional(command.DecisionReasonCode),
            EvidenceReferences = command.EvidenceReferences
                .Select(evidence => evidence with
                {
                    EvidenceType = Normalize(evidence.EvidenceType),
                    CaptureMethod = Normalize(evidence.CaptureMethod),
                    FileName = NormalizeOptional(evidence.FileName),
                    ContentType = NormalizeOptional(evidence.ContentType),
                    StorageReference = NormalizeOptional(evidence.StorageReference),
                    ReferenceNumberMasked = NormalizeOptional(evidence.ReferenceNumberMasked),
                    VerificationStatus = NormalizeOptional(evidence.VerificationStatus)
                })
                .ToArray()
        };
    }

    private static bool ContainsUnsafeNumericIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value, @"\d{5,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static bool IsServiceChannel(string sourceChannel) =>
        sourceChannel is StatutoryDiscountSourceChannels.WebPay
            or StatutoryDiscountSourceChannels.AssistedPaymentTerminal;

    private static void Require(Guid value, string errorCode, string message)
    {
        if (value == Guid.Empty)
        {
            throw Rejected(errorCode, message);
        }
    }

    private static void Require(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Rejected(errorCode, message);
        }
    }

    private static StatutoryDiscountDecisionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
