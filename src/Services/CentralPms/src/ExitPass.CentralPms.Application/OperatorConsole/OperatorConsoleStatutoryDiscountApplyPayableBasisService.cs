using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;

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
    private readonly IStatutoryDiscountServiceChannelAuthorizationService _serviceChannelAuthorizationService;

    /// <summary>
    /// Creates an Operator Console statutory discount payable-basis application service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountApplyPayableBasisService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter writer,
        IOperatorConsoleStatutoryDiscountReadService readService,
        IStatutoryDiscountStagedCommandService stagedCommandService,
        IStatutoryDiscountServiceChannelAuthorizationService serviceChannelAuthorizationService)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _stagedCommandService = stagedCommandService ?? throw new ArgumentNullException(nameof(stagedCommandService));
        _serviceChannelAuthorizationService = serviceChannelAuthorizationService ??
            throw new ArgumentNullException(nameof(serviceChannelAuthorizationService));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResult> ApplyAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        ApplyAuthorization authorization;
        if (command.ServiceChannelCaller is not null)
        {
            var serviceAuthorization = await _serviceChannelAuthorizationService.AuthorizeAsync(
                    command.ServiceChannelCaller,
                    command.SiteId,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
            authorization = new ApplyAuthorization(
                command.CorrelationId,
                serviceAuthorization.Allowed,
                serviceAuthorization.Decision,
                serviceAuthorization.DenialReasons,
                serviceAuthorization.AuditPersisted,
                serviceAuthorization.ErrorCode,
                serviceAuthorization.CorrelationId);
        }
        else
        {
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
            authorization = new ApplyAuthorization(
                persistedEvaluation.EvaluationId,
                persistedEvaluation.Allowed,
                persistedEvaluation.Decision,
                persistedEvaluation.DenialReasons,
                persistedEvaluation.Persisted,
                ErrorCode: null,
                persistedEvaluation.CorrelationId);
        }

        if (!authorization.Allowed)
        {
            return DeniedResult(command, authorization);
        }

        var detail = await _readService.GetDraftAsync(
                new OperatorConsoleStatutoryDiscountDraftDetailQuery(command.ValidationId, command.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return NotAcceptedResult(
                command,
                authorization,
                "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND",
                "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND");
        }

        if (command.ServiceChannelCaller is not null && detail.SiteId != command.SiteId)
        {
            return NotAcceptedResult(
                command,
                authorization,
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND");
        }

        if (!string.Equals(detail.ValidationStatus, "APPROVED", StringComparison.Ordinal))
        {
            return NotAcceptedResult(
                command,
                authorization,
                "STATUTORY_DISCOUNT_NOT_APPROVED",
                "STATUTORY_DISCOUNT_NOT_APPROVED",
                detail);
        }

        if (!detail.StatutoryDiscountDecisionCommandId.HasValue)
        {
            return NotAcceptedResult(
                command,
                authorization,
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
                authorization,
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                detail);
        }


        if (command.ServiceChannelCaller is not null &&
            !string.Equals(
                decision.SourceChannel,
                command.ServiceChannelCaller.SourceChannel,
                StringComparison.Ordinal))
        {
            return NotAcceptedResult(
                command,
                authorization,
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                detail,
                decision.StatutoryDiscountDecisionCommandId);
        }

        if (decision.CommandStatus is not StatutoryDiscountDecisionV2CommandStates.Completed ||
            decision.DecisionResultStatus is not StatutoryDiscountDecisionV2ResultStates.Approved)
        {
            return NotAcceptedResult(
                command,
                authorization,
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
            authorization.EvaluationId,
            AccessAllowed: true,
            authorization.Decision,
            authorization.DenialReasons,
            authorization.Persisted,
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
            authorization.CorrelationId,
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
        StatutoryDiscountPayableBasisApplicationV1Record application;
        if (existingApplication is null)
        {
            StatutoryDiscountPayableBasisApplicationV1Command applicationCommand;
            try
            {
                applicationCommand = ToApplicationV1Command(command, detail, decision, stageKey);
            }
            catch (ArgumentException exception) when (IsApprovedPayableBasisFactsRequired(exception))
            {
                return await ApplyAndRecordLegacyApplicationAfterWriterAsync(
                        command,
                        detail,
                        decision,
                        stageKey,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var applicationStart = await _stagedCommandService.CreateOrResolveApplicationAsync(
                    applicationCommand,
                    cancellationToken)
                .ConfigureAwait(false);

            if (applicationStart.SemanticConflict)
            {
                return NotAcceptedPersistence(
                    detail,
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT",
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT",
                    applicationStart.Record?.StatutoryDiscountPayableBasisApplicationCommandId);
            }

            if (applicationStart.Record is null)
            {
                var safeErrorCode = applicationStart.SafeErrorCode ??
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE";
                return NotAcceptedPersistence(detail, safeErrorCode, safeErrorCode);
            }

            application = applicationStart.Record;
        }
        else
        {
            application = existingApplication;
        }

        return await _stagedCommandService.ExecuteWithApplicationLockAsync(
            application,
            token => ApplyAndRecordCanonicalApplicationUnderLockAsync(command, detail, decision, application, stageKey, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult>
        ApplyAndRecordLegacyApplicationAfterWriterAsync(
            OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
            OperatorConsoleStatutoryDiscountDraftDetailResult detail,
            StatutoryDiscountDecisionV2Record decision,
            string stageKey,
            CancellationToken cancellationToken)
    {
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult persisted;
        try
        {
            persisted = await _writer.ApplyAsync(
                    ToPersistenceCommand(command, stageKey),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "40P01")
        {
            return await ReconcileAfterDeadlockAsync(detail, decision, command.CorrelationId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!persisted.ApplicationAccepted || persisted.ErrorCode is not null)
        {
            return persisted;
        }

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
                IneligibilityReason = applicationStart.SafeErrorCode ??
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE",
                ErrorCode = applicationStart.SafeErrorCode ??
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_AVAILABLE"
            };
        }

        var application = applicationStart.Record;
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

        var completed = await _stagedCommandService.CompleteApplicationAppliedAsync(
                processing.StatutoryDiscountPayableBasisApplicationCommandId,
                persisted.PayableBasisApplicationId,
                persisted.AppliedTariffSnapshotId,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return persisted with
        {
            StatutoryDiscountPayableBasisApplicationCommandId =
                completed.StatutoryDiscountPayableBasisApplicationCommandId
        };
    }

    private async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult>
        ApplyAndRecordCanonicalApplicationUnderLockAsync(
            OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
            OperatorConsoleStatutoryDiscountDraftDetailResult detail,
            StatutoryDiscountDecisionV2Record decision,
            StatutoryDiscountPayableBasisApplicationV1Record application,
            string stageKey,
            CancellationToken cancellationToken)
    {
        application = await _stagedCommandService.GetApplicationAsync(
                application.StatutoryDiscountPayableBasisApplicationCommandId,
                cancellationToken)
            .ConfigureAwait(false) ?? application;

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return AppliedPersistenceFromCanonical(detail, application, alreadyApplied: true);
        }

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable)
        {
            return NotAcceptedPersistence(
                detail,
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED",
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED",
                application.StatutoryDiscountPayableBasisApplicationCommandId);
        }

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing &&
            !command.AllowProcessingApplicationCompletion)
        {
            return NotAcceptedPersistence(
                detail,
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
                application.StatutoryDiscountPayableBasisApplicationCommandId);
        }

        var processing = await _stagedCommandService.MarkApplicationProcessingAsync(
                application.StatutoryDiscountPayableBasisApplicationCommandId,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var persisted = await _writer.ApplyAsync(
                    ToPersistenceCommand(command, stageKey),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!persisted.ApplicationAccepted || persisted.ErrorCode is not null)
            {
                var safeErrorCode = persisted.ErrorCode ??
                    persisted.IneligibilityReason ??
                    "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_REJECTED";
                await _stagedCommandService.RecordApplicationFailureAsync(
                        processing.StatutoryDiscountPayableBasisApplicationCommandId,
                        retryable: false,
                        safeErrorCode,
                        command.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);

                return persisted with
                {
                    StatutoryDiscountPayableBasisApplicationCommandId =
                        processing.StatutoryDiscountPayableBasisApplicationCommandId
                };
            }

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
        catch (PostgresException ex) when (ex.SqlState == "40P01")
        {
            return await ReconcileAfterDeadlockAsync(detail, decision, command.CorrelationId, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ReconcileAfterDeadlockAsync(
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountDecisionV2Record decision,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var application = await _stagedCommandService.GetApplicationByDecisionAsync(
                decision.StatutoryDiscountDecisionCommandId,
                cancellationToken)
            .ConfigureAwait(false);

        if (application is null)
        {
            return NotAcceptedPersistence(
                detail,
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE");
        }

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied)
        {
            return AppliedPersistenceFromCanonical(detail, application, alreadyApplied: true);
        }

        if (application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable)
        {
            return NotAcceptedPersistence(
                detail,
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED",
                application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED",
                application.StatutoryDiscountPayableBasisApplicationCommandId);
        }

        return NotAcceptedPersistence(
            detail,
            application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
            application.SafeErrorCode ?? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS",
            application.StatutoryDiscountPayableBasisApplicationCommandId);
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
        ApplyAuthorization authorization) =>
        new(
            authorization.EvaluationId,
            AccessAllowed: false,
            authorization.Decision,
            authorization.DenialReasons,
            authorization.Persisted,
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
            IneligibilityReason: authorization.ErrorCode ?? "ACCESS_DENIED",
            ErrorCode: authorization.ErrorCode,
            authorization.CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResult NotAcceptedResult(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        ApplyAuthorization authorization,
        string ineligibilityReason,
        string errorCode,
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail = null,
        Guid? statutoryDiscountDecisionCommandId = null,
        Guid? statutoryDiscountPayableBasisApplicationCommandId = null) =>
        new(
            authorization.EvaluationId,
            AccessAllowed: true,
            authorization.Decision,
            authorization.DenialReasons,
            authorization.Persisted,
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
            authorization.CorrelationId,
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

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand ToPersistenceCommand(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        string stageKey) =>
        new(
            command.ValidationId,
            command.OriginalTariffSnapshotId,
            command.ServiceChannelCaller is null ? command.UserId : null,
            stageKey,
            command.CorrelationId,
            command.ServiceChannelCaller?.ServiceIdentityId,
            command.ServiceChannelCaller is null ? "OPERATOR_CONSOLE" : "SYSTEM");

    private sealed record ApplyAuthorization(
        Guid EvaluationId,
        bool Allowed,
        string Decision,
        IReadOnlyList<string> DenialReasons,
        bool Persisted,
        string? ErrorCode,
        Guid CorrelationId);

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static bool IsApprovedPayableBasisFactsRequired(ArgumentException exception) =>
        string.Equals(
            exception.Message,
            "Approved statutory discount payable-basis facts are required.",
            StringComparison.Ordinal);

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
            decision.SourceChannel,
            idempotencyKey,
            command.CorrelationId);
    }

    private static StatutoryDiscountPayableBasisApplicationV1Command ToApplicationV1Command(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        OperatorConsoleStatutoryDiscountDraftDetailResult detail,
        StatutoryDiscountDecisionV2Record decision,
        string idempotencyKey)
    {
        var statutoryDiscountAmountMinorUnits =
            decision.StatutoryDiscountAmountMinorUnits ?? detail.StatutoryDiscountAmountMinorUnits;
        var finalPayableAmountMinorUnits =
            decision.NetPayableAmountMinorUnits ?? detail.FinalPayableAmountMinorUnits ?? detail.PayableAmountMinorUnits;
        var currency = decision.Currency ?? detail.CurrencyCode;

        if (statutoryDiscountAmountMinorUnits is null ||
            finalPayableAmountMinorUnits is null ||
            string.IsNullOrWhiteSpace(currency))
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
            command.OriginalTariffSnapshotId ?? decision.OriginalTariffSnapshotId ?? detail.OriginalTariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            decision.AppliedPolicyReferenceId ?? detail.StatutoryDiscountPolicyId,
            decision.PolicyResolutionBasis ?? detail.PolicyResolutionBasis,
            statutoryDiscountAmountMinorUnits.Value,
            decision.VatExclusiveAmountMinorUnits ?? detail.VatExclusiveAmountMinorUnits,
            decision.VatAmountMinorUnits ?? detail.VatAmountMinorUnits,
            finalPayableAmountMinorUnits.Value,
            currency,
            decision.SourceChannel,
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
