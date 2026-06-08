using System.Text.RegularExpressions;
using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console statutory discount validation draft service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before a statutory discount validation draft is created.
/// - Denied access does not create statutory discount validation drafts.
/// - This service never applies a discount or mutates tariff, payable, payment, provider, gate, coupon, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftService : IOperatorConsoleStatutoryDiscountDraftService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.CreateStatutoryDiscountDraft;
    private const string ValidationStatus = "REQUESTED";
    private static readonly Regex FullIdentifierPattern = new(@"\d{5,}", RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedEntitlementTypes = new(StringComparer.Ordinal)
    {
        "SENIOR_CITIZEN",
        "PWD"
    };

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleSessionLookupReadRepository _sessionRepository;
    private readonly IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository _policyRepository;
    private readonly IOperatorConsoleStatutoryDiscountDraftWriter _draftWriter;
    private readonly ISystemClock _clock;
    private readonly OperatorConsolePolicyReadinessEnvironment _environment;

    /// <summary>
    /// Creates an Operator Console statutory discount validation draft service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDraftService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleSessionLookupReadRepository sessionRepository,
        IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository policyRepository,
        IOperatorConsoleStatutoryDiscountDraftWriter draftWriter,
        ISystemClock clock,
        OperatorConsolePolicyReadinessEnvironment environment)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _policyRepository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
        _draftWriter = draftWriter ?? throw new ArgumentNullException(nameof(draftWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
        OperatorConsoleStatutoryDiscountDraftCommand command,
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
                command.EvidenceAccessIntent,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
        if (!persistedEvaluation.Allowed)
        {
            return DeniedResult(command, persistedEvaluation);
        }

        var session = await _sessionRepository.FindAsync(
            new OperatorConsoleSessionLookupReadRequest(
                command.ParkingSessionId,
                NormalizeOptional(command.TicketReference),
                command.SiteId,
                command.SiteGroupId,
                "PARKING_SESSION_ID"),
            cancellationToken);

        if (session is null)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "SESSION_NOT_FOUND",
                "SESSION_NOT_FOUND");
        }

        if (!string.Equals(session.SessionStatus, "ACTIVE", StringComparison.Ordinal))
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                "SESSION_NOT_ACTIVE",
                "SESSION_NOT_ELIGIBLE_FOR_OPERATOR_WORKFLOW");
        }

        var effectiveDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var policyResolution = await _policyRepository.ResolveAsync(
            new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                session.SiteId,
                command.SiteGroupId ?? session.SiteGroupId,
                entitlementType,
                effectiveDate),
            cancellationToken);

        var readiness = OperatorConsolePolicyReadinessClassifier.Evaluate(
            policyResolution,
            _environment,
            effectiveDate,
            evidenceRequiredByWorkflow: true);

        if (!readiness.PolicyResolved || readiness.Policy is null || !readiness.CanCreateDraft)
        {
            return NotAcceptedResult(
                command,
                persistedEvaluation,
                readiness.IneligibilityReason ?? "STATUTORY_DISCOUNT_POLICY_NOT_READY",
                readiness.ErrorCode ?? "STATUTORY_DISCOUNT_POLICY_NOT_READY",
                readiness);
        }

        var evidenceRequired = command.EvidenceCaptureRequested || readiness.Policy.RequiresEvidence;

        var draft = await _draftWriter.PersistAsync(
            new OperatorConsoleStatutoryDiscountDraftPersistenceCommand(
                command.ParkingSessionId,
                entitlementType,
                evidenceRequired,
                NormalizeOptional(command.ReasonCode),
                command.UserId,
                command.CorrelationId,
                readiness.Policy),
            cancellationToken);

        return new OperatorConsoleStatutoryDiscountDraftResult(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DraftAccepted: true,
            draft.Persisted,
            draft.DraftId,
            command.ParkingSessionId,
            entitlementType,
            draft.ValidationStatus,
            command.EvidenceCaptureRequested,
            draft.EvidenceRequired,
            draft.EvidenceReferenceCreated,
            draft.EvidenceReferenceId,
            draft.ReusedExistingDraft,
            draft.Policy,
            IneligibilityReason: null,
            ErrorCode: null,
            persistedEvaluation.CorrelationId,
            readiness.Classification,
            readiness.RequiresManualReview,
            readiness.IneligibilityReason,
            readiness.OperatorMessage);
    }

    private static OperatorConsoleStatutoryDiscountDraftResult DeniedResult(
        OperatorConsoleStatutoryDiscountDraftCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: false,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            command.ParkingSessionId,
            Normalize(command.EntitlementType),
            ValidationStatus: null,
            command.EvidenceCaptureRequested,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            Policy: null,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            persistedEvaluation.CorrelationId,
            PolicyReadinessClassification: OperatorConsolePolicyReadinessClassifications.NotReady,
            RequiresManualReview: false,
            PolicyReadinessReason: "ACCESS_DENIED",
            OperatorMessage: "Access denied for this Operator Console action.");

    private static OperatorConsoleStatutoryDiscountDraftResult NotAcceptedResult(
        OperatorConsoleStatutoryDiscountDraftCommand command,
        OperatorConsoleAccessEvaluationResult persistedEvaluation,
        string ineligibilityReason,
        string errorCode,
        OperatorConsolePolicyReadinessEvaluation? readiness = null) =>
        new(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            command.ParkingSessionId,
            Normalize(command.EntitlementType),
            ValidationStatus: null,
            command.EvidenceCaptureRequested,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            Policy: null,
            ineligibilityReason,
            errorCode,
            persistedEvaluation.CorrelationId,
            readiness?.Classification ?? OperatorConsolePolicyReadinessClassifications.NotReady,
            readiness?.RequiresManualReview ?? false,
            readiness?.IneligibilityReason ?? ineligibilityReason,
            readiness?.OperatorMessage ?? "The statutory discount draft request was not accepted.");

    private static string Validate(OperatorConsoleStatutoryDiscountDraftCommand command)
    {
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateGuid(command.ParkingSessionId, nameof(command.ParkingSessionId));

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }

        var entitlementType = Normalize(command.EntitlementType);
        if (string.IsNullOrWhiteSpace(entitlementType))
        {
            throw new ArgumentException("EntitlementType is required.", nameof(command.EntitlementType));
        }

        if (!SupportedEntitlementTypes.Contains(entitlementType))
        {
            throw new ArgumentException("EntitlementType must be SENIOR_CITIZEN or PWD.", nameof(command.EntitlementType));
        }

        if (string.IsNullOrWhiteSpace(command.IdDocumentType))
        {
            throw new ArgumentException("IdDocumentType is required.", nameof(command.IdDocumentType));
        }

        if (string.IsNullOrWhiteSpace(command.IssuingAuthority))
        {
            throw new ArgumentException("IssuingAuthority is required.", nameof(command.IssuingAuthority));
        }

        ValidateMaskedIdReference(command.MaskedIdReference);

        if (!command.OperatorAttestation)
        {
            throw new ArgumentException("OperatorAttestation must be true.", nameof(command.OperatorAttestation));
        }

        return entitlementType;
    }

    private static void ValidateMaskedIdReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("MaskedIdReference is required.", nameof(value));
        }

        var trimmed = value.Trim();
        var compactLength = trimmed.Count(char.IsLetterOrDigit);
        if (trimmed.Length > 16 || compactLength > 8 || FullIdentifierPattern.IsMatch(trimmed))
        {
            throw new ArgumentException("MaskedIdReference must contain only a masked or last-four style reference.", nameof(value));
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
