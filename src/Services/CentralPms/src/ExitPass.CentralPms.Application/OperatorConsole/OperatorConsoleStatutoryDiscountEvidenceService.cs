namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated metadata-only statutory discount evidence intake service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Captures evidence metadata only; no raw evidence bytes, OCR, automated ID validation, or storage subsystem.
/// - Evidence capture may satisfy approval gating by updating statutory discount evidence state only.
/// - Does not mutate payable-basis computation, payment, provider, gate, coupon, settlement, or reconciliation state.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountEvidenceService
    : IOperatorConsoleStatutoryDiscountEvidenceService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string CaptureEvidenceActionCode = OperatorConsoleActionCodes.CaptureEvidence;
    private const string ViewEvidenceActionCode = OperatorConsoleActionCodes.ViewEvidence;

    private static readonly HashSet<string> SupportedEvidenceTypes = new(StringComparer.Ordinal)
    {
        "SENIOR_CITIZEN_ID",
        "PWD_ID",
        "OTHER_SUPPORTING_DOCUMENT"
    };

    private static readonly HashSet<string> SupportedCaptureMethods = new(StringComparer.Ordinal)
    {
        "UPLOAD",
        "MANUAL_REFERENCE",
        "OPERATOR_CONFIRMED"
    };

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleStatutoryDiscountEvidenceRepository _repository;

    /// <summary>
    /// Creates an Operator Console statutory discount evidence service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountEvidenceService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleStatutoryDiscountEvidenceRepository repository)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult?> CaptureAsync(
        OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var evidenceType = ValidateCapture(command);

        var context = await _repository.GetDraftContextAsync(command.DraftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var requiredTypes = RequiredEvidenceTypes(context.EntitlementType, context.EvidenceRequired);
        if (requiredTypes.Count > 0 && !requiredTypes.Contains(evidenceType, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"EvidenceType must match required evidence type for {context.EntitlementType}.",
                nameof(command.EvidenceType));
        }

        var evaluation = await EvaluateAsync(command, context, CaptureEvidenceActionCode, cancellationToken);
        if (!evaluation.Allowed)
        {
            return new OperatorConsoleStatutoryDiscountEvidenceCaptureResult(
                Guid.Empty,
                command.DraftId,
                evidenceType,
                Normalize(command.CaptureMethod),
                null,
                null,
                null,
                null,
                null,
                command.UserId,
                evaluation.EvaluatedAt,
                "NOT_REDACTED",
                "PENDING_REVIEW",
                context.EvidenceCaptured,
                context.ValidationStatus,
                AccessAllowed: false,
                ErrorCode: "ACCESS_DENIED",
                evaluation.CorrelationId);
        }

        return await _repository.CaptureAsync(
            new OperatorConsoleStatutoryDiscountEvidencePersistenceCommand(
                command.DraftId,
                evidenceType,
                Normalize(command.CaptureMethod),
                NormalizeOptional(command.FileName),
                NormalizeOptional(command.ContentType),
                command.SizeBytes,
                BuildStorageReference(command),
                MaskReference(command.ReferenceNumber),
                command.UserId,
                command.CorrelationId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountEvidenceListResult?> ListAsync(
        OperatorConsoleStatutoryDiscountEvidenceListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateGuid(query.DraftId, nameof(query.DraftId));
        ValidateGuid(query.UserId, nameof(query.UserId));
        ValidateGuid(query.CorrelationId, nameof(query.CorrelationId));

        var context = await _repository.GetDraftContextAsync(query.DraftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                query.UserId,
                query.OperatorDeviceBindingId,
                query.SiteId ?? context.SiteId,
                query.SiteGroupId ?? context.SiteGroupId,
                query.OperatorShiftId,
                WorkflowCode,
                ViewEvidenceActionCode,
                context.ParkingSessionId,
                EvidenceAccessIntent: "OPERATOR_EVIDENCE_LIST",
                IdempotencyKey: $"operator-console-evidence-list-{query.DraftId}-{query.CorrelationId}",
                query.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
        if (!persistedEvaluation.Allowed)
        {
            return new OperatorConsoleStatutoryDiscountEvidenceListResult(
                query.DraftId,
                context.EvidenceRequired,
                context.EvidenceCaptured,
                RequiredEvidenceTypes(context.EntitlementType, context.EvidenceRequired),
                EvidenceCount: 0,
                LatestEvidenceStatus: null,
                Array.Empty<OperatorConsoleStatutoryDiscountEvidenceMetadataResult>(),
                persistedEvaluation.CorrelationId);
        }

        return await _repository.ListAsync(query.DraftId, query.CorrelationId, cancellationToken);
    }

    private async Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
        OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command,
        OperatorConsoleStatutoryDiscountEvidenceDraftContext context,
        string actionCode,
        CancellationToken cancellationToken)
    {
        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId ?? context.SiteId,
                command.SiteGroupId ?? context.SiteGroupId,
                command.OperatorShiftId,
                WorkflowCode,
                actionCode,
                context.ParkingSessionId,
                EvidenceAccessIntent: "OPERATOR_EVIDENCE_CAPTURE",
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        return await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);
    }

    private static string ValidateCapture(OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command)
    {
        ValidateGuid(command.DraftId, nameof(command.DraftId));
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }

        if (!command.OperatorConfirmation)
        {
            throw new ArgumentException("OperatorConfirmation must be true.", nameof(command.OperatorConfirmation));
        }

        var evidenceType = Normalize(command.EvidenceType);
        if (!SupportedEvidenceTypes.Contains(evidenceType))
        {
            throw new ArgumentException("EvidenceType must be SENIOR_CITIZEN_ID, PWD_ID, or OTHER_SUPPORTING_DOCUMENT.", nameof(command.EvidenceType));
        }

        var captureMethod = Normalize(command.CaptureMethod);
        if (!SupportedCaptureMethods.Contains(captureMethod))
        {
            throw new ArgumentException("CaptureMethod must be UPLOAD, MANUAL_REFERENCE, or OPERATOR_CONFIRMED.", nameof(command.CaptureMethod));
        }

        if (captureMethod == "UPLOAD")
        {
            if (string.IsNullOrWhiteSpace(command.FileName))
            {
                throw new ArgumentException("FileName is required for UPLOAD metadata capture.", nameof(command.FileName));
            }

            if (string.IsNullOrWhiteSpace(command.ContentType))
            {
                throw new ArgumentException("ContentType is required for UPLOAD metadata capture.", nameof(command.ContentType));
            }

            if (!command.SizeBytes.HasValue || command.SizeBytes <= 0)
            {
                throw new ArgumentException("SizeBytes must be positive for UPLOAD metadata capture.", nameof(command.SizeBytes));
            }
        }

        if (captureMethod == "MANUAL_REFERENCE" && string.IsNullOrWhiteSpace(command.ReferenceNumber))
        {
            throw new ArgumentException("ReferenceNumber is required for MANUAL_REFERENCE capture.", nameof(command.ReferenceNumber));
        }

        return evidenceType;
    }

    /// <summary>
    /// Returns the metadata evidence types required for the statutory discount entitlement.
    /// </summary>
    public static IReadOnlyList<string> RequiredEvidenceTypes(string entitlementType, bool evidenceRequired)
    {
        if (!evidenceRequired)
        {
            return Array.Empty<string>();
        }

        return string.Equals(entitlementType, "PWD", StringComparison.Ordinal)
            ? ["PWD_ID"]
            : ["SENIOR_CITIZEN_ID"];
    }

    private static string? BuildStorageReference(OperatorConsoleStatutoryDiscountEvidenceCaptureCommand command)
    {
        var captureMethod = Normalize(command.CaptureMethod);
        if (captureMethod == "MANUAL_REFERENCE")
        {
            var masked = MaskReference(command.ReferenceNumber);
            return masked is null ? null : $"manual-reference:{masked}";
        }

        if (captureMethod == "UPLOAD")
        {
            var fileName = NormalizeOptional(command.FileName);
            var contentType = NormalizeOptional(command.ContentType);
            return $"upload-metadata:file={fileName};contentType={contentType};sizeBytes={command.SizeBytes}";
        }

        if (!string.IsNullOrWhiteSpace(command.StorageReference))
        {
            return $"operator-confirmed:{NormalizeOptional(command.StorageReference)}";
        }

        return "operator-confirmed";
    }

    private static string? MaskReference(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        var tail = normalized.Length <= 4 ? normalized : normalized[^4..];
        return $"****{tail}";
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
