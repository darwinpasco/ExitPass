namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashBackfillOperatorWorkflowService
    : IFiscalExceptionSemanticHashBackfillOperatorWorkflowService
{
    private readonly FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions _options;
    private readonly IFiscalExceptionSemanticHashControlledBackfillApprovalService _approvalService;
    private readonly IFiscalExceptionSemanticHashGuardedBackfillMutationService _guardedMutationService;
    private readonly IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository _auditRepository;

    public FiscalExceptionSemanticHashBackfillOperatorWorkflowService(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions options,
        IFiscalExceptionSemanticHashControlledBackfillApprovalService approvalService,
        IFiscalExceptionSemanticHashGuardedBackfillMutationService guardedMutationService,
        IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository auditRepository)
    {
        _options = options;
        _approvalService = approvalService;
        _guardedMutationService = guardedMutationService;
        _auditRepository = auditRepository;
    }

    public async Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowResult> RequestAsync(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);

        var requestedAt = request.RequestedAt ?? DateTimeOffset.UtcNow;
        var blocked = ValidateRequestShape(request, requestedAt);
        if (blocked is not null)
        {
            return await PersistAsync(request, blocked, cancellationToken);
        }

        var approval = _approvalService.Evaluate(
            new FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
                request.Detail,
                request.LatestRecalculationPreviewAuditSummary));

        blocked = ValidateWorkflowBasis(request, approval, requestedAt);
        if (blocked is not null)
        {
            return await PersistAsync(request, blocked, cancellationToken);
        }

        if (!request.ExecuteControlledMutation || request.DryRunOnly)
        {
            return await PersistAsync(
                request,
                Result(
                    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval,
                    blockReasonCode: null,
                    "semantic_hash_backfill_operator_workflow_ready_single_record_dry_run_not_mutated",
                    request,
                    requestedAt,
                    request.DryRunOnly
                        ? FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.DryRunOnly
                        : FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.NotRequested,
                    guardedMutation: null,
                    mutated: false),
                cancellationToken);
        }

        if (!_options.EnableControlledMutationInvocation)
        {
            return await PersistAsync(
                request,
                Result(
                    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus
                        .PreparedButMutationInvocationDisabled,
                    "semantic_hash_backfill_operator_workflow_mutation_invocation_disabled",
                    "semantic_hash_backfill_operator_workflow_prepared_but_invocation_disabled_not_mutated",
                    request,
                    requestedAt,
                    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Disabled,
                    guardedMutation: null,
                    mutated: false),
                cancellationToken);
        }

        await PersistAsync(
            request,
            Result(
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.ReadyForOperatorApproval,
                blockReasonCode: null,
                "semantic_hash_backfill_operator_workflow_mutation_invocation_authorized",
                request,
                requestedAt,
                FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Invoked,
                guardedMutation: null,
                mutated: false),
            cancellationToken);

        var guardedMutation = await _guardedMutationService.MutateAsync(
            new FiscalExceptionSemanticHashGuardedBackfillMutationRequest(
                request.Detail,
                approval,
                request.LatestRecalculationPreviewAuditSummary!,
                request.MutationPreparationBasis!,
                request.ActorServiceIdentityId!.Value,
                request.ApprovalReference!,
                request.DualControlReference,
                requestedAt),
            cancellationToken);

        return await PersistAsync(
            request,
            Result(
                ToWorkflowStatus(guardedMutation.Status),
                guardedMutation.BlockReasonCode,
                guardedMutation.SafeSummary,
                request,
                requestedAt,
                ToInvocationPosture(guardedMutation.Status),
                guardedMutation,
                guardedMutation.FiscalIssuanceReferenceMutated),
            cancellationToken);
    }

    private FiscalExceptionSemanticHashBackfillOperatorWorkflowResult? ValidateRequestShape(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        DateTimeOffset requestedAt)
    {
        var summary = request.Detail.Summary;
        if (request.FiscalIssuanceReferenceId == Guid.Empty ||
            summary.FiscalIssuanceReferenceId == Guid.Empty ||
            request.FiscalIssuanceReferenceId != summary.FiscalIssuanceReferenceId)
        {
            return Blocked(
                "semantic_hash_backfill_operator_workflow_reference_mismatch",
                "semantic_hash_backfill_operator_workflow_blocked_reference_mismatch",
                request,
                requestedAt);
        }

        if (request.RequestMode !=
            FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.SingleRecordOnly)
        {
            return Blocked(
                "semantic_hash_backfill_operator_workflow_batch_not_allowed",
                "semantic_hash_backfill_operator_workflow_blocked_batch_not_allowed",
                request,
                requestedAt);
        }

        if (request.ActorServiceIdentityId is null || request.ActorServiceIdentityId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_backfill_actor_authorization_missing",
                "semantic_hash_backfill_operator_workflow_blocked_actor_authorization_missing",
                request,
                requestedAt);
        }

        if (string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            return Blocked(
                "semantic_hash_backfill_explicit_approval_missing",
                "semantic_hash_backfill_operator_workflow_blocked_explicit_approval_missing",
                request,
                requestedAt);
        }

        return null;
    }

    private FiscalExceptionSemanticHashBackfillOperatorWorkflowResult? ValidateWorkflowBasis(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        FiscalExceptionSemanticHashControlledBackfillApprovalResult approval,
        DateTimeOffset requestedAt)
    {
        if (approval.DualControlRequired && string.IsNullOrWhiteSpace(request.DualControlReference))
        {
            return Blocked(
                "semantic_hash_backfill_dual_control_required",
                "semantic_hash_backfill_operator_workflow_blocked_dual_control_required",
                request,
                requestedAt);
        }

        var preview = request.LatestRecalculationPreviewAuditSummary;
        if (request.RecalculationPreviewAuditId is null ||
            request.RecalculationPreviewAuditId == Guid.Empty ||
            preview is null ||
            preview.LastRecalculationPreviewAuditId != request.RecalculationPreviewAuditId)
        {
            return Blocked(
                "semantic_hash_recalculation_preview_audit_missing",
                "semantic_hash_backfill_operator_workflow_blocked_preview_audit_missing",
                request,
                requestedAt);
        }

        var mutationPreparation = request.MutationPreparationBasis;
        if (request.MutationPreparationAuditId is null ||
            request.MutationPreparationAuditId == Guid.Empty ||
            mutationPreparation is null ||
            mutationPreparation.MutationAuditId != request.MutationPreparationAuditId)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_audit_missing",
                "semantic_hash_backfill_operator_workflow_blocked_mutation_preparation_audit_missing",
                request,
                requestedAt);
        }

        if (approval.Status != FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill)
        {
            return Blocked(
                approval.BlockReasonCode ?? "semantic_hash_controlled_backfill_approval_not_ready",
                "semantic_hash_backfill_operator_workflow_blocked_approval_not_ready",
                request,
                requestedAt);
        }

        if (!mutationPreparation.AuditPersisted ||
            mutationPreparation.Command is null ||
            mutationPreparation.Status !=
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_not_ready",
                "semantic_hash_backfill_operator_workflow_blocked_mutation_preparation_not_ready",
                request,
                requestedAt);
        }

        return null;
    }

    private async Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowResult> PersistAsync(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await _auditRepository.RecordAsync(
                ToAuditWrite(request, result),
                cancellationToken);

            return result with
            {
                WorkflowAuditPersisted = true,
                WorkflowRequestId = record.WorkflowRequestId,
                CreatedAt = record.CreatedAt
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "semantic_hash_backfill_operator_workflow_audit_persistence_failed",
                ex);
        }
    }

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite ToAuditWrite(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowResult result) =>
        new(
            FiscalIssuanceReferenceId: result.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: result.RecalculationPreviewAuditId,
            MutationPreparationAuditId: result.MutationPreparationAuditId,
            ApprovalReference: result.ApprovalReference,
            DualControlReference: result.DualControlReference,
            ActorServiceIdentityId: result.ActorServiceIdentityId,
            ReasonCode: result.ReasonCode,
            SafeJustification: request.SafeJustification,
            RequestMode: result.RequestMode,
            WorkflowStatus: result.Status,
            BlockReasonCode: result.BlockReasonCode,
            MutationInvocationPosture: result.MutationInvocationPosture,
            GuardedMutationAuditId: result.GuardedMutationAuditId,
            GuardedMutationStatus: result.GuardedMutationStatus,
            ExecuteControlledMutationRequested: result.ExecuteControlledMutationRequested,
            MutationInvocationEnabled: result.MutationInvocationEnabled,
            DryRunOnly: result.DryRunOnly,
            RequestedAt: result.RequestedAt,
            CorrelationId: request.CorrelationId ?? request.Detail.CorrelationId,
            SafeSummary: result.SafeSummary);

    private FiscalExceptionSemanticHashBackfillOperatorWorkflowResult Blocked(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        DateTimeOffset requestedAt) =>
        Result(
            FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked,
            blockReasonCode,
            safeSummary,
            request,
            requestedAt,
            FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Blocked,
            guardedMutation: null,
            mutated: false);

    private FiscalExceptionSemanticHashBackfillOperatorWorkflowResult Result(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        DateTimeOffset requestedAt,
        FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture invocationPosture,
        FiscalExceptionSemanticHashGuardedBackfillMutationResult? guardedMutation,
        bool mutated) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            WorkflowRequestId: null,
            FiscalIssuanceReferenceId: request.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: request.RecalculationPreviewAuditId,
            MutationPreparationAuditId: request.MutationPreparationAuditId,
            ApprovalReference: request.ApprovalReference,
            DualControlReference: request.DualControlReference,
            ActorServiceIdentityId: request.ActorServiceIdentityId,
            ReasonCode: request.ReasonCode,
            RequestMode: request.RequestMode,
            MutationInvocationPosture: invocationPosture,
            ExecuteControlledMutationRequested: request.ExecuteControlledMutation,
            MutationInvocationEnabled: _options.EnableControlledMutationInvocation,
            DryRunOnly: request.DryRunOnly,
            GuardedMutationAuditId: guardedMutation?.MutationAuditId,
            GuardedMutationStatus: guardedMutation?.Status,
            WorkflowAuditPersisted: false,
            RequestedAt: requestedAt,
            CreatedAt: null,
            FiscalIssuanceReferenceMutated: mutated,
            RetryExecutionAvailable: false,
            PosServerPostCalled: guardedMutation?.PosServerPostCalled ?? false,
            RetryExecuted: guardedMutation?.RetryExecuted ?? false,
            RetryScheduled: guardedMutation?.RetryScheduled ?? false,
            PaymentFinalityChanged: guardedMutation?.PaymentFinalityChanged ?? false,
            ExitAuthorizationIssued: guardedMutation?.ExitAuthorizationIssued ?? false,
            GateBehaviorTriggered: guardedMutation?.GateBehaviorTriggered ?? false,
            FiscalNumberEdited: guardedMutation?.FiscalNumberEdited ?? false,
            ManualFiscalDocumentCreated: guardedMutation?.ManualFiscalDocumentCreated ?? false);

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus ToWorkflowStatus(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus guardedStatus) =>
        guardedStatus switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.MutationInvoked,
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus
                    .PreparedButMutationInvocationDisabled,
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Unavailable,
            _ => FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus.Blocked
        };

    private static FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture ToInvocationPosture(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus guardedStatus) =>
        guardedStatus switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Mutated =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Invoked,
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled =>
                FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Disabled,
            _ => FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture.Blocked
        };
}
