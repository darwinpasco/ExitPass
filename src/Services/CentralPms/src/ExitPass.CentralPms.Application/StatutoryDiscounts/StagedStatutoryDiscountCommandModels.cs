using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Internal command-state values for staged statutory-discount decisions.
/// </summary>
public static class StatutoryDiscountDecisionV2CommandStates
{
    public const string Received = "RECEIVED";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string FailedRetryable = "FAILED_RETRYABLE";
    public const string FailedNonRetryable = "FAILED_NON_RETRYABLE";
}

/// <summary>
/// Internal decision result values for staged statutory-discount decisions.
/// </summary>
public static class StatutoryDiscountDecisionV2ResultStates
{
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string NotDecided = "NOT_DECIDED";
}

/// <summary>
/// Internal command-state values for staged payable-basis applications.
/// </summary>
public static class StatutoryDiscountPayableBasisApplicationV1CommandStates
{
    public const string Received = "RECEIVED";
    public const string Processing = "PROCESSING";
    public const string Applied = "APPLIED";
    public const string FailedRetryable = "FAILED_RETRYABLE";
    public const string FailedNonRetryable = "FAILED_NON_RETRYABLE";
}

/// <summary>
/// Internal result classifications for staged payable-basis applications.
/// </summary>
public static class StatutoryDiscountPayableBasisApplicationV1ResultClassifications
{
    public const string Applied = "APPLIED";
    public const string IdempotentReplay = "IDEMPOTENT_REPLAY";
    public const string SemanticConflict = "SEMANTIC_CONFLICT";
    public const string DecisionNotApproved = "DECISION_NOT_APPROVED";
    public const string DecisionNotFound = "DECISION_NOT_FOUND";
    public const string InProgress = "IN_PROGRESS";
    public const string RetryableFailure = "RETRYABLE_FAILURE";
    public const string NonRetryableFailure = "NON_RETRYABLE_FAILURE";
}

