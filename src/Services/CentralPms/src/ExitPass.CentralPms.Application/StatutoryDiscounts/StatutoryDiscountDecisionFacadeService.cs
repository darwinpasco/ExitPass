using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Shared statutory-discount facade that reuses the merged Operator Console statutory-discount application path.
/// </summary>
public sealed class StatutoryDiscountDecisionFacadeService : IStatutoryDiscountDecisionFacadeService
{
    private static readonly HashSet<string> SupportedEntitlements = new(StringComparer.Ordinal)
    {
        "SENIOR_CITIZEN",
        "PWD"
    };

    private readonly IStatutoryDiscountDecisionFacadeRepository _repository;
    private readonly IOperatorConsoleStatutoryDiscountDraftService _draftService;
    private readonly IOperatorConsoleStatutoryDiscountEvidenceService _evidenceService;
    private readonly IOperatorConsoleStatutoryDiscountDecisionService _decisionService;
    private readonly IOperatorConsoleStatutoryDiscountApplyPayableBasisService _applyService;
    private readonly IOperatorConsoleStatutoryDiscountReadService _readService;
    private readonly ISystemClock _clock;

    public StatutoryDiscountDecisionFacadeService(
        IStatutoryDiscountDecisionFacadeRepository repository,
        IOperatorConsoleStatutoryDiscountDraftService draftService,
        IOperatorConsoleStatutoryDiscountEvidenceService evidenceService,
        IOperatorConsoleStatutoryDiscountDecisionService decisionService,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService applyService,
        IOperatorConsoleStatutoryDiscountReadService readService,
        ISystemClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _draftService = draftService ?? throw new ArgumentNullException(nameof(draftService));
        _evidenceService = evidenceService ?? throw new ArgumentNullException(nameof(evidenceService));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<StatutoryDiscountDecisionResult> SubmitAsync(
        StatutoryDiscountDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalized = NormalizeAndValidate(command);
        var repositoryCommand = new StatutoryDiscountDecisionRepositoryCommand(
            normalized,
            StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(normalized),
            StatutoryDiscountDecisionSemanticHash.Compute(normalized),
            StatutoryDiscountDecisionSemanticHash.SourceVersion,
            _clock.UtcNow);

        return await _repository.ExecuteWithCommandLockAsync(
            repositoryCommand,
            lockedCancellationToken => SubmitLockedAsync(repositoryCommand, normalized, lockedCancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StatutoryDiscountDecisionResult> SubmitLockedAsync(
        StatutoryDiscountDecisionRepositoryCommand repositoryCommand,
        StatutoryDiscountDecisionCommand normalized,
        CancellationToken cancellationToken)
    {
        var begin = await _repository.BeginAsync(repositoryCommand, cancellationToken).ConfigureAwait(false);
        if (begin.SemanticConflict)
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "IDEMPOTENCY_SEMANTIC_CONFLICT",
                "The same idempotency scope and key were already used for different statutory-discount facts.");
        }

        if (begin.Existing && begin.Record.DecisionStatus is not "PROCESSING")
        {
            return (begin.Record with { ResultClassification = "IDEMPOTENT_REPLAY" }).ToResult();
        }

        if (begin.Existing &&
            begin.Record.DecisionStatus is "PROCESSING" &&
            !string.Equals(begin.Record.IdempotencyKey, normalized.IdempotencyKey, StringComparison.Ordinal))
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS",
                "A statutory-discount decision for this parking session and entitlement is already processing.");
        }

        if (begin.Existing && begin.Record.DecisionStatus is "PROCESSING")
        {
            return (begin.Record with { ResultClassification = "RECOVERABLE_USING_ORIGINAL_KEY" }).ToResult();
        }

        var draft = await _draftService.DraftAsync(ToDraftCommand(normalized), cancellationToken).ConfigureAwait(false);
        if (!draft.DraftAccepted || draft.DraftId is null)
        {
            return await CompleteAsync(
                begin.Record.Merge(normalized, draft, detail: null, apply: null, "REJECTED", draft.ErrorCode ?? draft.IneligibilityReason),
                cancellationToken);
        }

        foreach (var evidence in normalized.EvidenceReferences)
        {
            var evidenceResult = await _evidenceService.CaptureAsync(
                ToEvidenceCommand(normalized, draft.DraftId.Value, evidence),
                cancellationToken).ConfigureAwait(false);

            if (evidenceResult is null || !evidenceResult.AccessAllowed || evidenceResult.ErrorCode is not null)
            {
                var error = evidenceResult?.ErrorCode ?? "EVIDENCE_REFERENCE_NOT_ACCEPTED";
                return await CompleteAsync(
                    begin.Record.Merge(normalized, draft, detail: null, apply: null, "REJECTED", error),
                    cancellationToken);
            }
        }

        var detail = await ReadDetailAsync(draft.DraftId.Value, normalized.CorrelationId, cancellationToken).ConfigureAwait(false);
        var decisionStatus = draft.ValidationStatus ?? "REQUESTED";
        var errorCode = draft.ErrorCode;

        if (!string.IsNullOrWhiteSpace(normalized.Decision))
        {
            var decision = await _decisionService.DecideAsync(
                ToDecisionCommand(normalized, draft.DraftId.Value),
                cancellationToken).ConfigureAwait(false);
            decisionStatus = decision.CurrentValidationStatus ?? decision.Decision ?? decisionStatus;
            errorCode = decision.ErrorCode ?? decision.IneligibilityReason;
            detail = await ReadDetailAsync(draft.DraftId.Value, normalized.CorrelationId, cancellationToken).ConfigureAwait(false);
        }

        OperatorConsoleStatutoryDiscountApplyPayableBasisResult? apply = null;
        if (normalized.ApplyPayableBasis && string.Equals(decisionStatus, "APPROVED", StringComparison.Ordinal))
        {
            apply = await _applyService.ApplyAsync(
                ToApplyCommand(normalized, draft.DraftId.Value),
                cancellationToken).ConfigureAwait(false);
            decisionStatus = apply.ApplicationAccepted ? "APPLIED_PAYABLE_BASIS" : decisionStatus;
            errorCode = apply.ErrorCode ?? apply.IneligibilityReason ?? errorCode;
            detail = await ReadDetailAsync(draft.DraftId.Value, normalized.CorrelationId, cancellationToken).ConfigureAwait(false);
        }

        return await CompleteAsync(
            begin.Record.Merge(normalized, draft, detail, apply, decisionStatus, errorCode),
            cancellationToken);
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

        var record = await _repository.GetAsync(statutoryDiscountDecisionCommandId, correlationId, cancellationToken)
            .ConfigureAwait(false);
        return record?.ToResult();
    }

    private async Task<StatutoryDiscountDecisionResult> CompleteAsync(
        StatutoryDiscountDecisionCommandRecord record,
        CancellationToken cancellationToken)
    {
        var completed = await _repository.CompleteAsync(record, cancellationToken).ConfigureAwait(false);
        return completed.ToResult();
    }

    private async Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> ReadDetailAsync(
        Guid draftId,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        await _readService.GetDraftAsync(
            new OperatorConsoleStatutoryDiscountDraftDetailQuery(draftId, correlationId),
            cancellationToken).ConfigureAwait(false);

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
            command.CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisCommand ToApplyCommand(
        StatutoryDiscountDecisionCommand command,
        Guid validationId) =>
        new(
            validationId,
            command.ReviewerUserId ?? command.ActorUserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            command.OriginalTariffSnapshotId,
            $"{command.IdempotencyKey}:apply",
            command.CorrelationId);

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
            throw Rejected("APPROVAL_REQUIRED_FOR_PAYABLE_BASIS", "Payable-basis application requires an APPROVE decision.");
        }

        if (decision is not null && !command.ReviewerAttestation)
        {
            throw Rejected("REVIEWER_ATTESTATION_REQUIRED", "Reviewer attestation is required for statutory-discount decisions.");
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
