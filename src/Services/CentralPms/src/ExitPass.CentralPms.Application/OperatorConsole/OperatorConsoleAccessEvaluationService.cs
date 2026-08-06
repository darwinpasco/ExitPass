using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Deterministic read-only Operator Console access evaluator.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This service never persists access evaluations or reasons.
/// - This service never creates or mutates payment, provider, gate, coupon, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationService : IOperatorConsoleAccessEvaluationService
{
    private static readonly HashSet<string> SupportedWorkflows = new(StringComparer.Ordinal)
    {
        OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
        OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow
    };

    private static readonly HashSet<string> SupportedActions = new(StringComparer.Ordinal)
    {
        OperatorConsoleActionCodes.SessionLookup,
        OperatorConsoleActionCodes.CreateStatutoryDiscountDraft,
        OperatorConsoleActionCodes.ViewStatutoryDiscountDraft,
        OperatorConsoleActionCodes.DecideStatutoryDiscount,
        OperatorConsoleActionCodes.CaptureEvidence,
        OperatorConsoleActionCodes.ViewEvidence,
        OperatorConsoleActionCodes.ReviewEvidence,
        OperatorConsoleActionCodes.ApplyStatutoryDiscountPayableBasis,
        OperatorConsoleActionCodes.ViewPolicyResolution,
        OperatorConsoleActionCodes.ViewAuditReport,
        OperatorConsoleActionCodes.ViewFiscalIssuanceStatus,
        OperatorConsoleActionCodes.VoidFiscalDocument,
        OperatorConsoleActionCodes.ViewFiscalStatusViewAuditReport,
        OperatorConsoleActionCodes.ViewFiscalVoidActionAuditReport
    };

    private static readonly HashSet<string> TrustedDeviceLevels = new(StringComparer.Ordinal)
    {
        "BROWSER_KEY_ONLY",
        "MTLS_ONLY",
        "BROWSER_KEY_AND_MTLS"
    };

    private readonly IOperatorConsoleAccessEvaluationReadRepository _repository;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates an Operator Console access evaluation service.
    /// </summary>
    public OperatorConsoleAccessEvaluationService(
        IOperatorConsoleAccessEvaluationReadRepository repository,
        ISystemClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
        OperatorConsoleAccessEvaluationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var evaluatedAt = _clock.UtcNow;
        var readRequest = new OperatorConsoleAccessEvaluationReadRequest(
            command.UserId,
            command.OperatorDeviceBindingId,
            command.SiteId,
            command.SiteGroupId,
            command.OperatorShiftId,
            command.ParkingSessionId,
            Normalize(command.WorkflowCode),
            Normalize(command.ControlledActionCode),
            command.EvidenceAccessIntent,
            evaluatedAt,
            command.CorrelationId);

        var context = await _repository.LoadAsync(readRequest, cancellationToken);
        return Evaluate(command, context, evaluatedAt);
    }

    private static OperatorConsoleAccessEvaluationResult Evaluate(
        OperatorConsoleAccessEvaluationCommand command,
        OperatorConsoleAccessEvaluationReadContext context,
        DateTimeOffset evaluatedAt)
    {
        var reasons = new List<string>();
        var workflowCode = Normalize(command.WorkflowCode);
        var actionCode = Normalize(command.ControlledActionCode);

        if (!SupportedWorkflows.Contains(workflowCode))
        {
            AddReason(reasons, "WORKFLOW_NOT_SUPPORTED");
        }

        if (!SupportedActions.Contains(actionCode))
        {
            AddReason(reasons, "ACTION_NOT_SUPPORTED");
        }

        EvaluateHrIdentityMapping(context, evaluatedAt, reasons);
        EvaluateDeviceBinding(context, evaluatedAt, reasons);
        EvaluateDeviceAssignment(context, evaluatedAt, reasons);
        EvaluateShift(context, command.UserId, evaluatedAt, reasons);
        EvaluateShiftTakeover(context, command.UserId, reasons);

        var allowed = reasons.Count == 0;
        return new OperatorConsoleAccessEvaluationResult(
            EvaluationId: Guid.Empty,
            Allowed: allowed,
            Decision: allowed ? "ALLOWED" : "DENIED",
            DenialReasons: reasons,
            EffectiveRole: allowed ? "OPERATOR" : null,
            DeviceTrust: ToDeviceTrust(context),
            ShiftContext: ToShiftContext(context, command.UserId, evaluatedAt),
            SiteContext: ToSiteContext(context),
            EvaluatedAt: evaluatedAt,
            Persisted: false,
            CorrelationId: command.CorrelationId,
            PersistenceContext: ToPersistenceContext(command, context, workflowCode, actionCode));
    }

    private static void EvaluateHrIdentityMapping(
        OperatorConsoleAccessEvaluationReadContext context,
        DateTimeOffset evaluatedAt,
        List<string> reasons)
    {
        var mapping = context.HrIdentityMapping;
        if (mapping is null)
        {
            AddReason(reasons, "HR_IDENTITY_MAPPING_NOT_FOUND");
            return;
        }

        if (!IsActive(mapping.MappingStatus) ||
            mapping.RevokedAt.HasValue ||
            mapping.EffectiveFrom > evaluatedAt ||
            (mapping.EffectiveTo.HasValue && mapping.EffectiveTo.Value <= evaluatedAt))
        {
            AddReason(reasons, "HR_IDENTITY_MAPPING_INACTIVE");
        }
    }

    private static void EvaluateDeviceBinding(
        OperatorConsoleAccessEvaluationReadContext context,
        DateTimeOffset evaluatedAt,
        List<string> reasons)
    {
        var binding = context.DeviceBinding;
        if (binding is null)
        {
            AddReason(reasons, "DEVICE_BINDING_NOT_FOUND");
            return;
        }

        if (!IsActive(binding.DeviceStatus) || binding.RevokedAt.HasValue)
        {
            AddReason(reasons, "DEVICE_BINDING_INACTIVE");
        }

        if (!TrustedDeviceLevels.Contains(binding.TrustLevel))
        {
            AddReason(reasons, "DEVICE_NOT_TRUSTED");
        }

        if (context.Request.SiteId.HasValue && binding.SiteId != context.Request.SiteId.Value)
        {
            AddReason(reasons, "DEVICE_SITE_ASSIGNMENT_INVALID");
        }

        if (context.Request.SiteGroupId.HasValue && binding.SiteGroupId != context.Request.SiteGroupId.Value)
        {
            AddReason(reasons, "DEVICE_SITE_ASSIGNMENT_INVALID");
        }

        _ = evaluatedAt;
    }

    private static void EvaluateDeviceAssignment(
        OperatorConsoleAccessEvaluationReadContext context,
        DateTimeOffset evaluatedAt,
        List<string> reasons)
    {
        var assignment = context.DeviceAssignment;
        if (assignment is null)
        {
            AddReason(reasons, "DEVICE_SITE_ASSIGNMENT_NOT_FOUND");
            return;
        }

        var invalid =
            !IsActive(assignment.AssignmentStatusCode) ||
            assignment.EndedAt.HasValue ||
            assignment.EffectiveFrom > evaluatedAt ||
            (assignment.EffectiveTo.HasValue && assignment.EffectiveTo.Value <= evaluatedAt) ||
            (context.Request.OperatorDeviceBindingId.HasValue &&
                assignment.OperatorDeviceBindingId != context.Request.OperatorDeviceBindingId.Value) ||
            (context.Request.SiteId.HasValue && assignment.SiteId != context.Request.SiteId.Value) ||
            (context.Request.SiteGroupId.HasValue && assignment.SiteGroupId != context.Request.SiteGroupId.Value);

        if (invalid)
        {
            AddReason(reasons, "DEVICE_SITE_ASSIGNMENT_INVALID");
        }
    }

    private static void EvaluateShift(
        OperatorConsoleAccessEvaluationReadContext context,
        Guid userId,
        DateTimeOffset evaluatedAt,
        List<string> reasons)
    {
        var shift = context.ActiveShift;
        if (shift is null)
        {
            AddReason(reasons, "NO_ACTIVE_SHIFT");
            return;
        }

        if (shift.RevokedAt.HasValue ||
            string.Equals(shift.OperationalStatus, "REVOKED", StringComparison.Ordinal) ||
            context.LatestShiftRevocation is { RevocationStatus: "APPROVED" or "EFFECTIVE" })
        {
            AddReason(reasons, "SHIFT_REVOKED");
            return;
        }

        var invalid =
            !IsActive(shift.OperationalStatus) ||
            shift.OperatorUserId != userId ||
            !shift.ActiveFrom.HasValue ||
            shift.ActiveFrom.Value > evaluatedAt ||
            (shift.ActiveTo.HasValue && shift.ActiveTo.Value <= evaluatedAt) ||
            (context.Request.SiteId.HasValue && shift.SiteId != context.Request.SiteId.Value) ||
            (context.Request.SiteGroupId.HasValue && shift.SiteGroupId != context.Request.SiteGroupId.Value);

        if (invalid)
        {
            AddReason(reasons, "NO_ACTIVE_SHIFT");
        }
    }

    private static void EvaluateShiftTakeover(
        OperatorConsoleAccessEvaluationReadContext context,
        Guid userId,
        List<string> reasons)
    {
        var takeover = context.ActiveShiftTakeover;
        if (takeover is null)
        {
            return;
        }

        if (!string.Equals(takeover.TakeoverStatus, "ACTIVE", StringComparison.Ordinal))
        {
            return;
        }

        if (takeover.TakeoverOperatorUserId != userId)
        {
            AddReason(reasons, "SHIFT_TAKEOVER_ACTIVE");
        }
    }

    private static OperatorConsoleDeviceTrustResult ToDeviceTrust(OperatorConsoleAccessEvaluationReadContext context)
    {
        var binding = context.DeviceBinding;
        return new OperatorConsoleDeviceTrustResult(
            binding?.OperatorDeviceBindingId ?? context.Request.OperatorDeviceBindingId,
            binding?.DeviceStatus ?? "NOT_FOUND",
            binding?.TrustLevel ?? "UNKNOWN",
            binding is not null &&
                IsActive(binding.DeviceStatus) &&
                TrustedDeviceLevels.Contains(binding.TrustLevel) &&
                !binding.RevokedAt.HasValue);
    }

    private static OperatorConsoleShiftContextResult ToShiftContext(
        OperatorConsoleAccessEvaluationReadContext context,
        Guid userId,
        DateTimeOffset evaluatedAt)
    {
        var shift = context.ActiveShift;
        return new OperatorConsoleShiftContextResult(
            shift?.OperatorShiftId ?? context.Request.OperatorShiftId,
            shift?.OperationalStatus ?? "NOT_FOUND",
            shift is not null &&
                shift.OperatorUserId == userId &&
                IsActive(shift.OperationalStatus) &&
                !shift.RevokedAt.HasValue &&
                shift.ActiveFrom.HasValue &&
                shift.ActiveFrom.Value <= evaluatedAt &&
                (!shift.ActiveTo.HasValue || shift.ActiveTo.Value > evaluatedAt));
    }

    private static OperatorConsoleSiteContextResult ToSiteContext(OperatorConsoleAccessEvaluationReadContext context)
    {
        var assignment = context.DeviceAssignment;
        return new OperatorConsoleSiteContextResult(
            assignment?.SiteId ?? context.Request.SiteId,
            assignment?.SiteGroupId ?? context.Request.SiteGroupId,
            assignment is not null &&
                IsActive(assignment.AssignmentStatusCode) &&
                !assignment.EndedAt.HasValue);
    }

    private static OperatorConsoleAccessEvaluationPersistenceContext ToPersistenceContext(
        OperatorConsoleAccessEvaluationCommand command,
        OperatorConsoleAccessEvaluationReadContext context,
        string workflowCode,
        string actionCode) =>
        new(
            command.UserId,
            context.HrIdentityMapping?.HrIdentityMappingId,
            context.DeviceBinding?.OperatorDeviceBindingId,
            context.ActiveShift?.OperatorShiftId,
            context.ActiveShiftTakeover?.ShiftTakeoverId,
            context.DeviceAssignment?.SiteGroupId ?? context.DeviceBinding?.SiteGroupId ?? context.ActiveShift?.SiteGroupId ?? command.SiteGroupId,
            context.DeviceAssignment?.SiteId ?? context.DeviceBinding?.SiteId ?? context.ActiveShift?.SiteId ?? command.SiteId,
            actionCode,
            workflowCode,
            command.ParkingSessionId.HasValue ? "PARKING_SESSION" : null,
            command.ParkingSessionId);

    private static void Validate(OperatorConsoleAccessEvaluationCommand command)
    {
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (string.IsNullOrWhiteSpace(command.WorkflowCode))
        {
            throw new ArgumentException("WorkflowCode is required.", nameof(command.WorkflowCode));
        }

        if (string.IsNullOrWhiteSpace(command.ControlledActionCode))
        {
            throw new ArgumentException("ControlledActionCode is required.", nameof(command.ControlledActionCode));
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static bool IsActive(string value) =>
        string.Equals(value, "ACTIVE", StringComparison.Ordinal);

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }
}
