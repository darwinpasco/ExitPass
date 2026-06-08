using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Foundational readiness evaluator for Operator Console controlled actions.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant: Operator Console controlled actions require operator, device, shift, site, workflow-state,
/// and audit readiness before production enforcement. This foundation does not mutate payment/provider/gate/coupon state.
/// </summary>
public sealed class OperatorConsoleAccessReadinessService
{
    private readonly OperatorConsoleActionCatalog _actionCatalog;
    private readonly OperatorConsoleDenialReasonCatalog _denialReasonCatalog;
    private readonly ISystemClock _clock;
    private readonly IOperatorConsoleAccessReadinessRepository? _repository;

    /// <summary>Creates the Operator Console access readiness service.</summary>
    public OperatorConsoleAccessReadinessService(
        OperatorConsoleActionCatalog actionCatalog,
        OperatorConsoleDenialReasonCatalog denialReasonCatalog,
        ISystemClock clock,
        IOperatorConsoleAccessReadinessRepository? repository = null)
    {
        _actionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
        _denialReasonCatalog = denialReasonCatalog ?? throw new ArgumentNullException(nameof(denialReasonCatalog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _repository = repository;
    }

    /// <summary>
    /// Evaluates foundation-level readiness from supplied context. Production table wiring is intentionally later scope.
    /// </summary>
    public OperatorConsoleAccessReadinessResult Evaluate(OperatorConsoleAccessReadinessCommand command) =>
        Evaluate(command, repositoryResult: null, _clock.UtcNow);

    /// <summary>
    /// Evaluates readiness with repository-backed operator/device/shift/site facts when configured.
    /// </summary>
    public async Task<OperatorConsoleAccessReadinessResult> EvaluateAsync(
        OperatorConsoleAccessReadinessCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var evaluatedAt = _clock.UtcNow;
        var repositoryResult = _repository is null
            ? null
            : await _repository.LoadAsync(command, evaluatedAt, cancellationToken);

        return Evaluate(command, repositoryResult, evaluatedAt);
    }

    private OperatorConsoleAccessReadinessResult Evaluate(
        OperatorConsoleAccessReadinessCommand command,
        OperatorConsoleAccessReadinessRepositoryResult? repositoryResult,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dimensionReasons = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["operator"] = [],
            ["roleAction"] = [],
            ["device"] = [],
            ["shift"] = [],
            ["site"] = [],
            ["workflow"] = [],
            ["audit"] = [],
            ["localDevBoundary"] = []
        };

        if (!command.OperatorUserId.HasValue || command.OperatorUserId.Value == Guid.Empty)
        {
            AddReason(dimensionReasons["operator"], OperatorConsoleDenialReasonCatalog.OperatorIdMissing);
        }

        if (!_actionCatalog.Contains(command.RequestedAction))
        {
            AddReason(dimensionReasons["roleAction"], OperatorConsoleDenialReasonCatalog.ActionNotAllowedForRole);
        }

        if (!command.OperatorDeviceBindingId.HasValue || command.OperatorDeviceBindingId.Value == Guid.Empty)
        {
            AddReason(dimensionReasons["device"], OperatorConsoleDenialReasonCatalog.DeviceIdMissing);
        }

        if (!command.OperatorShiftId.HasValue || command.OperatorShiftId.Value == Guid.Empty)
        {
            AddReason(dimensionReasons["shift"], OperatorConsoleDenialReasonCatalog.ShiftIdMissing);
        }

        if (!command.SiteId.HasValue || command.SiteId.Value == Guid.Empty)
        {
            AddReason(dimensionReasons["site"], OperatorConsoleDenialReasonCatalog.SiteIdMissing);
        }

        if (!command.SiteGroupId.HasValue || command.SiteGroupId.Value == Guid.Empty)
        {
            AddReason(dimensionReasons["site"], OperatorConsoleDenialReasonCatalog.SiteGroupIdMissing);
        }

        if (command.CorrelationId == Guid.Empty)
        {
            AddReason(dimensionReasons["audit"], OperatorConsoleDenialReasonCatalog.CorrelationIdMissing);
        }

        if (OperatorConsoleLocalDevFallbackPolicy.ShouldDenyFallback(command.EnvironmentName, command.UsesLocalDevFallbackContext))
        {
            AddReason(dimensionReasons["localDevBoundary"], OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
        }

        ApplyRepositoryReadiness(command, repositoryResult, dimensionReasons);

        var dimensions = dimensionReasons
            .Select(pair => new OperatorConsoleReadinessDimensionResult(
                pair.Key,
                pair.Value.Count == 0 ? "READY" : "BLOCKED",
                Required: true,
                DenialReasonCodes: pair.Value))
            .ToArray();

        var reasons = dimensions
            .SelectMany(dimension => dimension.DenialReasonCodes)
            .Distinct(StringComparer.Ordinal)
            .Select(ToDenialReason)
            .ToArray();

        var allowed = reasons.Length == 0;
        var nextOperatorAction = allowed
            ? null
            : BuildNextOperatorAction(reasons);

        return new OperatorConsoleAccessReadinessResult(
            AccessAllowed: allowed,
            AccessDecision: allowed ? "ALLOWED" : "DENIED",
            ReadinessStatus: allowed ? "READY" : "BLOCKED",
            ReadinessDimensions: dimensions,
            DenialReasons: reasons,
            OperatorReadiness: new OperatorConsoleOperatorReadiness(command.OperatorUserId, DimensionStatus(dimensionReasons["operator"]), dimensionReasons["operator"].Count == 0),
            DeviceReadiness: new OperatorConsoleDeviceReadiness(command.OperatorDeviceBindingId, DimensionStatus(dimensionReasons["device"]), dimensionReasons["device"].Count == 0),
            ShiftReadiness: new OperatorConsoleShiftReadiness(command.OperatorShiftId, DimensionStatus(dimensionReasons["shift"]), dimensionReasons["shift"].Count == 0),
            SiteReadiness: new OperatorConsoleSiteReadiness(command.SiteId, command.SiteGroupId, DimensionStatus(dimensionReasons["site"]), dimensionReasons["site"].Count == 0),
            WorkflowReadiness: new OperatorConsoleWorkflowReadiness(command.RequestedAction, command.WorkflowState, DimensionStatus(dimensionReasons["workflow"]), dimensionReasons["workflow"].Count == 0),
            AuditPersisted: false,
            EvaluatedAt: evaluatedAt,
            CorrelationId: command.CorrelationId,
            Retryable: reasons.Any(reason => reason.Retryable),
            NextOperatorAction: nextOperatorAction);
    }

    private OperatorConsoleAccessReadinessDenialReason ToDenialReason(string code)
    {
        var metadata = _denialReasonCatalog.Find(code) ??
            new OperatorConsoleDenialReasonMetadata(code, "BLOCKING", "ACCESS_READINESS", Retryable: false, UxMessageCategory: "SUPPORT_REQUIRED");

        return new OperatorConsoleAccessReadinessDenialReason(
            metadata.Code,
            metadata.Severity,
            metadata.Retryable,
            metadata.UxMessageCategory);
    }

    private static string DimensionStatus(IReadOnlyCollection<string> reasons) =>
        reasons.Count == 0 ? "READY" : "BLOCKED";

    private static void ApplyRepositoryReadiness(
        OperatorConsoleAccessReadinessCommand command,
        OperatorConsoleAccessReadinessRepositoryResult? repositoryResult,
        IReadOnlyDictionary<string, List<string>> dimensionReasons)
    {
        if (repositoryResult is null)
        {
            return;
        }

        if (!repositoryResult.Capabilities.HasReadinessTables)
        {
            if (!OperatorConsoleLocalDevFallbackPolicy.IsFallbackAllowed(command.EnvironmentName))
            {
                AddReason(
                    dimensionReasons["localDevBoundary"],
                    OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
            }

            return;
        }

        AddReasons(dimensionReasons["operator"], repositoryResult.OperatorDenialReasons);
        AddReasons(dimensionReasons["device"], repositoryResult.DeviceDenialReasons);
        AddReasons(dimensionReasons["shift"], repositoryResult.ShiftDenialReasons);
        AddReasons(dimensionReasons["site"], repositoryResult.SiteDenialReasons);
    }

    private static string BuildNextOperatorAction(IReadOnlyCollection<OperatorConsoleAccessReadinessDenialReason> reasons)
    {
        // Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
        // Invariant: Local/dev fallback context must never be accepted as production trust for controlled actions.
        return reasons.Any(reason => reason.Code == OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction)
            ? "Use production device enrollment, active shift, and site readiness records before continuing."
            : "Resolve the blocked Operator Console readiness checks before continuing.";
    }

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }

    private static void AddReasons(List<string> reasons, IEnumerable<string> additionalReasons)
    {
        foreach (var reason in additionalReasons)
        {
            AddReason(reasons, reason);
        }
    }
}
