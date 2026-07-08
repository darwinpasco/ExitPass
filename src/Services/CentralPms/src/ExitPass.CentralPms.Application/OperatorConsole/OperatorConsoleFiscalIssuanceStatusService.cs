using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated read-only Operator Console fiscal issuance status service.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceStatusService : IOperatorConsoleFiscalIssuanceStatusService
{
    private const string FiscalIssuanceReferenceTargetEntityType = "FISCAL_ISSUANCE_REFERENCE";
    private const string SourceModule = "operator-console-fiscal-issuance-status";
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

        if (!evaluation.Allowed)
        {
            var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(
                WithViewAuditContext(
                    evaluation,
                    query.FiscalIssuanceReferenceId,
                    "DENIED",
                    SafeErrorCode: "OPERATOR_CONSOLE_FISCAL_STATUS_ACCESS_DENIED",
                    SafeErrorPosture: evaluation.Decision),
                cancellationToken);

            return ToResult(persistedEvaluation, status: null);
        }

        try
        {
            var status = await _statusReadService.GetByReferenceIdAsync(
                query.FiscalIssuanceReferenceId,
                cancellationToken);

            var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(
                WithViewAuditContext(
                    evaluation,
                    query.FiscalIssuanceReferenceId,
                    status is null ? "NOT_FOUND" : "SUCCEEDED",
                    SafeErrorCode: status is null ? "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND" : null,
                    SafeErrorPosture: status is null ? "Fiscal issuance reference was not found." : null),
                cancellationToken);

            return ToResult(persistedEvaluation, status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await _accessEvaluationWriter.PersistAsync(
                WithViewAuditContext(
                    evaluation,
                    query.FiscalIssuanceReferenceId,
                    "FAILED_SAFELY",
                    SafeErrorCode: "OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_FAILED",
                    SafeErrorPosture: "Fiscal status view failed safely."),
                cancellationToken);

            throw;
        }
    }

    private static OperatorConsoleAccessEvaluationResult WithViewAuditContext(
        OperatorConsoleAccessEvaluationResult evaluation,
        Guid fiscalIssuanceReferenceId,
        string resultClass,
        string? SafeErrorCode,
        string? SafeErrorPosture) =>
        evaluation with
        {
            PersistenceContext = evaluation.PersistenceContext with
            {
                TargetEntityType = FiscalIssuanceReferenceTargetEntityType,
                TargetEntityId = fiscalIssuanceReferenceId,
                ResultClass = resultClass,
                SafeErrorCode = SafeErrorCode,
                SafeErrorPosture = SafeErrorPosture,
                SourceModule = SourceModule
            }
        };

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
