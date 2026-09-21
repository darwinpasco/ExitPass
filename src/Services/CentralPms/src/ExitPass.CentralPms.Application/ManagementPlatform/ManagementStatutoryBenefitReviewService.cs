using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementStatutoryBenefitReviewService : IManagementStatutoryBenefitReviewService
{
    private const string MediatedEvidenceAuditSourceChannel = "CENTRAL_PMS";
    private readonly IManagementStatutoryBenefitReviewRepository _repository;
    private readonly IStatutoryDiscountServiceChannelReviewRepository _canonicalReviews;
    private readonly IAuthorizedStatutoryBenefitDecisionService _decisions;
    private readonly IStatutoryDiscountDecisionFacadeService _decisionFacade;
    private readonly IOperatorConsoleStatutoryEvidenceReviewService _evidenceReview;
    private readonly ICentralPmsRbacRepository _audit;

    public ManagementStatutoryBenefitReviewService(
        IManagementStatutoryBenefitReviewRepository repository,
        IStatutoryDiscountServiceChannelReviewRepository canonicalReviews,
        IAuthorizedStatutoryBenefitDecisionService decisions,
        IStatutoryDiscountDecisionFacadeService decisionFacade,
        IOperatorConsoleStatutoryEvidenceReviewService evidenceReview,
        ICentralPmsRbacRepository audit)
    {
        _repository = repository;
        _canonicalReviews = canonicalReviews;
        _decisions = decisions;
        _decisionFacade = decisionFacade;
        _evidenceReview = evidenceReview;
        _audit = audit;
    }

    public async Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewQueue>> ListAsync(
        IdentityAdministrationActor actor,
        ManagementStatutoryBenefitReviewQuery query,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);
        if (normalized is null)
        {
            return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewQueue>.Failed(
                ManagementStatutoryBenefitReviewOutcome.Invalid,
                "INVALID_STATUTORY_BENEFIT_REVIEW_FILTER",
                "The statutory-benefit review filters are invalid.",
                query.CorrelationId);
        }

        var scope = await _repository.ResolveAuthorizedSitesAsync(
            actor, ManagementStatutoryBenefitReviewValues.ListPermission, cancellationToken);
        if (scope is null)
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_LIST", "DENIED", "VIEW_PERMISSION_OR_SCOPE_DENIED", actor, null, query.CorrelationId, cancellationToken);
            return Forbidden<ManagementStatutoryBenefitReviewQueue>(query.CorrelationId);
        }

        if (normalized.SiteReference.HasValue && !scope.SiteReferences.Contains(normalized.SiteReference.Value))
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_LIST", "DENIED", "SITE_SCOPE_CONCEALED", actor, normalized.SiteReference, query.CorrelationId, cancellationToken);
            return NotFound<ManagementStatutoryBenefitReviewQueue>(query.CorrelationId);
        }

        var queue = await _repository.ListAsync(normalized, scope.SiteReferences, cancellationToken);
        await AuditAsync("STATUTORY_BENEFIT_REVIEW_LIST", "SUCCESS", "LIST_RETURNED", actor, normalized.SiteReference, query.CorrelationId, cancellationToken);
        return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewQueue>.Succeeded(queue, query.CorrelationId);
    }

    public async Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewDetail>> GetAsync(
        IdentityAdministrationActor actor,
        Guid decisionCommandReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (decisionCommandReference == Guid.Empty)
        {
            return Invalid<ManagementStatutoryBenefitReviewDetail>("INVALID_STATUTORY_BENEFIT_REQUEST_REFERENCE", correlationId);
        }

        var scope = await _repository.ResolveAuthorizedSitesAsync(
            actor, ManagementStatutoryBenefitReviewValues.DetailPermission, cancellationToken);
        if (scope is null)
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DETAIL", "DENIED", "DETAIL_PERMISSION_OR_SCOPE_DENIED", actor, null, correlationId, cancellationToken);
            return Forbidden<ManagementStatutoryBenefitReviewDetail>(correlationId);
        }

        var metadata = await _repository.GetMetadataAsync(decisionCommandReference, cancellationToken);
        if (metadata is null || !scope.SiteReferences.Contains(metadata.SiteReference))
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DETAIL", "DENIED", "REQUEST_CONCEALED", actor, metadata?.SiteReference, correlationId, cancellationToken);
            return NotFound<ManagementStatutoryBenefitReviewDetail>(correlationId);
        }

        var canonical = await _canonicalReviews.GetAsync(decisionCommandReference, correlationId, cancellationToken);
        if (canonical is null)
        {
            return NotFound<ManagementStatutoryBenefitReviewDetail>(correlationId);
        }

        if (!CurrencyIsSupported(canonical))
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DETAIL", "FAILED", "NON_PHP_MONETARY_FACT_REJECTED", actor, metadata.SiteReference, correlationId, cancellationToken);
            return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewDetail>.Failed(
                ManagementStatutoryBenefitReviewOutcome.SourceUnavailable,
                "STATUTORY_BENEFIT_CURRENCY_UNSUPPORTED",
                "The request monetary facts are not available for PHP-only review.",
                correlationId);
        }

        var value = ToDetail(canonical, metadata, correlationId);
        await AuditAsync("STATUTORY_BENEFIT_REVIEW_DETAIL", "SUCCESS", "DETAIL_RETURNED", actor, metadata.SiteReference, correlationId, cancellationToken);
        return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewDetail>.Succeeded(value, correlationId);
    }

    public async Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitEvidence>> GetEvidenceAsync(
        IdentityAdministrationActor actor,
        Guid decisionCommandReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var scope = await _repository.ResolveAuthorizedSitesAsync(
            actor, ManagementStatutoryBenefitReviewValues.EvidencePermission, cancellationToken);
        var metadata = await _repository.GetMetadataAsync(decisionCommandReference, cancellationToken);
        if (scope is null)
        {
            await AuditAsync("STATUTORY_BENEFIT_EVIDENCE_VIEW", "DENIED", "EVIDENCE_PERMISSION_DENIED", actor, metadata?.SiteReference, correlationId, cancellationToken);
            return Forbidden<ManagementStatutoryBenefitEvidence>(correlationId);
        }

        if (metadata is null || !scope.SiteReferences.Contains(metadata.SiteReference))
        {
            await AuditAsync("STATUTORY_BENEFIT_EVIDENCE_VIEW", "DENIED", "EVIDENCE_CONCEALED", actor, metadata?.SiteReference, correlationId, cancellationToken);
            return NotFound<ManagementStatutoryBenefitEvidence>(correlationId);
        }

        var canonical = await _canonicalReviews.GetAsync(decisionCommandReference, correlationId, cancellationToken);
        if (canonical is null) return NotFound<ManagementStatutoryBenefitEvidence>(correlationId);
        var authoritative = await _evidenceReview.ReadAuthorizedAsync(
            decisionCommandReference,
            new StatutoryEvidenceAuthorizedReviewContext(
                metadata.SiteReference,
                canonical.SiteGroupId,
                canonical.SourceChannel,
                correlationId,
                new StatutoryEvidenceActor(actor.UserId, null, MediatedEvidenceAuditSourceChannel)),
            cancellationToken).ConfigureAwait(false);
        if (authoritative is null)
        {
            return NotFound<ManagementStatutoryBenefitEvidence>(correlationId);
        }

        var value = new ManagementStatutoryBenefitEvidence(
            ManagementStatutoryBenefitReviewValues.ContractVersion,
            decisionCommandReference,
            canonical.EvidenceRequired,
            authoritative.EvidenceRecorded,
            authoritative.Items.Select(item => new ManagementStatutoryBenefitEvidenceItem(
                item.DocumentType,
                "PROTECTED_UPLOAD",
                canonical.MaskedIdReference,
                item.ReviewabilityStatus)
            {
                EvidenceItemReference = item.EvidenceItemReference,
                DocumentType = item.DocumentType,
                ItemRole = item.ItemRole,
                ContentType = item.AuthoritativeContentType,
                UploadStatus = item.UploadStatus,
                ValidationStatus = item.ValidationStatus,
                MalwareScanStatus = item.ScanStatus,
                ReviewabilityStatus = item.ReviewabilityStatus,
                UploadedAt = item.UploadedAt,
                FinalizedAt = item.FinalizedAt,
                ValidatedAt = item.ValidatedAt,
                ScannedAt = item.ScannedAt,
                ReviewableAt = item.ReviewableAt,
                PreviewPermitted = item.PreviewPermitted
            }).ToArray(),
            correlationId);
        await AuditAsync("STATUTORY_BENEFIT_EVIDENCE_VIEW", "SUCCESS", "SAFE_EVIDENCE_METADATA_RETURNED", actor, metadata.SiteReference, correlationId, cancellationToken);
        return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitEvidence>.Succeeded(value, correlationId);
    }

    public async Task<OperatorConsoleStatutoryEvidencePreviewResult> OpenEvidencePreviewAsync(
        IdentityAdministrationActor actor,
        Guid decisionCommandReference,
        Guid evidenceItemReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var scope = await _repository.ResolveAuthorizedSitesAsync(
            actor, ManagementStatutoryBenefitReviewValues.EvidencePermission, cancellationToken);
        var metadata = await _repository.GetMetadataAsync(decisionCommandReference, cancellationToken);
        if (scope is null || metadata is null || !scope.SiteReferences.Contains(metadata.SiteReference))
        {
            await AuditAsync("STATUTORY_BENEFIT_EVIDENCE_PREVIEW", "DENIED", "EVIDENCE_PREVIEW_CONCEALED", actor, metadata?.SiteReference, correlationId, cancellationToken);
            return new OperatorConsoleStatutoryEvidencePreviewResult("REJECTED", "NOT_FOUND", false, correlationId, null, null);
        }

        var canonical = await _canonicalReviews.GetAsync(decisionCommandReference, correlationId, cancellationToken);
        if (canonical is null)
        {
            return new OperatorConsoleStatutoryEvidencePreviewResult("REJECTED", "NOT_FOUND", false, correlationId, null, null);
        }

        return await _evidenceReview.OpenAuthorizedPreviewAsync(
            decisionCommandReference,
            evidenceItemReference,
            new StatutoryEvidenceAuthorizedReviewContext(
                metadata.SiteReference,
                canonical.SiteGroupId,
                canonical.SourceChannel,
                correlationId,
                new StatutoryEvidenceActor(actor.UserId, null, MediatedEvidenceAuditSourceChannel)),
            cancellationToken).ConfigureAwait(false);
    }

    public Task RecordEvidencePreviewStreamOutcomeAsync(
        OperatorConsoleStatutoryEvidencePreviewAuditContext context,
        string outcome,
        CancellationToken cancellationToken) =>
        _evidenceReview.RecordPreviewStreamOutcomeAsync(context, outcome, cancellationToken);

    public async Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>> DecideAsync(
        IdentityAdministrationActor actor,
        ManagementStatutoryBenefitDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var decision = command.Decision.Trim().ToUpperInvariant();
        if (command.DecisionCommandReference == Guid.Empty || command.ExpectedVersion <= 0 ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) || decision is not ("APPROVE" or "REJECT") ||
            (decision == "REJECT" && string.IsNullOrWhiteSpace(command.RejectionReason)))
        {
            return Invalid<ManagementStatutoryBenefitDecisionResult>(
                decision == "REJECT" && string.IsNullOrWhiteSpace(command.RejectionReason)
                    ? "STATUTORY_BENEFIT_REJECTION_REASON_REQUIRED"
                    : "INVALID_STATUTORY_BENEFIT_DECISION_REQUEST",
                command.CorrelationId);
        }

        var permission = decision == "APPROVE"
            ? ManagementStatutoryBenefitReviewValues.ApprovePermission
            : ManagementStatutoryBenefitReviewValues.RejectPermission;
        var scope = await _repository.ResolveAuthorizedSitesAsync(actor, permission, cancellationToken);
        if (scope is null)
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DECISION", "DENIED", "DECISION_PERMISSION_OR_SCOPE_DENIED", actor, null, command.CorrelationId, cancellationToken);
            return Forbidden<ManagementStatutoryBenefitDecisionResult>(command.CorrelationId);
        }

        var metadata = await _repository.GetMetadataAsync(command.DecisionCommandReference, cancellationToken);
        if (metadata is null || !scope.SiteReferences.Contains(metadata.SiteReference))
        {
            return NotFound<ManagementStatutoryBenefitDecisionResult>(command.CorrelationId);
        }

        if (metadata.Version != command.ExpectedVersion)
        {
            var currentReview = await _canonicalReviews.GetAsync(
                command.DecisionCommandReference,
                command.CorrelationId,
                cancellationToken);
            var requestedStatus = decision == "APPROVE" ? "APPROVED" : "REJECTED";
            if (currentReview is null || !string.Equals(currentReview.ReviewStatus, requestedStatus, StringComparison.Ordinal))
            {
                return Conflict(command.CorrelationId, "STATUTORY_BENEFIT_REVIEW_VERSION_CONFLICT");
            }
        }

        var result = await _decisions.DecideAuthorizedAsync(
            new AuthorizedStatutoryBenefitDecisionCommand(
                command.DecisionCommandReference,
                actor.UserId,
                actor.HumanSessionId,
                decision,
                command.RejectionReason,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        if (!result.Accepted)
        {
            var conflict = result.ErrorCode == "STATUTORY_DISCOUNT_DECISION_ALREADY_COMPLETED";
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DECISION", conflict ? "REJECTED" : "FAILED", result.ErrorCode ?? "DECISION_REJECTED", actor, metadata.SiteReference, command.CorrelationId, cancellationToken);
            return conflict
                ? Conflict(command.CorrelationId, result.ErrorCode!)
                : ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>.Failed(
                    ManagementStatutoryBenefitReviewOutcome.Invalid,
                    result.ErrorCode ?? "STATUTORY_BENEFIT_DECISION_REJECTED",
                    "Central PMS did not accept the statutory-benefit decision.",
                    command.CorrelationId);
        }

        StatutoryDiscountDecisionResult? application = null;
        string? automaticApplicationFailureStatus = null;
        bool? automaticApplicationRetryable = null;
        string? automaticApplicationRecoveryAction = null;
        if (decision == "APPROVE")
        {
            var canonical = await _canonicalReviews.GetAsync(
                command.DecisionCommandReference,
                command.CorrelationId,
                cancellationToken);
            var caller = canonical is null
                ? null
                : await _repository.ResolveAutomaticApplicationCallerAsync(
                    canonical.SourceChannel,
                    metadata.SiteReference,
                    cancellationToken);
            if (canonical is not null && caller is not null)
            {
                try
                {
                    application = await _decisionFacade.SubmitAsync(
                        ToAutomaticApplicationCommand(canonical, caller, command.CorrelationId),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (StatutoryDiscountDecisionRejectedException)
                {
                    application = await _decisionFacade.GetAsync(
                        command.DecisionCommandReference,
                        command.CorrelationId,
                        cancellationToken).ConfigureAwait(false);
                    if (ApplicationWasNotRequested(application))
                    {
                        automaticApplicationFailureStatus = StatutoryDiscountApplicationStageStatuses.FailedNonRetryable;
                        automaticApplicationRetryable = false;
                        automaticApplicationRecoveryAction = StatutoryDiscountDecisionRecoveryActions.DoNotRetry;
                    }
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // The approval is durable. Recover its canonical application state instead of
                    // turning a retryable downstream application failure into a second reviewer action.
                    application = await _decisionFacade.GetAsync(
                        command.DecisionCommandReference,
                        command.CorrelationId,
                        cancellationToken).ConfigureAwait(false);
                    if (ApplicationWasNotRequested(application))
                    {
                        automaticApplicationFailureStatus = StatutoryDiscountApplicationStageStatuses.FailedRetryable;
                        automaticApplicationRetryable = true;
                        automaticApplicationRecoveryAction = StatutoryDiscountDecisionRecoveryActions.RetrySameRequestWithOriginalKey;
                    }
                }
            }
            else
            {
                application = await _decisionFacade.GetAsync(
                    command.DecisionCommandReference,
                    command.CorrelationId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var current = await _repository.GetMetadataAsync(command.DecisionCommandReference, cancellationToken)
            ?? throw new InvalidOperationException("The decided statutory-benefit review could not be read back.");
        if (!result.DecidedAt.HasValue)
        {
            await AuditAsync("STATUTORY_BENEFIT_REVIEW_DECISION", "FAILED", "AUTHORITATIVE_DECISION_TIMESTAMP_UNAVAILABLE", actor, metadata.SiteReference, command.CorrelationId, cancellationToken);
            return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>.Failed(
                ManagementStatutoryBenefitReviewOutcome.SourceUnavailable,
                "STATUTORY_BENEFIT_DECISION_TIMESTAMP_UNAVAILABLE",
                "The authoritative statutory-benefit decision timestamp is unavailable.",
                command.CorrelationId);
        }

        var value = new ManagementStatutoryBenefitDecisionResult(
            ManagementStatutoryBenefitReviewValues.ContractVersion,
            command.DecisionCommandReference,
            result.Status,
            result.Decision,
            result.Reason,
            current.ReviewerDisplayName ?? "Authorized reviewer",
            result.DecidedAt.Value,
            result.AlreadyDecided,
            current.Version,
            command.CorrelationId,
            automaticApplicationFailureStatus ?? application?.ApplicationCommandStatus ?? StatutoryDiscountApplicationStageStatuses.NotRequested,
            application?.PayableBasisReady ?? false,
            automaticApplicationRetryable ?? application?.ApplicationRetryable ?? (decision == "APPROVE"),
            automaticApplicationRecoveryAction ?? application?.ApplicationRecoveryAction ??
                (decision == "APPROVE" && application?.PayableBasisReady != true
                    ? StatutoryDiscountDecisionRecoveryActions.RetrySameRequestWithOriginalKey
                    : null));
        await AuditAsync("STATUTORY_BENEFIT_REVIEW_DECISION", result.AlreadyDecided ? "DUPLICATE" : "SUCCESS", "TERMINAL_DECISION_RECORDED", actor, metadata.SiteReference, command.CorrelationId, cancellationToken);
        return ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>.Succeeded(value, command.CorrelationId);
    }

    private static bool ApplicationWasNotRequested(StatutoryDiscountDecisionResult? application) =>
        application is null || string.Equals(
            application.ApplicationCommandStatus,
            StatutoryDiscountApplicationStageStatuses.NotRequested,
            StringComparison.Ordinal);

    private Task AuditAsync(string type, string result, string reason, IdentityAdministrationActor actor, Guid? site, Guid correlationId, CancellationToken cancellationToken) =>
        _audit.RecordAuditEventAsync(type, result, reason, "STATUTORY_BENEFIT_REVIEW", site, actor.UserId, null, correlationId,
            $"Management Platform statutory-benefit review event {reason}.", cancellationToken);

    private static ManagementStatutoryBenefitReviewQuery? Normalize(ManagementStatutoryBenefitReviewQuery query)
    {
        var status = string.IsNullOrWhiteSpace(query.Status) ? "PENDING_REVIEW" : query.Status.Trim().ToUpperInvariant();
        status = status switch { "PENDING" => "PENDING_REVIEW", "APPROVED" => "APPROVED", "REJECTED" => "REJECTED", "ALL" => "ALL", _ => status };
        var source = string.IsNullOrWhiteSpace(query.SourceChannel) ? null : query.SourceChannel.Trim().ToUpperInvariant();
        var benefit = string.IsNullOrWhiteSpace(query.BenefitType) ? null : query.BenefitType.Trim().ToUpperInvariant();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToLowerInvariant();
        if (status is not ("PENDING_REVIEW" or "APPROVED" or "REJECTED" or "ALL") ||
            source is not null and not ("WEBPAY" or "ASSISTED_PAYMENT_TERMINAL") ||
            benefit is not null and not ("SENIOR_CITIZEN" or "PWD") ||
            search?.Length > 160 || query.Page < 1 || query.PageSize is < 1 or > 100 ||
            query.SubmittedFrom >= query.SubmittedTo)
        {
            return null;
        }

        return query with { Status = status, SourceChannel = source, BenefitType = benefit, Search = search };
    }

    private static bool CurrencyIsSupported(StatutoryDiscountServiceChannelReviewDetail detail)
    {
        var hasMoney = detail.OriginalAmountMinorUnits.HasValue || detail.StatutoryDiscountAmountMinorUnits.HasValue || detail.FinalPayableAmountMinorUnits.HasValue;
        return !hasMoney || string.Equals(detail.Currency, ManagementStatutoryBenefitReviewValues.Currency, StringComparison.Ordinal);
    }

    private static ManagementStatutoryBenefitReviewDetail ToDetail(
        StatutoryDiscountServiceChannelReviewDetail source,
        ManagementStatutoryBenefitReviewMetadata metadata,
        Guid correlationId)
    {
        var money = source.OriginalAmountMinorUnits.HasValue && source.StatutoryDiscountAmountMinorUnits.HasValue && source.FinalPayableAmountMinorUnits.HasValue
            ? new ManagementStatutoryBenefitMoney(source.OriginalAmountMinorUnits.Value, source.StatutoryDiscountAmountMinorUnits.Value, source.FinalPayableAmountMinorUnits.Value, ManagementStatutoryBenefitReviewValues.Currency)
            : null;
        var decision = source.ReviewerDecision is not null && source.ReviewedAt.HasValue
            ? new ManagementStatutoryBenefitDecision(source.ReviewerDecision, source.ReviewerReasonCode, metadata.ReviewerDisplayName ?? "Authorized reviewer", source.ReviewedAt.Value)
            : null;
        return new ManagementStatutoryBenefitReviewDetail(
            ManagementStatutoryBenefitReviewValues.ContractVersion,
            source.RequestReference,
            source.StatutoryDiscountDecisionCommandId,
            source.ParkingSessionId,
            source.TicketReference,
            metadata.SiteReference,
            metadata.SiteCode,
            metadata.SiteName,
            source.SourceChannel,
            source.EntitlementType,
            source.ReviewStatus,
            source.EvidenceRequired,
            source.EvidenceRecorded,
            source.IdDocumentType,
            source.IssuingAuthority,
            source.ExpiryDate,
            source.MaskedIdReference,
            source.RequesterAttestation,
            RequiredResidencyWasSatisfied(source),
            source.AttestationNotes ?? source.ReasonCode,
            source.SubmittedAt,
            money,
            decision,
            metadata.Version,
            correlationId);
    }

    private static StatutoryDiscountDecisionCommand ToAutomaticApplicationCommand(
        StatutoryDiscountServiceChannelReviewDetail source,
        ManagementStatutoryBenefitAutomaticApplicationCaller caller,
        Guid correlationId) =>
        new(
            source.RequestReference,
            source.SourceChannel,
            source.ParkingSessionId,
            source.SiteId,
            source.SiteGroupId,
            source.TicketReference,
            source.PlateNumber,
            source.EntitlementType,
            source.IdDocumentType ?? string.Empty,
            source.IssuingAuthority ?? string.Empty,
            source.ExpiryDate,
            source.MaskedIdReference ?? throw new InvalidOperationException("The approved statutory-benefit request lacks a masked ID reference."),
            source.EvidenceRequired,
            source.EvidenceReferences.Select(evidence => new StatutoryDiscountEvidenceReference(
                evidence.EvidenceType,
                evidence.CaptureMethod,
                FileName: null,
                ContentType: null,
                SizeBytes: null,
                evidence.StorageReference,
                evidence.ReferenceNumberMasked,
                evidence.VerificationStatus)).ToArray(),
            caller.ServiceIdentityId,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            source.RequesterAttestation,
            source.AttestationNotes,
            source.ReasonCode,
            Decision: null,
            DecisionReasonCode: null,
            ReviewerUserId: null,
            ReviewerAttestation: false,
            ApplyPayableBasis: true,
            source.OriginalTariffSnapshotId,
            BeneficiaryResidencySatisfied: RequiredResidencyWasSatisfied(source),
            $"management-review-auto-apply:{source.StatutoryDiscountDecisionCommandId:N}",
            correlationId,
            new StatutoryDiscountServiceChannelCallerContext(
                caller.ServiceIdentityId,
                caller.SourceChannel,
                caller.ApplicationAudience,
                caller.PermissionCode));

    private static bool? RequiredResidencyWasSatisfied(StatutoryDiscountServiceChannelReviewDetail source) =>
        string.Equals(
            source.GoverningPolicy?.BeneficiaryResidencyScope,
            "RESIDENT_ONLY",
            StringComparison.Ordinal)
            ? true
            : null;

    private static ManagementStatutoryBenefitReviewResult<T> Invalid<T>(string code, Guid correlationId) =>
        ManagementStatutoryBenefitReviewResult<T>.Failed(ManagementStatutoryBenefitReviewOutcome.Invalid, code, "The statutory-benefit review request is invalid.", correlationId);

    private static ManagementStatutoryBenefitReviewResult<T> Forbidden<T>(Guid correlationId) =>
        ManagementStatutoryBenefitReviewResult<T>.Failed(ManagementStatutoryBenefitReviewOutcome.Forbidden, "CENTRAL_PMS_RBAC_FORBIDDEN", "The authenticated principal does not have the required statutory-benefit review authority.", correlationId);

    private static ManagementStatutoryBenefitReviewResult<T> NotFound<T>(Guid correlationId) =>
        ManagementStatutoryBenefitReviewResult<T>.Failed(ManagementStatutoryBenefitReviewOutcome.NotFound, "STATUTORY_BENEFIT_REQUEST_NOT_FOUND_OR_DENIED", "The statutory-benefit request is unavailable.", correlationId);

    private static ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult> Conflict(Guid correlationId, string code) =>
        ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>.Failed(ManagementStatutoryBenefitReviewOutcome.Conflict, code, "Another reviewer has already changed the authoritative decision.", correlationId);
}
