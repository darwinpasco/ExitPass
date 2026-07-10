using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console fiscal void facade.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceVoidService : IOperatorConsoleFiscalIssuanceVoidService
{
    public const string ConfirmationPhrase = "VOID FISCAL DOCUMENT";
    public const int MaxReasonTextLength = FiscalIssuanceVoidCommandService.MaxReasonTextLength;

    private const string FiscalIssuanceReferenceTargetEntityType = "FISCAL_ISSUANCE_REFERENCE";
    private const string SourceModule = "operator-console-fiscal-issuance-status";
    private const string WorkflowCode = OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.VoidFiscalDocument;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IFiscalIssuanceStatusReadService _statusReadService;
    private readonly IFiscalIssuanceVoidCommandService _voidCommandService;

    /// <summary>
    /// Creates the Operator Console fiscal void facade.
    /// </summary>
    public OperatorConsoleFiscalIssuanceVoidService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IFiscalIssuanceStatusReadService statusReadService,
        IFiscalIssuanceVoidCommandService voidCommandService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _statusReadService = statusReadService ?? throw new ArgumentNullException(nameof(statusReadService));
        _voidCommandService = voidCommandService ?? throw new ArgumentNullException(nameof(voidCommandService));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleFiscalIssuanceVoidResult> VoidAsync(
        OperatorConsoleFiscalIssuanceVoidCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestErrors = Validate(command);
        if (requestErrors.Count > 0)
        {
            return new OperatorConsoleFiscalIssuanceVoidResult(
                AccessEvaluationId: Guid.Empty,
                AccessAllowed: false,
                AccessDecision: "REQUEST_REJECTED",
                AccessDenialReasons: requestErrors,
                AccessPersisted: false,
                VoidResult: Rejected(command, "operator_console_fiscal_void_request_rejected", 400, requestErrors),
                CorrelationId: command.CorrelationId);
        }

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId,
                command.SiteGroupId,
                command.OperatorShiftId,
                WorkflowCode,
                ControlledActionCode,
                ParkingSessionId: null,
                EvidenceAccessIntent: null,
                BuildAccessIdempotencyKey(command),
                command.CorrelationId),
            cancellationToken);

        if (!evaluation.Allowed)
        {
            var persistedEvaluation = await PersistAsync(
                evaluation,
                command.FiscalIssuanceReferenceId,
                "DENIED",
                "OPERATOR_CONSOLE_FISCAL_VOID_ACCESS_DENIED",
                evaluation.Decision,
                cancellationToken);

            return ToResult(persistedEvaluation, null);
        }

        var status = await _statusReadService.GetByReferenceIdAsync(command.FiscalIssuanceReferenceId, cancellationToken)
            .ConfigureAwait(false);
        if (status is null)
        {
            var notFound = Rejected(command, "fiscal_issuance_reference_not_found", 404, ["fiscal_issuance_reference_not_found"]);
            var persistedEvaluation = await PersistAsync(
                evaluation,
                command.FiscalIssuanceReferenceId,
                "NOT_FOUND",
                "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND",
                "Fiscal issuance reference was not found.",
                cancellationToken);

            return ToResult(persistedEvaluation, notFound);
        }

        if (IsAlreadyVoided(status))
        {
            var alreadyVoided = AlreadyVoided(command, status);
            var persistedEvaluation = await PersistAsync(
                evaluation,
                command.FiscalIssuanceReferenceId,
                "ALREADY_VOIDED",
                null,
                null,
                cancellationToken);

            return ToResult(persistedEvaluation, alreadyVoided);
        }

        var voidResult = await _voidCommandService.VoidAsync(
                command.FiscalIssuanceReferenceId,
                new FiscalIssuanceVoidCommandRequest(
                    BuildPosServerIdempotencyKey(command),
                    command.ReasonCode!.Trim(),
                    NormalizeOptional(command.ReasonText),
                    $"operator-console:{command.UserId:D}",
                    RequestedAt: null,
                    CorrelationId: command.CorrelationId.ToString("D")),
                cancellationToken)
            .ConfigureAwait(false);

        var persisted = await PersistAsync(
            evaluation,
            command.FiscalIssuanceReferenceId,
            ResultClass(voidResult),
            SafeErrorCode(voidResult),
            SafeErrorPosture(voidResult),
            cancellationToken);

        return ToResult(persisted, voidResult);
    }

    private static IReadOnlyList<string> Validate(OperatorConsoleFiscalIssuanceVoidCommand command)
    {
        var errors = new List<string>();
        if (command.UserId == Guid.Empty)
        {
            errors.Add("operator_user_id_required");
        }

        if (command.FiscalIssuanceReferenceId == Guid.Empty)
        {
            errors.Add("fiscal_issuance_reference_id_required");
        }

        if (command.OperatorActionRequestId == Guid.Empty)
        {
            errors.Add("operator_action_request_id_required");
        }

        if (command.CorrelationId == Guid.Empty)
        {
            errors.Add("correlation_id_required");
        }

        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            errors.Add("reason_code_required");
        }

        if (string.IsNullOrWhiteSpace(command.ReasonText))
        {
            errors.Add("reason_text_required");
        }
        else if (command.ReasonText.Length > MaxReasonTextLength)
        {
            errors.Add("reason_text_too_long");
        }

        if (!string.Equals(command.ConfirmationText?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
        {
            errors.Add("confirmation_text_invalid");
        }

        return errors;
    }

    private async Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
        OperatorConsoleAccessEvaluationResult evaluation,
        Guid fiscalIssuanceReferenceId,
        string resultClass,
        string? safeErrorCode,
        string? safeErrorPosture,
        CancellationToken cancellationToken) =>
        await _accessEvaluationWriter.PersistAsync(
                evaluation with
                {
                    PersistenceContext = evaluation.PersistenceContext with
                    {
                        TargetEntityType = FiscalIssuanceReferenceTargetEntityType,
                        TargetEntityId = fiscalIssuanceReferenceId,
                        ResultClass = resultClass,
                        SafeErrorCode = safeErrorCode,
                        SafeErrorPosture = safeErrorPosture,
                        SourceModule = SourceModule
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

    private static OperatorConsoleFiscalIssuanceVoidResult ToResult(
        OperatorConsoleAccessEvaluationResult access,
        FiscalIssuanceVoidCommandResponse? voidResult) =>
        new(
            access.EvaluationId,
            access.Allowed,
            access.Decision,
            access.DenialReasons,
            access.Persisted,
            voidResult,
            access.CorrelationId);

    private static FiscalIssuanceVoidCommandResponse Rejected(
        OperatorConsoleFiscalIssuanceVoidCommand command,
        string status,
        int httpStatusCode,
        IReadOnlyList<string> errors) =>
        new(
            Accepted: false,
            Status: status,
            HttpStatusCode: httpStatusCode,
            Errors: errors,
            FiscalIssuanceReferenceId: command.FiscalIssuanceReferenceId,
            PosServerFiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalSequenceValue: null,
            FiscalDocumentStatusPosture: null,
            VoidStatus: null,
            VoidReasonCode: null,
            VoidedAt: null,
            PosServerResultClassification: null,
            IdempotencyKey: BuildPosServerIdempotencyKey(command),
            CorrelationId: command.CorrelationId.ToString("D"),
            ErrorPosture: null,
            NewFiscalNumberAllocated: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            RefundOrReversalCreated: false,
            HikCentralCalled: false,
            PaymentProviderCalled: false,
            RenderingGenerated: false,
            ReplacementFiscalDocumentCreated: false,
            FiscalSequenceChangedByCentralPms: false,
            IdempotentReplay: false);

    private static FiscalIssuanceVoidCommandResponse AlreadyVoided(
        OperatorConsoleFiscalIssuanceVoidCommand command,
        FiscalIssuanceStatusReadModel status) =>
        new(
            Accepted: true,
            Status: "pos_server_already_voided",
            HttpStatusCode: 200,
            Errors: Array.Empty<string>(),
            FiscalIssuanceReferenceId: status.FiscalIssuanceReferenceId,
            PosServerFiscalDocumentId: status.PosServerFiscalDocumentId,
            FiscalDocumentNumber: status.FiscalDocumentNumber,
            FiscalSequenceValue: status.FiscalSequenceValue,
            FiscalDocumentStatusPosture: status.PosServerFiscalDocumentStatusCodeKey,
            VoidStatus: status.PosServerVoidStatus,
            VoidReasonCode: status.PosServerVoidReasonCode,
            VoidedAt: status.PosServerVoidedAt,
            PosServerResultClassification: "already_voided",
            IdempotencyKey: BuildPosServerIdempotencyKey(command),
            CorrelationId: command.CorrelationId.ToString("D"),
            ErrorPosture: null,
            NewFiscalNumberAllocated: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            RefundOrReversalCreated: false,
            HikCentralCalled: false,
            PaymentProviderCalled: false,
            RenderingGenerated: false,
            ReplacementFiscalDocumentCreated: false,
            FiscalSequenceChangedByCentralPms: false,
            IdempotentReplay: false);

    private static bool IsAlreadyVoided(FiscalIssuanceStatusReadModel status) =>
        string.Equals(status.PosServerFiscalDocumentStatusCodeKey, "voided", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status.PosServerVoidStatus, "recorded", StringComparison.OrdinalIgnoreCase);

    private static string BuildAccessIdempotencyKey(OperatorConsoleFiscalIssuanceVoidCommand command) =>
        $"operator-console-fiscal-void-access-{command.FiscalIssuanceReferenceId:N}-{command.OperatorActionRequestId:N}";

    private static string BuildPosServerIdempotencyKey(OperatorConsoleFiscalIssuanceVoidCommand command) =>
        $"operator-console-fiscal-void:{command.FiscalIssuanceReferenceId:D}:{command.OperatorActionRequestId:D}";

    private static string ResultClass(FiscalIssuanceVoidCommandResponse result)
    {
        if (result.Accepted)
        {
            return result.Status == "pos_server_already_voided" ? "ALREADY_VOIDED" : "SUCCEEDED";
        }

        return result.Status switch
        {
            "fiscal_issuance_reference_not_found" => "NOT_FOUND",
            "pos_server_void_conflict" => "CONFLICT",
            "pos_server_void_rejected" => "REJECTED",
            _ => "FAILED_SAFELY"
        };
    }

    private static string? SafeErrorCode(FiscalIssuanceVoidCommandResponse result) =>
        result.Accepted ? null : result.Errors.FirstOrDefault() ?? result.Status;

    private static string? SafeErrorPosture(FiscalIssuanceVoidCommandResponse result) =>
        result.Accepted ? null : result.ErrorPosture ?? result.Status;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
