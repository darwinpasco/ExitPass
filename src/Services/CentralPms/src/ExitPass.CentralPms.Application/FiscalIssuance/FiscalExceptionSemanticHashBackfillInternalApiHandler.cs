namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashBackfillInternalApiHandler
    : IFiscalExceptionSemanticHashBackfillInternalApiHandler
{
    private const int MaxReasonCodeLength = 80;
    private const int MaxSafeJustificationLength = 512;
    private const int StatusOk = 200;
    private const int StatusBadRequest = 400;
    private const int StatusForbidden = 403;
    private const int StatusNotFound = 404;
    private const int StatusConflict = 409;

    private readonly FiscalExceptionSemanticHashBackfillInternalApiOptions _options;
    private readonly IFiscalExceptionQueueService _queueService;
    private readonly IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
        _recalculationPreviewAuditRepository;
    private readonly IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository
        _mutationAuditRepository;
    private readonly IFiscalExceptionSemanticHashBackfillOperatorWorkflowService _workflowService;

    public FiscalExceptionSemanticHashBackfillInternalApiHandler(
        FiscalExceptionSemanticHashBackfillInternalApiOptions options,
        IFiscalExceptionQueueService queueService,
        IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository recalculationPreviewAuditRepository,
        IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository mutationAuditRepository,
        IFiscalExceptionSemanticHashBackfillOperatorWorkflowService workflowService)
    {
        _options = options;
        _queueService = queueService;
        _recalculationPreviewAuditRepository = recalculationPreviewAuditRepository;
        _mutationAuditRepository = mutationAuditRepository;
        _workflowService = workflowService;
    }

    public async Task<FiscalExceptionSemanticHashBackfillInternalApiResponse> RequestAsync(
        FiscalExceptionSemanticHashBackfillInternalApiRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enabled)
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_disabled",
                "semantic_hash_backfill_internal_api_disabled_fail_closed",
                StatusForbidden);
        }

        if (request.FiscalIssuanceReferenceIds is { Count: > 0 })
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_batch_not_allowed",
                "semantic_hash_backfill_internal_api_blocked_batch_not_allowed",
                StatusBadRequest);
        }

        var shapeBlock = ValidateRequestShape(request);
        if (shapeBlock is not null)
        {
            return shapeBlock;
        }

        if (request.ExecuteControlledMutation && !_options.AllowControlledMutationIntent)
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_execute_intent_disabled",
                "semantic_hash_backfill_internal_api_blocked_execute_intent_disabled",
                StatusForbidden);
        }

        var detail = await _queueService.GetAsync(request.FiscalIssuanceReferenceId, cancellationToken);
        if (detail is null)
        {
            return Blocked(
                "fiscal_exception_queue_case_not_found",
                "semantic_hash_backfill_internal_api_blocked_feq_case_not_found",
                StatusNotFound);
        }

        var previewSummary = await _recalculationPreviewAuditRepository.GetSummaryAsync(
            request.FiscalIssuanceReferenceId,
            cancellationToken);
        var mutationAudit = request.MutationPreparationAuditId is null ||
            request.MutationPreparationAuditId == Guid.Empty
                ? null
                : await _mutationAuditRepository.GetRecordAsync(
                    request.MutationPreparationAuditId.Value,
                    cancellationToken);

        var basisBlock = ValidateAuditBasis(request, previewSummary, mutationAudit);
        if (basisBlock is not null)
        {
            return basisBlock;
        }

        var mutationPreparation = ToMutationPreparationResult(
            mutationAudit!,
            request);

        var workflow = await _workflowService.RequestAsync(
            new FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest(
                Detail: detail,
                FiscalIssuanceReferenceId: request.FiscalIssuanceReferenceId,
                RecalculationPreviewAuditId: request.RecalculationPreviewAuditId,
                MutationPreparationAuditId: request.MutationPreparationAuditId,
                ActorServiceIdentityId: request.ActorServiceIdentityId,
                ApprovalReference: request.ApprovalReference,
                DualControlReference: request.DualControlReference,
                ReasonCode: request.ReasonCode,
                SafeJustification: request.SafeJustification,
                CorrelationId: request.CorrelationId ?? detail.CorrelationId,
                RequestMode: FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.SingleRecordOnly,
                ExecuteControlledMutation: request.ExecuteControlledMutation,
                DryRunOnly: request.DryRunOnly,
                RequestedAt: DateTimeOffset.UtcNow,
                LatestRecalculationPreviewAuditSummary: previewSummary,
                MutationPreparationBasis: mutationPreparation),
            cancellationToken);

        return FromWorkflow(workflow);
    }

    private static FiscalExceptionSemanticHashBackfillInternalApiResponse? ValidateRequestShape(
        FiscalExceptionSemanticHashBackfillInternalApiRequest request)
    {
        if (request.FiscalIssuanceReferenceId == Guid.Empty)
        {
            return Blocked(
                "fiscal_issuance_reference_id_required",
                "semantic_hash_backfill_internal_api_blocked_reference_required",
                StatusBadRequest);
        }

        if (request.RecalculationPreviewAuditId is null ||
            request.RecalculationPreviewAuditId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_recalculation_preview_audit_id_required",
                "semantic_hash_backfill_internal_api_blocked_preview_audit_id_required",
                StatusBadRequest);
        }

        if (request.MutationPreparationAuditId is null ||
            request.MutationPreparationAuditId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_audit_id_required",
                "semantic_hash_backfill_internal_api_blocked_mutation_preparation_audit_id_required",
                StatusBadRequest);
        }

        if (request.CorrelationId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_correlation_id_invalid",
                "semantic_hash_backfill_internal_api_blocked_correlation_id_invalid",
                StatusBadRequest);
        }

        if (!IsSafeReasonCode(request.ReasonCode))
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_reason_code_invalid",
                "semantic_hash_backfill_internal_api_blocked_reason_code_invalid",
                StatusBadRequest);
        }

        if (!IsSafeJustification(request.SafeJustification))
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_safe_justification_invalid",
                "semantic_hash_backfill_internal_api_blocked_safe_justification_invalid",
                StatusBadRequest);
        }

        return null;
    }

    private FiscalExceptionSemanticHashBackfillInternalApiResponse? ValidateAuditBasis(
        FiscalExceptionSemanticHashBackfillInternalApiRequest request,
        FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? previewSummary,
        FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord? mutationAudit)
    {
        if (request.RecalculationPreviewAuditId is null ||
            request.RecalculationPreviewAuditId == Guid.Empty ||
            previewSummary is null ||
            previewSummary.LastRecalculationPreviewAuditId != request.RecalculationPreviewAuditId)
        {
            return Blocked(
                "semantic_hash_recalculation_preview_audit_missing",
                "semantic_hash_backfill_internal_api_blocked_preview_audit_missing",
                StatusConflict);
        }

        if (request.MutationPreparationAuditId is null ||
            request.MutationPreparationAuditId == Guid.Empty ||
            mutationAudit is null ||
            mutationAudit.MutationAuditId != request.MutationPreparationAuditId)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_audit_missing",
                "semantic_hash_backfill_internal_api_blocked_mutation_preparation_audit_missing",
                StatusConflict);
        }

        if (mutationAudit.FiscalIssuanceReferenceId != request.FiscalIssuanceReferenceId ||
            mutationAudit.RecalculationPreviewAuditId != request.RecalculationPreviewAuditId)
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_mutation_prep_basis_mismatch",
                "semantic_hash_backfill_internal_api_blocked_mutation_prep_basis_mismatch",
                StatusConflict);
        }

        if (!MatchesIfPresent(mutationAudit.ActorServiceIdentityId, request.ActorServiceIdentityId) ||
            !MatchesIfPresent(mutationAudit.ApprovalReference, request.ApprovalReference) ||
            !MatchesIfPresent(mutationAudit.DualControlReference, request.DualControlReference))
        {
            return Blocked(
                "semantic_hash_backfill_internal_api_mutation_prep_request_mismatch",
                "semantic_hash_backfill_internal_api_blocked_mutation_prep_request_mismatch",
                StatusConflict);
        }

        return null;
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult
        ToMutationPreparationResult(
            FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord audit,
            FiscalExceptionSemanticHashBackfillInternalApiRequest request)
    {
        FiscalExceptionSemanticHashControlledBackfillMutationCommand? command = null;
        if (audit.MutationStatus ==
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation &&
            audit.RecalculationPreviewAuditId is not null &&
            !string.IsNullOrWhiteSpace(audit.NewHashValue) &&
            !string.IsNullOrWhiteSpace(audit.NewHashAlgorithm) &&
            !string.IsNullOrWhiteSpace(audit.NewHashSourceVersion) &&
            audit.NewHashSourceFactCount is > 0 &&
            !string.IsNullOrWhiteSpace(audit.SafeSourceSummary))
        {
            command = new FiscalExceptionSemanticHashControlledBackfillMutationCommand(
                FiscalIssuanceReferenceId: audit.FiscalIssuanceReferenceId,
                LatestRecalculationPreviewAuditId: audit.RecalculationPreviewAuditId.Value,
                ApprovalBasisStatus: audit.ApprovalBasisStatus,
                StoredSourceVersion: audit.OldSourceVersion,
                RequiredSourceVersion: audit.RequiredSourceVersion,
                RecalculatedHashValue: audit.NewHashValue,
                RecalculatedHashAlgorithm: audit.NewHashAlgorithm,
                RecalculatedHashSourceVersion: audit.NewHashSourceVersion,
                RecalculatedSourceFactCount: audit.NewHashSourceFactCount.Value,
                RecalculatedSafeSourceSummary: audit.SafeSourceSummary,
                ActorServiceIdentityId: request.ActorServiceIdentityId,
                ApprovalReference: request.ApprovalReference,
                DualControlReference: request.DualControlReference,
                CorrelationId: request.CorrelationId ?? audit.CorrelationId,
                MutationMode: audit.MutationMode,
                DryRunOnly: request.DryRunOnly,
                MutationStatus: audit.MutationStatus);
        }

        return new FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult(
            Status: audit.MutationStatus,
            BlockReasonCode: audit.BlockReasonCode,
            SafeSummary: audit.SafeSummary,
            Command: command,
            MutationMode: audit.MutationMode,
            MutationEnabled: audit.MutationEnabled,
            DryRunOnly: request.DryRunOnly,
            AuditPersisted: true,
            FiscalIssuanceReferenceMutated: audit.FiscalIssuanceReferenceMutated,
            RetryExecutionAvailable: false,
            PosServerPostCalled: false,
            RetryExecuted: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            MutationAuditId: audit.MutationAuditId,
            MutationAttemptedAt: audit.AttemptedAt);
    }

    private static FiscalExceptionSemanticHashBackfillInternalApiResponse FromWorkflow(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowResult result) =>
        new(
            WorkflowRequestId: result.WorkflowRequestId,
            WorkflowStatus: result.Status,
            BlockReasonCode: result.BlockReasonCode,
            MutationInvocationPosture: result.MutationInvocationPosture,
            GuardedMutationAuditId: result.GuardedMutationAuditId,
            GuardedMutationStatus: result.GuardedMutationStatus,
            RetryExecutionAvailable: false,
            SafeSummary: result.SafeSummary,
            HttpStatusCode: ToHttpStatusCode(result.Status));

    private static int ToHttpStatusCode(FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval => StatusOk,
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked => StatusOk,
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.PreparedButMutationInvocationDisabled =>
                StatusConflict,
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Unavailable => StatusConflict,
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked => StatusConflict,
            _ => StatusConflict
        };

    private static bool MatchesIfPresent(Guid? expected, Guid? actual) =>
        expected is null || actual is null || actual == Guid.Empty || expected == actual;

    private static bool MatchesIfPresent(string? expected, string? actual) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.IsNullOrWhiteSpace(actual) ||
        string.Equals(expected, actual, StringComparison.Ordinal);

    private static bool IsSafeReasonCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxReasonCodeLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '_' or '-' or ':');
    }

    private static bool IsSafeJustification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSafeJustificationLength)
        {
            return false;
        }

        if (value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("payload", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("canonical_source", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.All(character =>
            !char.IsControl(character) ||
            character is '\r' or '\n' or '\t');
    }

    private static FiscalExceptionSemanticHashBackfillInternalApiResponse Blocked(
        string blockReasonCode,
        string safeSummary,
        int httpStatusCode) =>
        new(
            WorkflowRequestId: null,
            WorkflowStatus: FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked,
            BlockReasonCode: blockReasonCode,
            MutationInvocationPosture:
                FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Blocked,
            GuardedMutationAuditId: null,
            GuardedMutationStatus: null,
            RetryExecutionAvailable: false,
            SafeSummary: safeSummary,
            HttpStatusCode: httpStatusCode);
}
