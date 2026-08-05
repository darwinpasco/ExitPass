using System.Security.Cryptography;
using ExitPass.CentralPms.Application.StatutoryDiscounts;

namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public sealed class StatutoryEvidenceChannelService : IStatutoryEvidenceChannelService
{
    private readonly IStatutoryEvidenceMetadataRepository _metadataRepository;
    private readonly IStatutoryEvidenceMetadataService _metadataService;
    private readonly IStatutoryEvidenceUploadRepository _uploadRepository;
    private readonly IStatutoryEvidenceUploadService _uploadService;
    private readonly IStatutoryEvidenceProtectedObjectStorageAdapter _storageAdapter;
    private readonly IStatutoryDiscountDecisionFacadeService _decisionService;
    private readonly StatutoryEvidenceChannelOptions _channelOptions;
    private readonly StatutoryEvidenceUploadOptions _uploadOptions;
    private readonly StatutoryEvidenceScanWorkerOptions _scanOptions;

    public StatutoryEvidenceChannelService(
        IStatutoryEvidenceMetadataRepository metadataRepository,
        IStatutoryEvidenceMetadataService metadataService,
        IStatutoryEvidenceUploadRepository uploadRepository,
        IStatutoryEvidenceUploadService uploadService,
        IStatutoryEvidenceProtectedObjectStorageAdapter storageAdapter,
        IStatutoryDiscountDecisionFacadeService decisionService,
        StatutoryEvidenceChannelOptions channelOptions,
        StatutoryEvidenceUploadOptions uploadOptions,
        StatutoryEvidenceScanWorkerOptions scanOptions)
    {
        _metadataRepository = metadataRepository;
        _metadataService = metadataService;
        _uploadRepository = uploadRepository;
        _uploadService = uploadService;
        _storageAdapter = storageAdapter;
        _decisionService = decisionService;
        _channelOptions = channelOptions;
        _uploadOptions = uploadOptions;
        _scanOptions = scanOptions;
    }

    public async Task<StatutoryEvidenceChannelResponse> BootstrapAsync(
        StatutoryEvidenceChannelBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedChannel(command.SourceChannel) ||
            command.StatutoryDiscountDecisionCommandId == Guid.Empty)
        {
            return Rejected(command.SourceChannel, command.CorrelationId, "INVALID_REQUEST");
        }

        var binding = await _metadataRepository.ResolveRequestBindingAsync(command.StatutoryDiscountDecisionCommandId, cancellationToken);
        if (binding is null)
        {
            await _metadataRepository.RecordAccessDeniedAsync(null, null, null, null, command.CorrelationId, command.Actor, "UNKNOWN_STATUTORY_REQUEST", cancellationToken);
            return Rejected(command.SourceChannel, command.CorrelationId, "UNKNOWN_CONTEXT");
        }

        if (!SourceMatches(command.SourceChannel, command.Actor, binding) ||
            !await _metadataRepository.ActorHasScopeAsync(command.Actor, StatutoryEvidenceScopeOperations.Capture, binding.SiteId, binding.SiteGroupId, cancellationToken))
        {
            await _metadataRepository.RecordAccessDeniedAsync(null, binding.SiteId, binding.SiteGroupId, binding.ParkingSessionId, command.CorrelationId, command.Actor, "SCOPE_DENIED", cancellationToken);
            return Rejected(command.SourceChannel, command.CorrelationId, "SCOPE_DENIED");
        }

        var decision = await _decisionService.GetAsync(command.StatutoryDiscountDecisionCommandId, command.CorrelationId, cancellationToken);
        if (decision is null)
        {
            return Rejected(command.SourceChannel, command.CorrelationId, "UNKNOWN_CONTEXT");
        }

        if (!decision.EvidenceRequired)
        {
            return BuildResponse(command.SourceChannel, command.CorrelationId, null, null, decision, "NOT_REQUIRED", false, null, false, null);
        }

        var existing = await _metadataRepository.GetEvidenceSetByDecisionCommandIdAsync(command.StatutoryDiscountDecisionCommandId, cancellationToken);
        if (existing is not null)
        {
            return BuildResponse(command.SourceChannel, command.CorrelationId, existing, existing.Items.FirstOrDefault(), decision, MapLifecycle(existing, existing.Items.FirstOrDefault(), decision), true, null, false, null);
        }

        var governance = await ResolveGovernanceAsync(binding, cancellationToken);
        if (governance is null)
        {
            return Rejected(command.SourceChannel, command.CorrelationId, "GOVERNANCE_PROFILE_UNAVAILABLE");
        }

        var operationKey = StableOperationKey(command.ClientOperationKey, command.StatutoryDiscountDecisionCommandId);
        var createSet = await _metadataService.CreateOrResolveSetAsync(
            new StatutoryEvidenceCreateSetCommand(
                binding.StatutoryDiscountDecisionCommandId,
                binding.StatutoryDiscountValidationId,
                binding.ParkingSessionId,
                binding.SiteId,
                binding.SiteGroupId,
                binding.EntitlementType,
                governance.RequiredDocumentProfileCode,
                governance.RequiredDocumentProfileVersion,
                governance.RetentionClassCode,
                governance.RetentionPolicyVersion,
                governance.EnvironmentScope,
                $"{command.SourceChannel}:evidence-bootstrap",
                operationKey,
                command.CorrelationId,
                command.Actor),
            cancellationToken);
        if (createSet.Classification is "REJECTED" or "SEMANTIC_CONFLICT" || createSet.EvidenceSet is null)
        {
            return Rejected(command.SourceChannel, command.CorrelationId, createSet.ErrorCode ?? createSet.Classification);
        }

        var addItem = await _metadataService.AddItemAsync(
            new StatutoryEvidenceAddItemCommand(
                createSet.EvidenceSet.EvidenceSetReference,
                governance.DocumentType,
                governance.ItemRole,
                governance.ExpectedMediaClass,
                null,
                governance.RequiredDocumentProfileCode,
                $"{command.SourceChannel}:evidence-bootstrap-item",
                operationKey,
                command.CorrelationId,
                command.Actor),
            cancellationToken);

        if (addItem.Classification is "REJECTED" or "SEMANTIC_CONFLICT" || addItem.EvidenceSet is null)
        {
            return Rejected(command.SourceChannel, command.CorrelationId, addItem.ErrorCode ?? addItem.Classification);
        }

        return BuildResponse(command.SourceChannel, command.CorrelationId, addItem.EvidenceSet, addItem.EvidenceItem, decision, "ITEM_CREATED", true, null, false, null);
    }

    public async Task<StatutoryEvidenceChannelResponse> GetStatusAsync(
        StatutoryEvidenceChannelStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedChannel(query.SourceChannel) ||
            (query.StatutoryDiscountDecisionCommandId is null && query.EvidenceSetReference is null))
        {
            return Rejected(query.SourceChannel, query.CorrelationId, "INVALID_REQUEST");
        }

        StatutoryEvidenceSetReadModel? set = null;
        if (query.EvidenceSetReference is Guid reference)
        {
            set = await _metadataRepository.GetEvidenceSetAsync(reference, cancellationToken);
            if (set is not null && !await IsAuthorizedForSetAsync(set, query.Actor, query.CorrelationId, cancellationToken))
            {
                return Rejected(query.SourceChannel, query.CorrelationId, "SCOPE_DENIED");
            }
        }
        else if (query.StatutoryDiscountDecisionCommandId is Guid decisionCommandId)
        {
            set = await _metadataRepository.GetEvidenceSetByDecisionCommandIdAsync(decisionCommandId, cancellationToken);
            if (set is not null && !await IsAuthorizedForSetAsync(set, query.Actor, query.CorrelationId, cancellationToken))
            {
                return Rejected(query.SourceChannel, query.CorrelationId, "SCOPE_DENIED");
            }
        }

        var decisionId = query.StatutoryDiscountDecisionCommandId ?? set?.StatutoryDiscountDecisionCommandId;
        var decision = decisionId is Guid id
            ? await _decisionService.GetAsync(id, query.CorrelationId, cancellationToken)
            : null;
        if (decision is null)
        {
            return Rejected(query.SourceChannel, query.CorrelationId, "UNKNOWN_CONTEXT");
        }

        if (!decision.EvidenceRequired)
        {
            return BuildResponse(query.SourceChannel, query.CorrelationId, null, null, decision, "NOT_REQUIRED", false, null, false, null);
        }

        var item = set?.Items.FirstOrDefault();
        var lifecycle = set is null
            ? "REQUIRED_NOT_STARTED"
            : MapLifecycle(set, item, decision);
        return BuildResponse(query.SourceChannel, query.CorrelationId, set, item, decision, lifecycle, true, null, false, null);
    }

    public async Task<StatutoryEvidenceChannelReadiness> GetAptEvidenceReadinessAsync(
        Guid? statutoryDiscountDecisionCommandId,
        StatutoryEvidenceActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountDecisionCommandId is null)
        {
            return new StatutoryEvidenceChannelReadiness("NOT_REQUIRED", false, true, false, null, "No statutory evidence is required.");
        }

        var decision = await _decisionService.GetAsync(statutoryDiscountDecisionCommandId.Value, correlationId, cancellationToken);
        if (decision is null)
        {
            return new StatutoryEvidenceChannelReadiness("UNKNOWN_FAIL_CLOSED", true, false, false, "STATUTORY_EVIDENCE_CONTEXT_UNAVAILABLE", "Statutory evidence context is unavailable.");
        }

        if (!decision.EvidenceRequired)
        {
            return new StatutoryEvidenceChannelReadiness("NOT_REQUIRED", false, true, false, null, "No statutory evidence is required.");
        }

        var set = await _metadataRepository.GetEvidenceSetByDecisionCommandIdAsync(statutoryDiscountDecisionCommandId.Value, cancellationToken);
        var item = set?.Items.FirstOrDefault();
        var lifecycle = set is null
            ? "REQUIRED_NOT_STARTED"
            : MapLifecycle(set, item, decision);

        var ready = StatutoryEvidenceChannelConstants.ReadyEvidenceStatuses.Contains(lifecycle);
        return new StatutoryEvidenceChannelReadiness(
            lifecycle,
            true,
            ready,
            lifecycle is "SCAN_RETRYABLE",
            ready ? null : BlockingReason(lifecycle) ?? "STATUTORY_EVIDENCE_NOT_READY",
            ready ? "Statutory evidence readiness passed." : "Statutory evidence is not ready for cash acceptance.");
    }

    public async Task<StatutoryEvidenceOpaqueUploadSessionResponse> CreateUploadSessionAsync(
        StatutoryEvidenceChannelUploadSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _uploadService.AuthorizeUploadAsync(
            new StatutoryEvidenceUploadAuthorizationCommand(
                command.EvidenceSetReference,
                command.EvidenceItemReference,
                command.DeclaredContentType,
                command.DeclaredContentLength,
                "IMAGE",
                StatutoryEvidenceUploadConstants.ChecksumAlgorithmSha256,
                command.DeclaredChecksumSha256,
                $"{command.SourceChannel}:opaque-upload-session",
                StableOperationKey(command.ClientOperationKey, command.EvidenceItemReference),
                command.CorrelationId,
                command.Actor),
            cancellationToken);

        if (result.Classification is "REJECTED" or "SEMANTIC_CONFLICT" || result.UploadAuthorization is null)
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse(result.Classification, result.Retryable, result.ErrorCode, command.CorrelationId, null, StatutoryEvidenceUploadConstants.UploadMethodPut, null, command.DeclaredContentType, _uploadOptions.MaxContentLengthBytes);
        }

        return new StatutoryEvidenceOpaqueUploadSessionResponse(
            result.Classification,
            result.Retryable,
            result.ErrorCode,
            command.CorrelationId,
            result.UploadAuthorization.UploadAuthorizationReference,
            StatutoryEvidenceUploadConstants.UploadMethodPut,
            result.UploadAuthorization.ExpiresAt,
            result.UploadAuthorization.AcceptedContentType,
            result.UploadAuthorization.MaxContentLengthBytes);
    }

    public async Task<StatutoryEvidenceOpaqueUploadSessionResponse> UploadAsync(
        StatutoryEvidenceChannelUploadCommand command,
        CancellationToken cancellationToken)
    {
        var session = await ResolveAuthorizedUploadSessionAsync(command, cancellationToken);
        if (session is null)
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, "SCOPE_DENIED", command.CorrelationId, null, StatutoryEvidenceUploadConstants.UploadMethodPut, null, command.ContentType ?? string.Empty, _uploadOptions.MaxContentLengthBytes);
        }

        var authorization = session.Authorization;
        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _uploadRepository.ExpireUploadAuthorizationsAsync(session.Target, DateTimeOffset.UtcNow, command.CorrelationId, command.Actor, cancellationToken);
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, "AUTHORIZATION_EXPIRED", command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        if (authorization.AuthorizationStatus != "ISSUED")
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, "AUTHORIZATION_NOT_USABLE", command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        if (session.Target.EvidenceItem.UploadStatus != "AUTHORIZED" ||
            session.Target.EvidenceSet.SetStatus is "LOCKED_FOR_REVIEW" or "TOMBSTONED" ||
            session.Target.EvidenceItem.DeletionStatus is "REQUESTED" or "DELETED")
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, "AUTHORIZATION_NOT_USABLE", command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        if (!string.Equals(command.ContentType, authorization.ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, StatutoryEvidenceUploadConstants.ContentTypeMismatch, command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        if (command.ContentLength is null || command.ContentLength != authorization.ExpectedContentLength || command.ContentLength > _uploadOptions.MaxContentLengthBytes)
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, StatutoryEvidenceUploadConstants.ContentLengthMismatch, command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        var hashingStream = new HashingBoundedReadStream(command.Content, authorization.ExpectedContentLength);
        try
        {
            var uploadResult = await _storageAdapter.UploadObjectAsync(
                new StatutoryEvidenceObjectUploadRequest(
                    ResolveBucketName(),
                    authorization.InternalObjectKey,
                    authorization.ExpectedContentType,
                    authorization.ExpectedContentLength,
                    authorization.ExpectedChecksumSha256,
                    hashingStream),
                cancellationToken);

            if (uploadResult.Classification != "ACCEPTED")
            {
                return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", uploadResult.Retryable, uploadResult.Classification, command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
            }
        }
        catch (InvalidOperationException)
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", true, StatutoryEvidenceUploadConstants.ProviderUnavailable, command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        if (hashingStream.TotalBytes != authorization.ExpectedContentLength ||
            !string.Equals(hashingStream.Sha256Hex, authorization.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new StatutoryEvidenceOpaqueUploadSessionResponse("REJECTED", false, StatutoryEvidenceUploadConstants.ChecksumMismatch, command.CorrelationId, authorization.UploadAuthorizationReference, StatutoryEvidenceUploadConstants.UploadMethodPut, authorization.ExpiresAt, authorization.ExpectedContentType, _uploadOptions.MaxContentLengthBytes);
        }

        return new StatutoryEvidenceOpaqueUploadSessionResponse(
            "ACCEPTED",
            false,
            null,
            command.CorrelationId,
            authorization.UploadAuthorizationReference,
            StatutoryEvidenceUploadConstants.UploadMethodPut,
            authorization.ExpiresAt,
            authorization.ExpectedContentType,
            _uploadOptions.MaxContentLengthBytes);
    }

    public async Task<StatutoryEvidenceChannelResponse> FinalizeUploadSessionAsync(
        StatutoryEvidenceChannelFinalizeCommand command,
        CancellationToken cancellationToken)
    {
        var session = await _uploadRepository.GetUploadSessionAsync(command.OpaqueUploadSessionReference, cancellationToken);
        if (session is null || !await IsAuthorizedForSetAsync(session.Target.EvidenceSet, command.Actor, command.CorrelationId, cancellationToken))
        {
            return Rejected(command.SourceChannel, command.CorrelationId, "SCOPE_DENIED");
        }

        var result = await _uploadService.FinalizeUploadAsync(
            new StatutoryEvidenceUploadFinalizationCommand(
                session.Target.EvidenceSet.EvidenceSetReference,
                session.Target.EvidenceItem.EvidenceItemReference,
                command.OpaqueUploadSessionReference,
                $"{command.SourceChannel}:opaque-upload-finalize",
                StableOperationKey(command.ClientOperationKey, command.OpaqueUploadSessionReference),
                command.CorrelationId,
                command.Actor),
            cancellationToken);
        if (result.Classification is "REJECTED" or "SEMANTIC_CONFLICT")
        {
            return Rejected(command.SourceChannel, command.CorrelationId, result.ErrorCode ?? result.Classification, result.Retryable);
        }

        var set = await _metadataRepository.GetEvidenceSetAsync(session.Target.EvidenceSet.EvidenceSetReference, cancellationToken);
        var decision = await _decisionService.GetAsync(session.Target.EvidenceSet.StatutoryDiscountDecisionCommandId, command.CorrelationId, cancellationToken);
        return BuildResponse(command.SourceChannel, command.CorrelationId, set, result.EvidenceItem, decision, set is null ? "UNKNOWN_FAIL_CLOSED" : MapLifecycle(set, result.EvidenceItem, decision), true, null, result.Retryable, result.ErrorCode);
    }

    private async Task<StatutoryEvidenceUploadSession?> ResolveAuthorizedUploadSessionAsync(
        StatutoryEvidenceChannelUploadCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedChannel(command.SourceChannel) || command.OpaqueUploadSessionReference == Guid.Empty)
        {
            return null;
        }

        var session = await _uploadRepository.GetUploadSessionAsync(command.OpaqueUploadSessionReference, cancellationToken);
        if (session is null)
        {
            return null;
        }

        return await IsAuthorizedForSetAsync(session.Target.EvidenceSet, command.Actor, command.CorrelationId, cancellationToken)
            ? session
            : null;
    }

    private async Task<bool> IsAuthorizedForSetAsync(
        StatutoryEvidenceSetReadModel set,
        StatutoryEvidenceActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!StatutoryEvidenceMetadataConstants.CodeComparer.Equals(actor.SourceChannel, set.SourceChannel) ||
            !await _metadataRepository.ActorHasScopeAsync(actor, StatutoryEvidenceScopeOperations.Capture, set.SiteId, set.SiteGroupId, cancellationToken))
        {
            await _metadataRepository.RecordAccessDeniedAsync(set.EvidenceSetReference, set.SiteId, set.SiteGroupId, set.ParkingSessionId, correlationId, actor, "SCOPE_DENIED", cancellationToken);
            return false;
        }

        return true;
    }

    private async Task<GovernanceSelection?> ResolveGovernanceAsync(
        StatutoryEvidenceDurableRequestBinding binding,
        CancellationToken cancellationToken)
    {
        var retention = await _metadataRepository.FindApprovedRetentionPolicyAsync(_channelOptions.EnvironmentScope, cancellationToken);
        if (retention is null)
        {
            return null;
        }

        var entitlement = NormalizeCode(binding.EntitlementType);
        var documentType = entitlement == "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID";
        var profile = entitlement == "PWD"
            ? _channelOptions.PwdDocumentProfileCode
            : _channelOptions.SeniorCitizenDocumentProfileCode;
        return new GovernanceSelection(
            documentType,
            _channelOptions.SingleDocumentItemRole,
            _channelOptions.ExpectedJpegMediaClass,
            profile,
            _channelOptions.RequiredDocumentProfileVersion,
            retention.RetentionClassCode,
            retention.RetentionPolicyVersion,
            retention.EnvironmentScope);
    }

    private StatutoryEvidenceChannelResponse BuildResponse(
        string sourceChannel,
        Guid correlationId,
        StatutoryEvidenceSetReadModel? set,
        StatutoryEvidenceItemReadModel? item,
        StatutoryDiscountDecisionResult? decision,
        string lifecycle,
        bool evidenceRequired,
        string? blockingReasonCode,
        bool retryable,
        string? errorCode)
    {
        var replacement = ResolveReplacementPosture(set, item, decision);
        return new StatutoryEvidenceChannelResponse(
            string.IsNullOrWhiteSpace(errorCode) ? "ACCEPTED" : "REJECTED",
            retryable,
            errorCode,
            correlationId,
            sourceChannel,
            evidenceRequired,
            set?.EvidenceSetReference,
            item?.EvidenceItemReference,
            _uploadOptions.AllowedContentTypes.Where(StatutoryEvidenceUploadConstants.SupportedContentTypes.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _uploadOptions.MaxContentLengthBytes,
            _scanOptions.MaxDecodedWidth > 0 ? _scanOptions.MaxDecodedWidth : null,
            _scanOptions.MaxDecodedHeight > 0 ? _scanOptions.MaxDecodedHeight : null,
            _scanOptions.MaxDecodedPixelCount > 0 ? _scanOptions.MaxDecodedPixelCount : null,
            item?.DocumentType,
            item?.ItemRole,
            lifecycle,
            replacement,
            lifecycle is "REVIEWABLE" or "REVIEW_PENDING" or "APPROVED" or "APPLIED",
            lifecycle is "NOT_REQUIRED" or "APPLIED",
            blockingReasonCode ?? BlockingReason(lifecycle),
            DateTimeOffset.UtcNow);
    }

    private StatutoryEvidenceChannelResponse Rejected(string sourceChannel, Guid correlationId, string code, bool retryable = false) =>
        new("REJECTED", retryable, code, correlationId, sourceChannel, true, null, null, [], 0, null, null, null, null, null, "UNKNOWN_FAIL_CLOSED", "REPLACEMENT_NOT_ALLOWED", false, false, code, DateTimeOffset.UtcNow);

    private static string MapLifecycle(StatutoryEvidenceSetReadModel set, StatutoryEvidenceItemReadModel? item, StatutoryDiscountDecisionResult? decision)
    {
        if (decision?.PayableBasisReady == true)
        {
            return "APPLIED";
        }

        if (decision?.DecisionStatus == "REJECTED")
        {
            return "REJECTED";
        }

        if (decision?.DecisionStatus == "APPROVED")
        {
            return "APPROVED";
        }

        if (item is null)
        {
            return "REQUIRED_NOT_STARTED";
        }

        if (item.ScanResultClassification is "MALICIOUS" or "SUSPICIOUS" || item.ScanStatus is "MALICIOUS" or "SUSPICIOUS")
        {
            return "MALWARE_DETECTED";
        }

        if (item.ValidationStatus is "FAILED" or "UNSUPPORTED" || item.ValidationResultClassification == "FAILED")
        {
            return "VALIDATION_FAILED";
        }

        if (item.ScanStatus is "ERROR_RETRYABLE" or "UNAVAILABLE" or "TIMEOUT" || item.ScanResultClassification is "ERROR_RETRYABLE" or "UNAVAILABLE")
        {
            return "SCAN_RETRYABLE";
        }

        if (item.ScanStatus is "ERROR_TERMINAL" || item.ScanResultClassification is "ERROR_TERMINAL")
        {
            return "SCAN_FAILED";
        }

        if (item.UploadStatus == "AUTHORIZED")
        {
            return "UPLOAD_SESSION_AVAILABLE";
        }

        if (item.UploadStatus == "UPLOADING")
        {
            return "UPLOAD_IN_PROGRESS";
        }

        if (item.UploadStatus != "UPLOADED")
        {
            return "ITEM_CREATED";
        }

        if (item.ValidationStatus is "NOT_STARTED" or "PENDING" or "IN_PROGRESS" or "RETRY_PENDING")
        {
            return "VALIDATION_PENDING";
        }

        if (item.ScanStatus is "NOT_STARTED" or "PENDING" or "IN_PROGRESS" or "RETRY_PENDING")
        {
            return "SCAN_PENDING";
        }

        if (item.ReviewabilityStatus == "REVIEWABLE")
        {
            return "REVIEWABLE";
        }

        if (set.SetStatus == "LOCKED_FOR_REVIEW")
        {
            return "REVIEW_PENDING";
        }

        return "NOT_REVIEWABLE";
    }

    private static string ResolveReplacementPosture(StatutoryEvidenceSetReadModel? set, StatutoryEvidenceItemReadModel? item, StatutoryDiscountDecisionResult? decision)
    {
        if (set is null || item is null)
        {
            return "REPLACEMENT_ALLOWED";
        }

        if (set.SetStatus == "LOCKED_FOR_REVIEW" ||
            set.HoldActive ||
            decision?.DecisionStatus is "APPROVED" or "REJECTED" ||
            decision?.PayableBasisReady == true ||
            item.UploadStatus == "UPLOADED" && item.ScanResultClassification is null)
        {
            return "REPLACEMENT_NOT_ALLOWED";
        }

        return "REPLACEMENT_ALLOWED";
    }

    private static string? BlockingReason(string lifecycle) =>
        lifecycle switch
        {
            "NOT_REQUIRED" or "APPLIED" => null,
            "REQUIRED_NOT_STARTED" => "STATUTORY_EVIDENCE_REQUIRED_NOT_STARTED",
            "UPLOAD_SESSION_AVAILABLE" or "UPLOAD_IN_PROGRESS" => "STATUTORY_EVIDENCE_UPLOAD_PENDING",
            "VALIDATION_PENDING" => "STATUTORY_EVIDENCE_VALIDATION_PENDING",
            "VALIDATION_FAILED" => "STATUTORY_EVIDENCE_VALIDATION_FAILED",
            "SCAN_PENDING" => "STATUTORY_EVIDENCE_SCAN_PENDING",
            "SCAN_RETRYABLE" => "STATUTORY_EVIDENCE_SCAN_RETRYABLE",
            "SCAN_FAILED" => "STATUTORY_EVIDENCE_SCAN_FAILED",
            "MALWARE_DETECTED" => "STATUTORY_EVIDENCE_MALWARE_DETECTED",
            "REVIEW_PENDING" => "STATUTORY_EVIDENCE_REVIEW_PENDING",
            "REJECTED" => "STATUTORY_EVIDENCE_REJECTED",
            "APPROVED" => "STATUTORY_EVIDENCE_APPROVED_NOT_APPLIED",
            _ => "STATUTORY_EVIDENCE_NOT_READY"
        };

    private string ResolveBucketName() =>
        !string.IsNullOrWhiteSpace(_uploadOptions.BucketName)
            ? _uploadOptions.BucketName
            : throw new InvalidOperationException("Evidence upload storage bucket is not configured.");

    private static bool SourceMatches(string routeSourceChannel, StatutoryEvidenceActor actor, StatutoryEvidenceDurableRequestBinding binding) =>
        StatutoryEvidenceMetadataConstants.CodeComparer.Equals(routeSourceChannel, actor.SourceChannel) &&
        StatutoryEvidenceMetadataConstants.CodeComparer.Equals(routeSourceChannel, binding.SourceChannel);

    private static bool IsSupportedChannel(string sourceChannel) =>
        StatutoryEvidenceChannelConstants.WebPay.Equals(sourceChannel, StringComparison.OrdinalIgnoreCase) ||
        StatutoryEvidenceChannelConstants.AssistedPaymentTerminal.Equals(sourceChannel, StringComparison.OrdinalIgnoreCase);

    private static string StableOperationKey(string? clientOperationKey, Guid durableReference) =>
        string.IsNullOrWhiteSpace(clientOperationKey)
            ? durableReference.ToString("N")
            : clientOperationKey.Trim();

    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private sealed record GovernanceSelection(
        string DocumentType,
        string ItemRole,
        string ExpectedMediaClass,
        string RequiredDocumentProfileCode,
        string RequiredDocumentProfileVersion,
        string RetentionClassCode,
        string RetentionPolicyVersion,
        string EnvironmentScope);

    private sealed class HashingBoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumBytes;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _totalBytes;
        private string? _sha256Hex;

        public HashingBoundedReadStream(Stream inner, long maximumBytes)
        {
            _inner = inner;
            _maximumBytes = maximumBytes;
        }

        public long TotalBytes => _totalBytes;

        public string Sha256Hex => _sha256Hex ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return 0;
            }

            _totalBytes += read;
            if (_totalBytes > _maximumBytes)
            {
                throw new InvalidOperationException("Evidence upload exceeded the declared content length.");
            }

            _hash.AppendData(buffer.Span[..read]);
            return read;
        }
    }
}