/// <summary>
/// Internal staged statutory-discount decision command. This model is not a public API DTO.
/// </summary>
public sealed record StatutoryDiscountDecisionV2Command(
    Guid RequestReference,
    string SourceChannel,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    StatutoryDiscountDecisionV2BeneficiaryMetadata? Beneficiary,
    StatutoryDiscountDecisionV2IdentityMetadata? IdentityMetadata,
    IReadOnlyList<StatutoryDiscountDecisionV2EvidenceReference> EvidenceReferences,
    StatutoryDiscountDecisionV2AttestationFacts? Attestation,
    Guid ActorUserId,
    Guid? ReviewerUserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    StatutoryDiscountDecisionV2DecisionFacts Decision,
    Guid? PolicyResolutionReferenceId,
    Guid? AppliedPolicyReferenceId,
    Guid? FallbackPolicyReferenceId,
    string? PolicyResolutionBasis,
    bool LocalOrdinanceApplied,
    Guid? OriginalTariffSnapshotId,
    StatutoryDiscountDecisionV2TariffFacts? OriginalTariffFacts,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record StatutoryDiscountDecisionV2BeneficiaryMetadata(
    string? BeneficiaryReference,
    string? BeneficiaryType,
    string? ClaimantRole,
    int? BeneficiaryCount);

public sealed record StatutoryDiscountDecisionV2IdentityMetadata(
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? MaskedIdReference,
    string? IdentityReferenceHash);

public sealed record StatutoryDiscountDecisionV2EvidenceReference(
    string EvidenceType,
    string CaptureMethod,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus,
    string? VerificationReference,
    DateTimeOffset? VerifiedAt);

public sealed record StatutoryDiscountDecisionV2AttestationFacts(
    bool RequesterAttested,
    string? AttestationReference,
    string? AttestationReasonCode,
    bool ReviewerAttested);

public sealed record StatutoryDiscountDecisionV2DecisionFacts(
    string Decision,
    string? DecisionReasonCode,
    string? SafeErrorCode);

public sealed record StatutoryDiscountDecisionV2TariffFacts(
    long? GrossAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetPayableAmountMinorUnits,
    string? Currency);

public sealed record StatutoryDiscountDecisionV2RepositoryCommand(
    StatutoryDiscountDecisionV2Command Command,
    string BusinessIdentity,
    string IdempotencyScope,
    string SemanticRequestHash,
    string SemanticHashSourceVersion,
    DateTimeOffset RequestedAt);

public sealed record StatutoryDiscountDecisionV2BeginResult(
    bool Existing,
    bool SemanticConflict,
    bool RecoverableWithOriginalKey,
    StatutoryDiscountDecisionV2Record Record);

public sealed record StatutoryDiscountDecisionV2Record(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid ParkingSessionId,
    string SourceChannel,
    string EntitlementType,
    string BusinessIdentity,
    string IdempotencyScope,
    string IdempotencyKey,
    string SemanticHashSourceVersion,
    string SemanticRequestHash,
    string CommandStatus,
    string DecisionResultStatus,
    string ResultClassification,
    bool Retryable,
    string RecoveryClassification,
    string? SafeErrorCode,
    Guid? StatutoryDiscountValidationId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    Guid? FallbackPolicyReferenceId,
    string? PolicyResolutionBasis,
    bool LocalOrdinanceApplied,
    long? GrossAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetPayableAmountMinorUnits,
    string? Currency,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? ReasonCode,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset UpdatedAt);

public sealed record StatutoryDiscountPayableBasisApplicationV1Command(
    Guid RequestReference,
    Guid StatutoryDiscountDecisionCommandId,
    Guid ParkingSessionId,
    Guid? SiteId,
    string EntitlementType,
    Guid? StatutoryDiscountValidationId,
    Guid? OriginalTariffSnapshotId,
    Guid? TargetTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    string? PolicyResolutionBasis,
    long ApprovedDiscountAmountMinorUnits,
    long? ApprovedVatExclusiveAmountMinorUnits,
    long? ApprovedVatAmountMinorUnits,
    long ApprovedFinalPayableAmountMinorUnits,
    string Currency,
    string SourceChannel,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record StatutoryDiscountPayableBasisApplicationV1RepositoryCommand(
    StatutoryDiscountPayableBasisApplicationV1Command Command,
    string BusinessIdentity,
    string IdempotencyScope,
    string SemanticRequestHash,
    string SemanticHashSourceVersion,
    DateTimeOffset RequestedAt);

public sealed record StatutoryDiscountPayableBasisApplicationV1BeginResult(
    bool Existing,
    bool SemanticConflict,
    bool RecoverableWithOriginalKey,
    StatutoryDiscountPayableBasisApplicationV1Record Record);

public sealed record StatutoryDiscountPayableBasisApplicationV1Record(
    Guid StatutoryDiscountPayableBasisApplicationCommandId,
    Guid RequestReference,
    Guid StatutoryDiscountDecisionCommandId,
    Guid ParkingSessionId,
    string EntitlementType,
    string BusinessIdentity,
    string IdempotencyScope,
    string IdempotencyKey,
    string SemanticHashSourceVersion,
    string SemanticRequestHash,
    string CommandStatus,
    string ResultClassification,
    bool Retryable,
    string RecoveryClassification,
    string? SafeErrorCode,
    Guid? StatutoryDiscountValidationId,
    Guid? StatutoryDiscountPayableBasisApplicationId,
    Guid? OriginalTariffSnapshotId,
    Guid? TargetTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    string? PolicyResolutionBasis,
    long ApprovedDiscountAmountMinorUnits,
    long? ApprovedVatExclusiveAmountMinorUnits,
    long? ApprovedVatAmountMinorUnits,
    long ApprovedFinalPayableAmountMinorUnits,
    string Currency,
    string SourceChannel,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset UpdatedAt);

public sealed record StagedStatutoryDiscountCommandStartResult<TRecord>(
    string ResultClassification,
    bool Existing,
    bool SemanticConflict,
    bool Retryable,
    string RecoveryClassification,
    TRecord? Record,
    string? SafeErrorCode);

/// <summary>
/// Deterministic staged decision-v2 semantic hash helper.
/// </summary>
public static class StatutoryDiscountDecisionV2SemanticHash
{
    public const string SourceVersion = "statutory-discount-decision:sha256:v2";

    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildBusinessIdentity(StatutoryDiscountDecisionV2Command command) =>
        $"statutory-discount-decision:{command.ParkingSessionId:N}:{Normalize(command.EntitlementType)}";

    public static string BuildIdempotencyScope(StatutoryDiscountDecisionV2Command command) =>
        BuildBusinessIdentity(command);

    public static string Compute(StatutoryDiscountDecisionV2Command command)
    {
        EnsureSafeIdentity(command.IdentityMetadata);
        EnsureSafeEvidence(command.EvidenceReferences);

        var source = new
        {
            version = SourceVersion,
            parkingSessionId = command.ParkingSessionId,
            siteId = command.SiteId,
            siteGroupId = command.SiteGroupId,
            ticketReference = NormalizeOptional(command.TicketReference),
            plateNumber = NormalizeOptional(command.PlateNumber),
            entitlementType = Normalize(command.EntitlementType),
            beneficiary = command.Beneficiary is null
                ? null
                : new
                {
                    beneficiaryReference = NormalizeOptional(command.Beneficiary.BeneficiaryReference),
                    beneficiaryType = NormalizeOptional(command.Beneficiary.BeneficiaryType),
                    claimantRole = NormalizeOptional(command.Beneficiary.ClaimantRole),
                    beneficiaryCount = command.Beneficiary.BeneficiaryCount
                },
            identity = command.IdentityMetadata is null
                ? null
                : new
                {
                    idDocumentType = NormalizeOptional(command.IdentityMetadata.IdDocumentType),
                    issuingAuthority = NormalizeOptional(command.IdentityMetadata.IssuingAuthority),
                    expiryDate = command.IdentityMetadata.ExpiryDate,
                    maskedIdReference = NormalizeOptional(command.IdentityMetadata.MaskedIdReference),
                    identityReferenceHash = NormalizeOptional(command.IdentityMetadata.IdentityReferenceHash)
                },
            evidenceReferences = (command.EvidenceReferences ?? [])
                .Select(evidence => new
                {
                    evidenceType = Normalize(evidence.EvidenceType),
                    captureMethod = Normalize(evidence.CaptureMethod),
                    storageReference = NormalizeOptional(evidence.StorageReference),
                    referenceNumberMasked = NormalizeOptional(evidence.ReferenceNumberMasked),
                    verificationStatus = NormalizeOptional(evidence.VerificationStatus),
                    verificationReference = NormalizeOptional(evidence.VerificationReference),
                    verifiedAt = evidence.VerifiedAt?.ToUniversalTime()
                })
                .OrderBy(evidence => evidence.evidenceType, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.storageReference, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.verificationReference, StringComparer.Ordinal)
                .ToArray(),
            attestation = command.Attestation is null
                ? null
                : new
                {
                    requesterAttested = command.Attestation.RequesterAttested,
                    attestationReference = NormalizeOptional(command.Attestation.AttestationReference),
                    attestationReasonCode = NormalizeOptional(command.Attestation.AttestationReasonCode),
                    reviewerAttested = command.Attestation.ReviewerAttested
                },
            actorUserId = command.ActorUserId,
            reviewerUserId = command.ReviewerUserId,
            operatorDeviceBindingId = command.OperatorDeviceBindingId,
            operatorShiftId = command.OperatorShiftId,
            decision = Normalize(command.Decision.Decision),
            decisionReasonCode = NormalizeOptional(command.Decision.DecisionReasonCode),
            safeErrorCode = NormalizeOptional(command.Decision.SafeErrorCode),
            policyResolutionReferenceId = command.PolicyResolutionReferenceId,
            appliedPolicyReferenceId = command.AppliedPolicyReferenceId,
            fallbackPolicyReferenceId = command.FallbackPolicyReferenceId,
            policyResolutionBasis = NormalizeOptional(command.PolicyResolutionBasis),
            localOrdinanceApplied = command.LocalOrdinanceApplied,
            originalTariffSnapshotId = command.OriginalTariffSnapshotId,
            originalTariffFacts = command.OriginalTariffFacts is null
                ? null
                : new
                {
                    grossAmountMinorUnits = command.OriginalTariffFacts.GrossAmountMinorUnits,
                    vatExclusiveAmountMinorUnits = command.OriginalTariffFacts.VatExclusiveAmountMinorUnits,
                    vatAmountMinorUnits = command.OriginalTariffFacts.VatAmountMinorUnits,
                    statutoryDiscountAmountMinorUnits = command.OriginalTariffFacts.StatutoryDiscountAmountMinorUnits,
                    netPayableAmountMinorUnits = command.OriginalTariffFacts.NetPayableAmountMinorUnits,
                    currency = NormalizeOptional(command.OriginalTariffFacts.Currency)
                }
        };

        return ComputeHash(source);
    }

    internal static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    internal static string ComputeHash<T>(T source)
    {
        var json = JsonSerializer.Serialize(source, HashJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void EnsureSafeIdentity(StatutoryDiscountDecisionV2IdentityMetadata? identity)
    {
        if (identity is null)
        {
            return;
        }

        EnsureMaskedOrHashed(identity.MaskedIdReference, nameof(identity.MaskedIdReference));
    }

    private static void EnsureSafeEvidence(IEnumerable<StatutoryDiscountDecisionV2EvidenceReference>? evidenceReferences)
    {
        foreach (var evidence in evidenceReferences ?? [])
        {
            EnsureMaskedOrHashed(evidence.ReferenceNumberMasked, nameof(evidence.ReferenceNumberMasked));
            if (LooksLikeRawPayload(evidence.StorageReference))
            {
                throw new ArgumentException("Raw evidence payloads are not permitted in staged statutory-discount commands.");
            }
        }
    }

    private static void EnsureMaskedOrHashed(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        var containsMask = normalized.Contains('*', StringComparison.Ordinal);
        var hasHashPrefix = normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase);
        var digitCount = normalized.Count(char.IsDigit);
        if (!containsMask && !hasHashPrefix && digitCount >= 6)
        {
            throw new ArgumentException($"{fieldName} must be masked or hashed.");
        }

        if (LooksLikeRawPayload(normalized))
        {
            throw new ArgumentException($"{fieldName} must not contain raw evidence payload data.");
        }
    }

    private static bool LooksLikeRawPayload(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/9j/", StringComparison.Ordinal)
            || value.StartsWith("iVBOR", StringComparison.Ordinal)
            || value.Length > 2048);
}

/// <summary>
/// Deterministic staged payable-basis-application-v1 semantic hash helper.
/// </summary>
public static class StatutoryDiscountPayableBasisApplicationV1SemanticHash
{
    public const string SourceVersion = "statutory-discount-payable-basis-application:sha256:v1";

    public static string BuildBusinessIdentity(StatutoryDiscountPayableBasisApplicationV1Command command) =>
        $"statutory-discount-payable-basis-application:{command.StatutoryDiscountDecisionCommandId:N}";

    public static string BuildIdempotencyScope(StatutoryDiscountPayableBasisApplicationV1Command command) =>
        BuildBusinessIdentity(command);

    public static string Compute(StatutoryDiscountPayableBasisApplicationV1Command command)
    {
        var source = new
        {
            version = SourceVersion,
            statutoryDiscountDecisionCommandId = command.StatutoryDiscountDecisionCommandId,
            parkingSessionId = command.ParkingSessionId,
            siteId = command.SiteId,
            entitlementType = StatutoryDiscountDecisionV2SemanticHash.Normalize(command.EntitlementType),
            statutoryDiscountValidationId = command.StatutoryDiscountValidationId,
            originalTariffSnapshotId = command.OriginalTariffSnapshotId,
            targetTariffSnapshotId = command.TargetTariffSnapshotId,
            appliedTariffSnapshotId = command.AppliedTariffSnapshotId,
            appliedPolicyReferenceId = command.AppliedPolicyReferenceId,
            policyResolutionBasis = StatutoryDiscountDecisionV2SemanticHash.NormalizeOptional(command.PolicyResolutionBasis),
            approvedDiscountAmountMinorUnits = command.ApprovedDiscountAmountMinorUnits,
            approvedVatExclusiveAmountMinorUnits = command.ApprovedVatExclusiveAmountMinorUnits,
            approvedVatAmountMinorUnits = command.ApprovedVatAmountMinorUnits,
            approvedFinalPayableAmountMinorUnits = command.ApprovedFinalPayableAmountMinorUnits,
            currency = StatutoryDiscountDecisionV2SemanticHash.NormalizeOptional(command.Currency)
        };

        return StatutoryDiscountDecisionV2SemanticHash.ComputeHash(source);
    }
}
