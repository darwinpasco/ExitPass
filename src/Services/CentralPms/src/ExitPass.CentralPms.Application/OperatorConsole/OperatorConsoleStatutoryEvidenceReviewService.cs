using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed class OperatorConsoleStatutoryEvidenceReviewService : IOperatorConsoleStatutoryEvidenceReviewService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ActionCode = OperatorConsoleActionCodes.ReviewEvidence;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleStatutoryEvidenceReviewRepository _repository;
    private readonly IStatutoryEvidenceProtectedObjectStorageAdapter _storage;
    private readonly StatutoryEvidenceUploadOptions _uploadOptions;

    public OperatorConsoleStatutoryEvidenceReviewService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryEvidenceReviewRepository repository,
        IStatutoryEvidenceProtectedObjectStorageAdapter storage,
        IOptions<StatutoryEvidenceUploadOptions> uploadOptions)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _uploadOptions = uploadOptions?.Value ?? throw new ArgumentNullException(nameof(uploadOptions));
    }

    public async Task<OperatorConsoleStatutoryEvidenceReviewResult?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        ValidateGuid(statutoryDiscountDecisionCommandId, nameof(statutoryDiscountDecisionCommandId));
        ArgumentNullException.ThrowIfNull(accessContext);

        var access = await EvaluateAndPersistAsync(accessContext, cancellationToken).ConfigureAwait(false);
        if (!HasDurableScope(access))
        {
            await RecordDeniedAsync(accessContext, "OPERATOR_CONSOLE_EVIDENCE_REVIEW_ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Operator Console statutory evidence review access was denied.");
        }

        var record = await _repository.ReadAsync(statutoryDiscountDecisionCommandId, cancellationToken).ConfigureAwait(false);
        if (record is null || !ScopeMatches(record, access))
        {
            await RecordDeniedAsync(accessContext, "OPERATOR_CONSOLE_EVIDENCE_REVIEW_NOT_FOUND", cancellationToken).ConfigureAwait(false);
            return null;
        }

        await _repository.RecordAccessEventAsync(
            AccessEvent(
                "ACCESS_ALLOWED",
                "ALLOWED",
                "OPERATOR_CONSOLE_EVIDENCE_METADATA_READ",
                record,
                item: null,
                accessContext),
            cancellationToken).ConfigureAwait(false);

        return ToResult(record, accessContext.CorrelationId);
    }

    public async Task<OperatorConsoleStatutoryEvidencePreviewResult> OpenPreviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid evidenceItemReference,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        ValidateGuid(statutoryDiscountDecisionCommandId, nameof(statutoryDiscountDecisionCommandId));
        ValidateGuid(evidenceItemReference, nameof(evidenceItemReference));
        ArgumentNullException.ThrowIfNull(accessContext);

        var access = await EvaluateAndPersistAsync(accessContext, cancellationToken).ConfigureAwait(false);
        if (!HasDurableScope(access))
        {
            await RecordDeniedAsync(accessContext, "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Operator Console statutory evidence preview access was denied.");
        }

        var record = await _repository.ReadAsync(statutoryDiscountDecisionCommandId, cancellationToken).ConfigureAwait(false);
        if (record is null || !ScopeMatches(record, access))
        {
            await RecordDeniedAsync(accessContext, "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_NOT_FOUND", cancellationToken).ConfigureAwait(false);
            return Rejected("NOT_FOUND", accessContext.CorrelationId);
        }

        var item = record.Items.SingleOrDefault(value => value.EvidenceItemReference == evidenceItemReference);
        if (item is null)
        {
            await _repository.RecordAccessEventAsync(
                AccessEvent("ACCESS_DENIED", "DENIED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_NOT_FOUND", record, null, accessContext),
                cancellationToken).ConfigureAwait(false);
            return Rejected("NOT_FOUND", accessContext.CorrelationId);
        }

        var denialReason = PreviewDenialReason(record, item);
        if (denialReason is not null)
        {
            await _repository.RecordAccessEventAsync(
                AccessEvent("ACCESS_DENIED", "DENIED", denialReason, record, item, accessContext),
                cancellationToken).ConfigureAwait(false);
            return Rejected(denialReason, accessContext.CorrelationId);
        }

        if (_uploadOptions.MaxContentLengthBytes <= 0 || string.IsNullOrWhiteSpace(_uploadOptions.BucketName))
        {
            const string reason = "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_NOT_CONFIGURED";
            await _repository.RecordAccessEventAsync(
                AccessEvent("ACCESS_DENIED", "FAILED", reason, record, item, accessContext),
                cancellationToken).ConfigureAwait(false);
            return Rejected(reason, accessContext.CorrelationId, retryable: false);
        }

        var target = BuildTarget(record, item, accessContext);
        StatutoryEvidenceObjectContent content;
        try
        {
            content = await _storage.OpenObjectContentStreamAsync(
                    new StatutoryEvidenceObjectContentRequest(
                        _uploadOptions.BucketName,
                        target.InternalObjectKey,
                        _uploadOptions.MaxContentLengthBytes),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            const string reason = "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE";
            await _repository.RecordAccessEventAsync(
                AccessEvent("ACCESS_ALLOWED", "FAILED", reason, record, item, accessContext),
                cancellationToken).ConfigureAwait(false);
            return Rejected(reason, accessContext.CorrelationId, retryable: true);
        }

        if (!ObjectMetadataMatches(target, content) ||
            !await _repository.IsCurrentPreviewTargetAsync(target, cancellationToken).ConfigureAwait(false))
        {
            await content.DisposeAsync().ConfigureAwait(false);
            const string reason = "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STALE";
            await _repository.RecordAccessEventAsync(
                AccessEvent("ACCESS_DENIED", "DENIED", reason, record, item, accessContext),
                cancellationToken).ConfigureAwait(false);
            return Rejected(reason, accessContext.CorrelationId);
        }

        await _repository.RecordAccessEventAsync(
            AccessEvent("ACCESS_ALLOWED", "ALLOWED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STARTED", record, item, accessContext),
            cancellationToken).ConfigureAwait(false);

        return new OperatorConsoleStatutoryEvidencePreviewResult(
            "ACCEPTED",
            null,
            false,
            accessContext.CorrelationId,
            content,
            new OperatorConsoleStatutoryEvidencePreviewAuditContext(
                target,
                new StatutoryEvidenceActor(accessContext.UserId, null, OperatorConsoleStatutoryEvidenceReviewConstants.SourceChannel)));
    }

    public Task RecordPreviewStreamOutcomeAsync(
        OperatorConsoleStatutoryEvidencePreviewAuditContext context,
        string outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var normalized = outcome?.Trim().ToUpperInvariant();
        var (eventResult, reasonCode) = normalized switch
        {
            "COMPLETED" => ("ALLOWED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_COMPLETED"),
            "CANCELLED" => ("FAILED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_CANCELLED"),
            _ => ("FAILED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STREAM_FAILED")
        };

        return _repository.RecordAccessEventAsync(
            new OperatorConsoleStatutoryEvidenceAccessEvent(
                "ACCESS_ALLOWED",
                eventResult,
                reasonCode,
                context.Target.EvidenceSetId,
                context.Target.EvidenceItemId,
                context.Target.SiteId,
                context.Target.SiteGroupId,
                context.Target.ParkingSessionId,
                context.Target.CorrelationId,
                context.Actor),
            cancellationToken);
    }

    private async Task<OperatorConsoleAccessEvaluationResult> EvaluateAndPersistAsync(
        OperatorConsoleReviewAccessContext context,
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
                    ActionCode,
                    ParkingSessionId: null,
                    EvidenceAccessIntent: "REVIEW_PREVIEW",
                    context.IdempotencyKey,
                    context.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);

        return await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken).ConfigureAwait(false);
    }

    private Task RecordDeniedAsync(
        OperatorConsoleReviewAccessContext accessContext,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _repository.RecordAccessEventAsync(
            new OperatorConsoleStatutoryEvidenceAccessEvent(
                "ACCESS_DENIED",
                "DENIED",
                reasonCode,
                null,
                null,
                null,
                null,
                null,
                accessContext.CorrelationId,
                new StatutoryEvidenceActor(accessContext.UserId, null, OperatorConsoleStatutoryEvidenceReviewConstants.SourceChannel)),
            cancellationToken);

    private static bool HasDurableScope(OperatorConsoleAccessEvaluationResult access) =>
        access.Allowed &&
        access.SiteContext.Assigned &&
        access.SiteContext.SiteId.HasValue &&
        access.SiteContext.SiteGroupId.HasValue;

    private static bool ScopeMatches(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        OperatorConsoleAccessEvaluationResult access) =>
        access.SiteContext.SiteId == record.SiteId &&
        access.SiteContext.SiteGroupId == record.SiteGroupId;

    private static OperatorConsoleStatutoryEvidenceReviewResult ToResult(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        Guid correlationId) =>
        new(
            record.StatutoryDiscountDecisionCommandId,
            record.EvidenceSetReference,
            record.SourceChannel,
            record.DecisionResultStatus,
            record.ReviewStatus,
            record.EvidenceRequired,
            record.EvidenceRecorded,
            record.SetStatus,
            record.RetentionStatus,
            record.DeletionStatus,
            record.HoldActive,
            ReplacementPosture(record),
            record.Items.Select(item =>
            {
                var denial = PreviewDenialReason(record, item);
                return new OperatorConsoleStatutoryEvidenceReviewItemResult(
                    item.EvidenceItemReference,
                    item.DocumentType,
                    item.ItemRole,
                    item.DeclaredContentType,
                    item.VerifiedContentType,
                    item.VerifiedContentLength,
                    item.UploadStatus,
                    item.ValidationStatus,
                    item.ScanStatus,
                    item.ReviewabilityStatus,
                    item.BindingStatus,
                    item.RetentionStatus,
                    item.DeletionStatus,
                    item.HoldActive,
                    item.UploadedAt,
                    item.FinalizedAt,
                    item.ScanCompletedAt,
                    item.ScanCompletedAt,
                    item.ReviewableAt,
                    denial is null,
                    denial);
            }).ToArray(),
            correlationId);

    private static string ReplacementPosture(OperatorConsoleStatutoryEvidenceReviewRecord record) =>
        record.SetStatus == "LOCKED_FOR_REVIEW" ||
        record.HoldActive ||
        record.DecisionResultStatus is "APPROVED" or "REJECTED" ||
        record.ReviewStatus is "APPROVED" or "REJECTED"
            ? "REPLACEMENT_NOT_ALLOWED"
            : "REPLACEMENT_ALLOWED";

    private static string? PreviewDenialReason(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        OperatorConsoleStatutoryEvidenceReviewItemRecord item)
    {
        if (!record.EvidenceRequired)
        {
            return "STATUTORY_EVIDENCE_NOT_REQUIRED";
        }

        if (record.EvidenceSetId is null || record.EvidenceSetReference is null)
        {
            return "STATUTORY_EVIDENCE_MISSING";
        }

        if (record.SetStatus == "TOMBSTONED" || item.BindingStatus == "SUPERSEDED")
        {
            return "STATUTORY_EVIDENCE_STALE";
        }

        if (record.DeletionStatus != "NOT_REQUESTED" || item.DeletionStatus != "NOT_REQUESTED")
        {
            return "STATUTORY_EVIDENCE_DELETION_IN_PROGRESS";
        }

        if (record.RetentionStatus is not ("ACTIVE" or "HELD") || item.RetentionStatus is not ("ACTIVE" or "HELD"))
        {
            return "STATUTORY_EVIDENCE_RETENTION_INACCESSIBLE";
        }

        if (item.UploadStatus != "UPLOADED" ||
            item.AuthorizationStatus != "CONSUMED" ||
            item.UploadAuthorizationId is null ||
            item.UploadAuthorizationReference is null ||
            item.UploadAuthorizationRowVersion is null ||
            record.SetRowVersion is null ||
            string.IsNullOrWhiteSpace(item.InternalObjectKey) ||
            string.IsNullOrWhiteSpace(item.VerifiedContentType) ||
            item.VerifiedContentLength is null or <= 0 ||
            string.IsNullOrWhiteSpace(item.VerifiedChecksumSha256))
        {
            return "STATUTORY_EVIDENCE_UPLOAD_NOT_FINALIZED";
        }

        if (item.ValidationStatus is "NOT_STARTED" or "PENDING" or "IN_PROGRESS" or "RETRY_PENDING")
        {
            return "STATUTORY_EVIDENCE_VALIDATION_PENDING";
        }

        if (item.ValidationStatus != "PASSED")
        {
            return "STATUTORY_EVIDENCE_VALIDATION_FAILED";
        }

        if (item.ScanStatus is "NOT_STARTED" or "PENDING" or "IN_PROGRESS" or "RETRY_PENDING")
        {
            return "STATUTORY_EVIDENCE_SCAN_PENDING";
        }

        if (item.ScanStatus is "ERROR_RETRYABLE" or "UNAVAILABLE" or "TIMEOUT")
        {
            return "STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE";
        }

        if (item.ScanStatus is "MALICIOUS" or "SUSPICIOUS")
        {
            return "STATUTORY_EVIDENCE_MALWARE_DETECTED";
        }

        if (item.ScanStatus is not ("CLEAN" or "PASSED"))
        {
            return "STATUTORY_EVIDENCE_SCAN_FAILED";
        }

        if (item.ReviewabilityStatus != "REVIEWABLE")
        {
            return "STATUTORY_EVIDENCE_NOT_REVIEWABLE";
        }

        if (item.BindingStatus is "REJECTED")
        {
            return "STATUTORY_EVIDENCE_BINDING_INVALID";
        }

        if (!OperatorConsoleStatutoryEvidenceReviewConstants.SupportedPreviewMediaTypes.Contains(item.VerifiedContentType) ||
            !string.Equals(item.DeclaredContentType, item.VerifiedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA";
        }

        if (!string.Equals(
                item.InternalStorageLocatorReference,
                $"upload-authorization:{item.UploadAuthorizationReference:D}",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(item.InternalChecksumSha256, item.VerifiedChecksumSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "STATUTORY_EVIDENCE_PREVIEW_STALE";
        }

        return null;
    }

    private static OperatorConsoleStatutoryEvidencePreviewTarget BuildTarget(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        OperatorConsoleStatutoryEvidenceReviewItemRecord item,
        OperatorConsoleReviewAccessContext accessContext) =>
        new(
            record.StatutoryDiscountDecisionCommandId,
            record.ParkingSessionId,
            record.SiteId,
            record.SiteGroupId,
            record.EvidenceSetId!.Value,
            record.EvidenceSetReference!.Value,
            record.SetRowVersion!.Value,
            item.EvidenceItemId,
            item.EvidenceItemReference,
            item.ItemRowVersion,
            item.UploadAuthorizationId!.Value,
            item.UploadAuthorizationReference!.Value,
            item.UploadAuthorizationRowVersion!.Value,
            item.InternalObjectKey!,
            item.VerifiedContentType!,
            item.VerifiedContentLength!.Value,
            item.VerifiedChecksumSha256!,
            item.ProviderObjectVersion,
            accessContext.CorrelationId,
            accessContext.UserId);

    private static bool ObjectMetadataMatches(
        OperatorConsoleStatutoryEvidencePreviewTarget target,
        StatutoryEvidenceObjectContent content) =>
        string.Equals(content.ContentType, target.ContentType, StringComparison.OrdinalIgnoreCase) &&
        content.ContentLength == target.ContentLength &&
        string.Equals(content.ChecksumSha256, target.ChecksumSha256, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(target.ProviderObjectVersion) ||
         string.Equals(content.ObjectVersion, target.ProviderObjectVersion, StringComparison.Ordinal));

    private static OperatorConsoleStatutoryEvidenceAccessEvent AccessEvent(
        string eventType,
        string eventResult,
        string reasonCode,
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        OperatorConsoleStatutoryEvidenceReviewItemRecord? item,
        OperatorConsoleReviewAccessContext accessContext) =>
        new(
            eventType,
            eventResult,
            reasonCode,
            record.EvidenceSetId,
            item?.EvidenceItemId,
            record.SiteId,
            record.SiteGroupId,
            record.ParkingSessionId,
            accessContext.CorrelationId,
            new StatutoryEvidenceActor(accessContext.UserId, null, OperatorConsoleStatutoryEvidenceReviewConstants.SourceChannel));

    private static OperatorConsoleStatutoryEvidencePreviewResult Rejected(
        string errorCode,
        Guid correlationId,
        bool retryable = false) =>
        new("REJECTED", errorCode, retryable, correlationId, null, null);

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
