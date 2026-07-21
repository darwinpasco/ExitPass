using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Channel-neutral statutory-discount source-channel constants.
/// </summary>
public static class StatutoryDiscountSourceChannels
{
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string WebPay = "WEBPAY";
    public const string AssistedPaymentTerminal = "ASSISTED_PAYMENT_TERMINAL";

    public static bool IsSupported(string value) =>
        Normalize(value) is OperatorConsole or WebPay or AssistedPaymentTerminal;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}

/// <summary>
/// Stable durable command status values exposed by the shared statutory-discount facade.
/// </summary>
public static class StatutoryDiscountDecisionCommandStatuses
{
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
}

/// <summary>
/// Stable client-result vocabulary for shared statutory-discount decision clients.
/// </summary>
public static class StatutoryDiscountDecisionClientResultStatuses
{
    public const string CreatedDurablyCompleted = "CREATED_DURABLY_COMPLETED";
    public const string IdempotentReplay = "IDEMPOTENT_REPLAY";
    public const string SemanticConflict = "SEMANTIC_CONFLICT";
    public const string InProgress = "IN_PROGRESS";
    public const string RecoverableUsingOriginalKey = "RECOVERABLE_USING_ORIGINAL_KEY";
    public const string Approved = "APPROVED";
    public const string RejectedOrNonApproved = "REJECTED_OR_NON_APPROVED";
    public const string ValidationFailure = "VALIDATION_FAILURE";
    public const string UnsafeIdentityInput = "UNSAFE_IDENTITY_INPUT";
    public const string NotFound = "NOT_FOUND";
    public const string TemporarilyUnavailable = "TEMPORARILY_UNAVAILABLE";
    public const string RetryableFailure = "RETRYABLE_FAILURE";
    public const string NonRetryableFailure = "NON_RETRYABLE_FAILURE";
}

/// <summary>
/// Stable recovery classifications for shared statutory-discount command clients.
/// </summary>
public static class StatutoryDiscountDecisionRecoveryClassifications
{
    public const string None = "NONE";
    public const string ReadCanonicalResult = "READ_CANONICAL_RESULT";
    public const string RetryOriginalIdempotencyKey = "RETRY_ORIGINAL_IDEMPOTENCY_KEY";
    public const string WaitThenRetryOriginalIdempotencyKey = "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY";
    public const string CorrectRequestRequired = "CORRECT_REQUEST_REQUIRED";
    public const string NotRecoverable = "NOT_RECOVERABLE";
}

/// <summary>
/// Stable recovery actions for shared statutory-discount command clients.
/// </summary>
public static class StatutoryDiscountDecisionRecoveryActions
{
    public const string ReadCanonicalDecision = "READ_CANONICAL_DECISION";
    public const string RetrySameRequestWithOriginalKey = "RETRY_SAME_REQUEST_WITH_ORIGINAL_IDEMPOTENCY_KEY";
    public const string SubmitCorrectedRequest = "SUBMIT_CORRECTED_REQUEST";
    public const string WaitAndRetry = "WAIT_AND_RETRY";
    public const string DoNotRetry = "DO_NOT_RETRY";
}

