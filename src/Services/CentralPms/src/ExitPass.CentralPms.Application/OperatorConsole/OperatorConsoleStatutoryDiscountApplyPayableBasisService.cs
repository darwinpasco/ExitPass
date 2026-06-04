namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console statutory discount payable-basis application service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before payable-basis application is attempted.
/// - Denied access does not read or mutate statutory discount or tariff state.
/// - This service does not create payment attempts, provider outcomes, exit authorizations, gate records, coupon applications, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisService
    : IOperatorConsoleStatutoryDiscountApplyPayableBasisService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.ApplyStatutoryDiscountPayableBasis;

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter _writer;

    /// <summary>
    /// Creates an Operator Console statutory discount payable-basis application service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountApplyPayableBasisService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter writer)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResult> ApplyAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

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
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
        if (!persistedEvaluation.Allowed)
        {
            return DeniedResult(command, persistedEvaluation);
        }

        var persistedApplication = await _writer.ApplyAsync(
            new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand(
                command.ValidationId,
                command.OriginalTariffSnapshotId,
                command.UserId,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        return new OperatorConsoleStatutoryDiscountApplyPayableBasisResult(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            persistedApplication.ApplicationAccepted,
            persistedApplication.ApplicationPersisted,
            persistedApplication.PayableBasisApplicationId,
            persistedApplication.StatutoryDiscountValidationId,
            persistedApplication.ParkingSessionId,
            persistedApplication.OriginalTariffSnapshotId,
            persistedApplication.AppliedTariffSnapshotId,
            persistedApplication.ApplicationStatus,
            persistedApplication.AlreadyApplied,
            persistedApplication.GrossAmountMinorUnits,
            persistedApplication.VatAmountMinorUnits,
            persistedApplication.VatExclusiveAmountMinorUnits,
            persistedApplication.StatutoryDiscountAmountMinorUnits,
            persistedApplication.FinalPayableAmountMinorUnits,
            persistedApplication.CurrencyCode,
            persistedApplication.StatutoryDiscountPolicyId,
            persistedApplication.ResolvedJurisdictionId,
            persistedApplication.PolicyResolutionBasis,
            persistedApplication.PolicyCode,
            persistedApplication.BenefitType,
            persistedApplication.NationalLawReference,
            persistedApplication.OrdinanceReference,
            persistedApplication.PolicySnapshotUsed,
            persistedApplication.IneligibilityReason,
            persistedApplication.ErrorCode,
            persistedEvaluation.CorrelationId);
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResult DeniedResult(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: false,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            ApplicationAccepted: false,
            ApplicationPersisted: false,
            PayableBasisApplicationId: null,
            StatutoryDiscountValidationId: command.ValidationId,
            ParkingSessionId: null,
            OriginalTariffSnapshotId: command.OriginalTariffSnapshotId,
            AppliedTariffSnapshotId: null,
            ApplicationStatus: null,
            AlreadyApplied: false,
            GrossAmountMinorUnits: null,
            VatAmountMinorUnits: null,
            VatExclusiveAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: null,
            FinalPayableAmountMinorUnits: null,
            CurrencyCode: null,
            StatutoryDiscountPolicyId: null,
            ResolvedJurisdictionId: null,
            PolicyResolutionBasis: null,
            PolicyCode: null,
            BenefitType: null,
            NationalLawReference: null,
            OrdinanceReference: null,
            PolicySnapshotUsed: false,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            persistedEvaluation.CorrelationId);

    private static void Validate(OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command)
    {
        ValidateGuid(command.ValidationId, nameof(command.ValidationId));
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

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
}
