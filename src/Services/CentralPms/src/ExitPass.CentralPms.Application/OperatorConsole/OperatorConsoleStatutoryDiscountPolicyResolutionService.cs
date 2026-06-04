using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated read-only Operator Console statutory discount policy resolution service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before any policy details are returned.
/// - Denied access does not resolve or return policy details.
/// - This service does not create statutory discount drafts, mutate payable basis, create payment attempts,
///   call providers, open gates, create coupons, or create reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountPolicyResolutionService
    : IOperatorConsoleStatutoryDiscountPolicyResolutionService
{
    private const string WorkflowCode = "STATUTORY_DISCOUNT_VALIDATION";
    private const string ControlledActionCode = OperatorConsoleActionCodes.ViewPolicyResolution;
    private const string SeniorCitizen = "SENIOR_CITIZEN";
    private const string Pwd = "PWD";

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository _repository;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates a statutory discount policy resolution service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountPolicyResolutionService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository repository,
        ISystemClock clock)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountPolicyResolutionResult> ResolveAsync(
        OperatorConsoleStatutoryDiscountPolicyResolutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entitlementType = Validate(command);

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId,
                command.SiteGroupId,
                command.OperatorShiftId,
                WorkflowCode,
                ControlledActionCode,
                command.ParkingSessionId,
                EvidenceAccessIntent: null,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
        if (!persistedEvaluation.Allowed)
        {
            return new OperatorConsoleStatutoryDiscountPolicyResolutionResult(
                persistedEvaluation.EvaluationId,
                AccessAllowed: false,
                persistedEvaluation.Decision,
                persistedEvaluation.DenialReasons,
                persistedEvaluation.Persisted,
                PolicyResolved: false,
                Policy: null,
                IneligibilityReason: "ACCESS_DENIED",
                ErrorCode: null,
                persistedEvaluation.CorrelationId);
        }

        var readResult = await _repository.ResolveAsync(
            new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                command.SiteId,
                command.SiteGroupId,
                entitlementType,
                DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime)),
            cancellationToken);

        return new OperatorConsoleStatutoryDiscountPolicyResolutionResult(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            readResult.Resolved,
            readResult.Policy,
            readResult.IneligibilityReason,
            readResult.ErrorCode,
            persistedEvaluation.CorrelationId);
    }

    private static string Validate(OperatorConsoleStatutoryDiscountPolicyResolutionCommand command)
    {
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.SiteId, nameof(command.SiteId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }

        var entitlementType = command.EntitlementType?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(entitlementType))
        {
            throw new ArgumentException("EntitlementType is required.", nameof(command.EntitlementType));
        }

        if (entitlementType is not SeniorCitizen and not Pwd)
        {
            throw new ArgumentException("EntitlementType must be SENIOR_CITIZEN or PWD.", nameof(command.EntitlementType));
        }

        return entitlementType;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