/// <summary>
/// Channel-neutral statutory-discount command accepted by Central PMS.
/// </summary>
public sealed record StatutoryDiscountDecisionCommand(
    Guid RequestReference,
    string SourceChannel,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string IdDocumentType,
    string IssuingAuthority,
    DateOnly? ExpiryDate,
    string MaskedIdReference,
    bool EvidenceCaptureRequested,
    IReadOnlyList<StatutoryDiscountEvidenceReference> EvidenceReferences,
    Guid ActorUserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    bool RequesterAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    string? Decision,
    string? DecisionReasonCode,
    Guid? ReviewerUserId,
    bool ReviewerAttestation,
    bool ApplyPayableBasis,
    Guid? OriginalTariffSnapshotId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Metadata-only evidence reference for the shared statutory-discount command.
/// </summary>
public sealed record StatutoryDiscountEvidenceReference(
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus);

/// <summary>
/// Normalized repository command with durable idempotency and semantic hash evidence.
/// </summary>
public sealed record StatutoryDiscountDecisionRepositoryCommand(
    StatutoryDiscountDecisionCommand Command,
    string IdempotencyScope,
    string SemanticRequestHash,
    string SemanticHashSourceVersion,
    DateTimeOffset RequestedAt);

/// <summary>
/// Start result for durable statutory-discount command idempotency.
/// </summary>
public sealed record StatutoryDiscountDecisionBeginResult(
    bool Existing,
    bool SemanticConflict,
    StatutoryDiscountDecisionCommandRecord Record);

/// <summary>
/// Durable statutory-discount command/readback record.
/// </summary>
public sealed record StatutoryDiscountDecisionCommandRecord(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid ParkingSessionId,
    string SourceChannel,
    string EntitlementType,
    string IdempotencyKey,
    string DecisionStatus,
    string ResultClassification,
    string IdempotencyScope,
    string SemanticHashSourceVersion,
    string SemanticRequestHash,
    Guid? StatutoryDiscountValidationId,
    Guid? PayableBasisApplicationId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string? PolicyResolutionBasis,
    Guid? AppliedPolicyReferenceId,
    Guid? FallbackPolicyReferenceId,
    bool LocalOrdinanceApplied,
    long? GrossAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetPayableAmountMinorUnits,
    string? Currency,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? ReasonCode,
    string? ErrorCode,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? AppliedAt);

/// <summary>
/// Canonical shared statutory-discount result.
/// </summary>
public sealed record StatutoryDiscountDecisionResult(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid? StatutoryDiscountValidationId,
    Guid ParkingSessionId,
    string SourceChannel,
    string EntitlementType,
    string DecisionStatus,
    string? PolicyResolutionBasis,
    Guid? AppliedPolicyReferenceId,
    Guid? FallbackPolicyReferenceId,
    bool LocalOrdinanceApplied,
    long? GrossAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetPayableAmountMinorUnits,
    string? Currency,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? ReasonCode,
    string? ErrorCode,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? AppliedAt,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string ResultClassification,
    string SemanticHashSourceVersion);

/// <summary>
/// Controlled rejection from the shared statutory-discount facade.
/// </summary>
public sealed class StatutoryDiscountDecisionRejectedException : Exception
{
    public StatutoryDiscountDecisionRejectedException(string errorCode, string message, bool isNotFound = false)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
        IsNotFound = isNotFound;
    }

    public string ErrorCode { get; }

    public bool IsNotFound { get; }
}

/// <summary>
/// Shared semantic hash helper for the statutory-discount command facade.
/// </summary>
public static class StatutoryDiscountDecisionSemanticHash
{
    public const string SourceVersion = "statutory-discount-decision:sha256:v1";

    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildIdempotencyScope(StatutoryDiscountDecisionCommand command) =>
        $"statutory-discount-decision:{command.ParkingSessionId:N}:{Normalize(command.EntitlementType)}";

