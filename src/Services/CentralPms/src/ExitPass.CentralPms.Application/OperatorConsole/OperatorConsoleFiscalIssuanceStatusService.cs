using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated read-only Operator Console fiscal issuance status service.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceStatusService : IOperatorConsoleFiscalIssuanceStatusService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.ViewFiscalIssuanceStatus;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IFiscalIssuanceStatusReadService _statusReadService;

    /// <summary>
    /// Creates an Operator Console fiscal issuance status service.
    /// </summary>
    public OperatorConsoleFiscalIssuanceStatusService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IFiscalIssuanceStatusReadService statusReadService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _statusReadService = statusReadService ?? throw new ArgumentNullException(nameof(statusReadService));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleFiscalIssuanceStatusResult> GetAsync(
        OperatorConsoleFiscalIssuanceStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                query.UserId,
                query.OperatorDeviceBindingId,
                query.SiteId,
                query.SiteGroupId,
                query.OperatorShiftId,
                WorkflowCode,
                ControlledActionCode,
                ParkingSessionId: null,
                EvidenceAccessIntent: null,
                BuildIdempotencyKey(query),
                query.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);

        if (!persistedEvaluation.Allowed)
        {
            return ToResult(persistedEvaluation, status: null);
        }

        var status = await _statusReadService.GetByReferenceIdAsync(
            query.FiscalIssuanceReferenceId,
            cancellationToken);

        return ToResult(persistedEvaluation, status);
    }

    private static OperatorConsoleFiscalIssuanceStatusResult ToResult(
        OperatorConsoleAccessEvaluationResult access,
        FiscalIssuanceStatusReadModel? status) =>
        new(
            access.EvaluationId,
            access.Allowed,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            status,
            access.CorrelationId);

    private static string BuildIdempotencyKey(OperatorConsoleFiscalIssuanceStatusQuery query) =>
        $"operator-console-fiscal-status-view-{query.FiscalIssuanceReferenceId:N}-{query.CorrelationId:N}";

    private static void Validate(OperatorConsoleFiscalIssuanceStatusQuery query)
    {
        ValidateGuid(query.UserId, nameof(query.UserId));
        ValidateGuid(query.FiscalIssuanceReferenceId, nameof(query.FiscalIssuanceReferenceId));
        ValidateGuid(query.CorrelationId, nameof(query.CorrelationId));
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
