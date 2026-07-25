using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryDiscounts;

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
    private readonly IOperatorConsoleStatutoryDiscountReadService _readService;
    private readonly IStatutoryDiscountStagedCommandService _stagedCommandService;

    /// <summary>
    /// Creates an Operator Console statutory discount payable-basis application service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountApplyPayableBasisService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter writer,
        IOperatorConsoleStatutoryDiscountReadService readService,
        IStatutoryDiscountStagedCommandService stagedCommandService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _stagedCommandService = stagedCommandService ?? throw new ArgumentNullException(nameof(stagedCommandService));
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

        var detail = await _readService.GetDraftAsync(
                new OperatorConsoleStatutoryDiscountDraftDetailQuery(command.ValidationId, command.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND",
                "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND");
        }

        if (!string.Equals(detail.ValidationStatus, "APPROVED", StringComparison.Ordinal))
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "STATUTORY_DISCOUNT_NOT_APPROVED",
                "STATUTORY_DISCOUNT_NOT_APPROVED",
                detail);
        }

        if (!detail.StatutoryDiscountDecisionCommandId.HasValue)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                detail);
        }

        var decision = await _stagedCommandService.GetDecisionAsync(
                detail.StatutoryDiscountDecisionCommandId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (decision is null)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                detail);
        }

        if (decision.CommandStatus is not StatutoryDiscountDecisionV2CommandStates.Completed ||
            decision.DecisionResultStatus is not StatutoryDiscountDecisionV2ResultStates.Approved)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED",
                "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED",
                detail,
                decision.StatutoryDiscountDecisionCommandId);
        }

        var stageKey = DeriveLegacyApplicationStageIdempotencyKey(
            command.IdempotencyKey,
            decision.StatutoryDiscountDecisionCommandId);
        var existingCanonicalApplication = await _stagedCommandService.GetApplicationByDecisionAsync(
                decision.StatutoryDiscountDecisionCommandId,
                cancellationToken)
            .ConfigureAwait(false);
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult persistedApplication;
        if (existingCanonicalApplication?.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable)
        {
            persistedApplication = ResolveExistingApplication(detail, existingCanonicalApplication);
        }
        else
        {
            persistedApplication = await ApplyAndRecordCanonicalApplicationAsync(
                    command,
                    detail,
                    decision,
                    existingCanonicalApplication,
                    stageKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }

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
            persistedEvaluation.CorrelationId,
            decision.StatutoryDiscountDecisionCommandId,
            persistedApplication.StatutoryDiscountPayableBasisApplicationCommandId);
    }

    private async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ApplyAndRecordCanonicalApplicationAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountDecisionV2Record decision,
        StatutoryDiscountPayableBasisApplicationV1Record? existingApplication,
        string stageKey,
        CancellationToken cancellationToken)
    {
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult persisted;
        try
        {
            persisted = await _writer.ApplyAsync(
                    new OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand(
                        command.ValidationId,
                        command.OriginalTariffSnapshotId,
                        command.UserId,
                        stageKey,
                        command.CorrelationId),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            throw;
        }

        if (!persisted.ApplicationAccepted || persisted.ErrorCode is not null)
        {
            return persisted;
        }

        StatutoryDiscountPayableBasisApplicationV1Record application;
        if (existingApplication is null)
        {
            var applicationStart = await _stagedCommandService.CreateOrResolveApplicationAsync(
                    ToApplicationV1Command(command, detail, decision, persisted, stageKey),
                    cancellationToken)
                .ConfigureAwait(false);
            if (applicationStart.SemanticConflict)
            {
                return persisted with
                {
                    ApplicationAccepted = false,
                    ApplicationPersisted = false,
                    IneligibilityReason = "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT",
                    ErrorCode = "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT",
                    StatutoryDiscountPayableBasisApplicationCommandId =
                        applicationStart.Record?.StatutoryDiscountPayableBasisApplicationCommandId
                };
            }

            if (applicationStart.Record is null)
            {
                return persisted with
                {
                    ApplicationAccepted = false,
                    ApplicationPersisted = false,
                    IneligibilityReason = applicationStart.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE",
                    ErrorCode = applicationStart.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE"
                };
            }

            application = applicationStart.Record;
        }
        else
        {
            application = existingApplication;
        }

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return persisted with
            {
                AlreadyApplied = true,
                StatutoryDiscountPayableBasisApplicationCommandId =
                    application.StatutoryDiscountPayableBasisApplicationCommandId
            };
        }

        var processing = await _stagedCommandService.MarkApplicationProcessingAsync(
                application.StatutoryDiscountPayableBasisApplicationCommandId,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var completed = await _stagedCommandService.CompleteApplicationAppliedAsync(
                    processing.StatutoryDiscountPayableBasisApplicationCommandId,
                    persisted.PayableBasisApplicationId,
                    persisted.AppliedTariffSnapshotId,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);

            return persisted with
            {
                StatutoryDiscountPayableBasisApplicationCommandId = completed.StatutoryDiscountPayableBasisApplicationCommandId
            };
        }
        catch
        {
            await _stagedCommandService.RecordApplicationFailureAsync(
                    application.StatutoryDiscountPayableBasisApplicationCommandId,
                    retryable: true,
                    "OPERATOR_CONSOLE_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
                    command.CorrelationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult ResolveExistingApplication(
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountPayableBasisApplicationV1Record application)
    {
        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return AppliedPersistenceFromCanonical(detail, application, alreadyApplied: true);
        }

        return NotAcceptedPersistence(
            detail,
            application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
            application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
            application.StatutoryDiscountPayableBasisApplicationCommandId);
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

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResult NotAcceptedResult(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        string ineligibilityReason,
        string errorCode,
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail = null,
        Guid? statutoryDiscountDecisionCommandId = null,
        Guid? statutoryDiscountPayableBasisApplicationCommandId = null) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            ApplicationAccepted: false,
            ApplicationPersisted: false,
            PayableBasisApplicationId: detail?.PayableBasisApplicationId,
            StatutoryDiscountValidationId: command.ValidationId,
            ParkingSessionId: detail?.ParkingSessionId,
            OriginalTariffSnapshotId: command.OriginalTariffSnapshotId ?? detail?.OriginalTariffSnapshotId,
            AppliedTariffSnapshotId: detail?.AppliedTariffSnapshotId,
            ApplicationStatus: detail?.PayableBasisApplicationStatus,
            AlreadyApplied: false,
            GrossAmountMinorUnits: detail?.OriginalAmountMinorUnits,
            VatAmountMinorUnits: detail?.VatAmountMinorUnits,
            VatExclusiveAmountMinorUnits: detail?.VatExclusiveAmountMinorUnits,
            StatutoryDiscountAmountMinorUnits: detail?.StatutoryDiscountAmountMinorUnits,
            FinalPayableAmountMinorUnits: detail?.FinalPayableAmountMinorUnits,
            CurrencyCode: detail?.CurrencyCode,
            StatutoryDiscountPolicyId: detail?.StatutoryDiscountPolicyId,
            ResolvedJurisdictionId: detail?.ResolvedJurisdictionId,
            PolicyResolutionBasis: detail?.PolicyResolutionBasis,
            PolicyCode: detail?.PolicyCode,
            BenefitType: detail?.BenefitType,
            NationalLawReference: detail?.NationalLawReference,
            OrdinanceReference: detail?.OrdinanceReference,
            PolicySnapshotUsed: detail?.PolicySnapshot is not null,
            ineligibilityReason,
            errorCode,
            persistedEvaluation.CorrelationId,
            statutoryDiscountDecisionCommandId ?? detail?.StatutoryDiscountDecisionCommandId,
            statutoryDiscountPayableBasisApplicationCommandId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult NotAcceptedPersistence(
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        string ineligibilityReason,
        string errorCode,
        Guid? statutoryDiscountPayableBasisApplicationCommandId = null) =>
        new(
            ApplicationAccepted: false,
            ApplicationPersisted: false,
            detail.PayableBasisApplicationId,
            detail.DraftId,
            detail.ParkingSessionId,
            detail.OriginalTariffSnapshotId,
            detail.AppliedTariffSnapshotId,
            detail.PayableBasisApplicationStatus,
            AlreadyApplied: false,
            detail.OriginalAmountMinorUnits,
            detail.VatAmountMinorUnits,
            detail.VatExclusiveAmountMinorUnits,
            detail.StatutoryDiscountAmountMinorUnits,
            detail.FinalPayableAmountMinorUnits,
            detail.CurrencyCode,
            detail.StatutoryDiscountPolicyId,
            detail.ResolvedJurisdictionId,
            detail.PolicyResolutionBasis,
            detail.PolicyCode,
            detail.BenefitType,
            detail.NationalLawReference,
            detail.OrdinanceReference,
            PolicySnapshotUsed: detail.PolicySnapshot is not null,
            ineligibilityReason,
            errorCode,
            statutoryDiscountPayableBasisApplicationCommandId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult AppliedPersistenceFromCanonical(
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountPayableBasisApplicationV1Record application,
        bool alreadyApplied) =>
        new(
            ApplicationAccepted: true,
            ApplicationPersisted: true,
            application.StatutoryDiscountPayableBasisApplicationId ?? detail.PayableBasisApplicationId,
            application.StatutoryDiscountValidationId ?? detail.DraftId,
            application.ParkingSessionId,
            application.OriginalTariffSnapshotId ?? detail.OriginalTariffSnapshotId,
            application.AppliedTariffSnapshotId ?? detail.AppliedTariffSnapshotId,
            "APPLIED",
            alreadyApplied,
            detail.OriginalAmountMinorUnits,
            application.ApprovedVatAmountMinorUnits ?? detail.VatAmountMinorUnits,
            application.ApprovedVatExclusiveAmountMinorUnits ?? detail.VatExclusiveAmountMinorUnits,
            application.ApprovedDiscountAmountMinorUnits,
            application.ApprovedFinalPayableAmountMinorUnits,
            application.Currency,
            detail.StatutoryDiscountPolicyId,
            detail.ResolvedJurisdictionId,
            application.PolicyResolutionBasis ?? detail.PolicyResolutionBasis,
            detail.PolicyCode,
            detail.BenefitType,
            detail.NationalLawReference,
            detail.OrdinanceReference,
            PolicySnapshotUsed: detail.PolicySnapshot is not null,
            IneligibilityReason: null,
            ErrorCode: null,
            application.StatutoryDiscountPayableBasisApplicationCommandId);

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

    private static StatutoryDiscountPayableBasisApplicationV1Command ToApplicationV1Command(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountDecisionV2Record decision,
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult persisted,
        string idempotencyKey)
    {
        if (persisted.PayableBasisApplicationId is null ||
            persisted.AppliedTariffSnapshotId is null ||
            persisted.StatutoryDiscountAmountMinorUnits is null ||
            persisted.FinalPayableAmountMinorUnits is null ||
            string.IsNullOrWhiteSpace(persisted.CurrencyCode))
        {
            throw new ArgumentException("Approved statutory discount payable-basis facts are required.");
        }

        return new StatutoryDiscountPayableBasisApplicationV1Command(
            detail.DraftId,
            decision.StatutoryDiscountDecisionCommandId,
            decision.ParkingSessionId,
            detail.SiteId,
            decision.EntitlementType,
            decision.StatutoryDiscountValidationId ?? detail.DraftId,
            persisted.OriginalTariffSnapshotId ?? command.OriginalTariffSnapshotId ?? decision.OriginalTariffSnapshotId ?? detail.OriginalTariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            decision.AppliedPolicyReferenceId ?? persisted.StatutoryDiscountPolicyId ?? detail.StatutoryDiscountPolicyId,
            decision.PolicyResolutionBasis ?? persisted.PolicyResolutionBasis ?? detail.PolicyResolutionBasis,
            persisted.StatutoryDiscountAmountMinorUnits.Value,
            persisted.VatExclusiveAmountMinorUnits,
            persisted.VatAmountMinorUnits,
            persisted.FinalPayableAmountMinorUnits.Value,
            persisted.CurrencyCode,
            StatutoryDiscountSourceChannels.OperatorConsole,
            idempotencyKey,
            command.CorrelationId);
    }

    private static string DeriveLegacyApplicationStageIdempotencyKey(
        string idempotencyKey,
        Guid statutoryDiscountDecisionCommandId)
    {
        var source = $"operator-console-statutory-discount-payable-basis-application-v1:{statutoryDiscountDecisionCommandId:N}:{idempotencyKey.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"operator-console-payable-basis-application-v1:sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