    public static string Compute(StatutoryDiscountDecisionCommand command)
    {
        var source = new
        {
            version = SourceVersion,
            parkingSessionId = command.ParkingSessionId,
            siteId = command.SiteId,
            siteGroupId = command.SiteGroupId,
            ticketReference = NormalizeOptional(command.TicketReference),
            plateNumber = NormalizeOptional(command.PlateNumber),
            entitlementType = Normalize(command.EntitlementType),
            idDocumentType = Normalize(command.IdDocumentType),
            issuingAuthority = Normalize(command.IssuingAuthority),
            expiryDate = command.ExpiryDate,
            maskedIdReference = NormalizeOptional(command.MaskedIdReference),
            evidenceCaptureRequested = command.EvidenceCaptureRequested,
            evidenceReferences = (command.EvidenceReferences ?? [])
                .Select(evidence => new
                {
                    evidenceType = Normalize(evidence.EvidenceType),
                    captureMethod = Normalize(evidence.CaptureMethod),
                    fileName = NormalizeOptional(evidence.FileName),
                    contentType = NormalizeOptional(evidence.ContentType),
                    sizeBytes = evidence.SizeBytes,
                    storageReference = NormalizeOptional(evidence.StorageReference),
                    referenceNumberMasked = NormalizeOptional(evidence.ReferenceNumberMasked),
                    verificationStatus = NormalizeOptional(evidence.VerificationStatus)
                })
                .OrderBy(evidence => evidence.evidenceType, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.storageReference, StringComparer.Ordinal)
                .ToArray(),
            actorUserId = command.ActorUserId,
            operatorDeviceBindingId = command.OperatorDeviceBindingId,
            operatorShiftId = command.OperatorShiftId,
            requesterAttestation = command.RequesterAttestation,
            attestationNotes = NormalizeOptional(command.AttestationNotes),
            reasonCode = NormalizeOptional(command.ReasonCode),
            decision = NormalizeOptional(command.Decision),
            decisionReasonCode = NormalizeOptional(command.DecisionReasonCode),
            reviewerUserId = command.ReviewerUserId,
            reviewerAttestation = command.ReviewerAttestation,
            applyPayableBasis = command.ApplyPayableBasis,
            originalTariffSnapshotId = command.OriginalTariffSnapshotId
        };

        var json = JsonSerializer.Serialize(source, HashJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

internal static class StatutoryDiscountDecisionMappings
{
    public static StatutoryDiscountDecisionResult ToResult(this StatutoryDiscountDecisionCommandRecord record) =>
        new(
            record.StatutoryDiscountDecisionCommandId,
            record.RequestReference,
            record.StatutoryDiscountValidationId,
            record.ParkingSessionId,
            record.SourceChannel,
            record.EntitlementType,
            record.DecisionStatus,
            record.PolicyResolutionBasis,
            record.AppliedPolicyReferenceId,
            record.FallbackPolicyReferenceId,
            record.LocalOrdinanceApplied,
            record.GrossAmountMinorUnits,
            record.StatutoryDiscountAmountMinorUnits,
            record.NetPayableAmountMinorUnits,
            record.Currency,
            record.EvidenceRequired,
            record.EvidenceRecorded,
            record.ReasonCode,
            record.ErrorCode,
            record.CorrelationId,
            record.CreatedAt,
            record.DecidedAt,
            record.AppliedAt,
            record.OriginalTariffSnapshotId,
            record.AppliedTariffSnapshotId,
            record.ResultClassification,
            record.SemanticHashSourceVersion);

    public static StatutoryDiscountDecisionCommandRecord Merge(
        this StatutoryDiscountDecisionCommandRecord record,
        StatutoryDiscountDecisionCommand command,
        OperatorConsoleStatutoryDiscountDraftResult draft,
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail,
        OperatorConsoleStatutoryDiscountApplyPayableBasisResult? apply,
        string decisionStatus,
        string? errorCode)
    {
        var policyId = apply?.StatutoryDiscountPolicyId ?? detail?.StatutoryDiscountPolicyId ?? draft.Policy?.StatutoryDiscountPolicyId;
        return record with
        {
            StatutoryDiscountValidationId = draft.DraftId,
            PayableBasisApplicationId = apply?.PayableBasisApplicationId,
            OriginalTariffSnapshotId = apply?.OriginalTariffSnapshotId ?? detail?.OriginalTariffSnapshotId ?? command.OriginalTariffSnapshotId,
            AppliedTariffSnapshotId = apply?.AppliedTariffSnapshotId ?? detail?.AppliedTariffSnapshotId,
            DecisionStatus = decisionStatus,
            ResultClassification = record.ResultClassification == "ACCEPTED" ? "ACCEPTED" : record.ResultClassification,
            PolicyResolutionBasis = apply?.PolicyResolutionBasis ?? detail?.PolicyResolutionBasis ?? draft.Policy?.PolicyResolutionBasis,
            AppliedPolicyReferenceId = policyId,
            FallbackPolicyReferenceId = null,
            LocalOrdinanceApplied = !string.IsNullOrWhiteSpace(apply?.OrdinanceReference ?? detail?.OrdinanceReference),
            GrossAmountMinorUnits = apply?.GrossAmountMinorUnits ?? detail?.OriginalAmountMinorUnits,
            StatutoryDiscountAmountMinorUnits = apply?.StatutoryDiscountAmountMinorUnits ?? detail?.StatutoryDiscountAmountMinorUnits,
            NetPayableAmountMinorUnits = apply?.FinalPayableAmountMinorUnits ?? detail?.FinalPayableAmountMinorUnits ?? detail?.PayableAmountMinorUnits,
            Currency = apply?.CurrencyCode ?? detail?.CurrencyCode,
            EvidenceRequired = detail?.EvidenceRequired ?? draft.EvidenceRequired,
            EvidenceRecorded = detail?.EvidenceRequiredSatisfied ?? false,
            ReasonCode = command.DecisionReasonCode ?? command.ReasonCode,
            ErrorCode = errorCode,
            DecidedAt = detail?.ValidatedAt,
            AppliedAt = apply?.ApplicationPersisted == true ? DateTimeOffset.UtcNow : null
        };
    }
}
