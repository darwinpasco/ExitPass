using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public static class StatutoryEvidenceMetadataConstants
{
    public const string SemanticHashSourceVersion = "statutory-evidence-metadata:sha256:v1";
    public static readonly StringComparer CodeComparer = StringComparer.OrdinalIgnoreCase;

    public static readonly ISet<string> DocumentTypes = new HashSet<string>(CodeComparer)
    {
        "SENIOR_CITIZEN_ID",
        "PWD_ID",
        "AUTHORIZATION_LETTER",
        "SUPPORTING_DOCUMENT"
    };

    public static readonly ISet<string> ItemRoles = new HashSet<string>(CodeComparer)
    {
        "FRONT",
        "BACK",
        "SUPPLEMENTAL",
        "SINGLE_DOCUMENT"
    };

    public static readonly ISet<string> SourceChannels = new HashSet<string>(CodeComparer)
    {
        "WEBPAY",
        "ASSISTED_PAYMENT_TERMINAL",
        "OPERATOR_CONSOLE",
        "CENTRAL_PMS"
    };
}

public sealed record StatutoryEvidenceActor(Guid? UserId, Guid? ServiceIdentityId, string SourceChannel);

public static class StatutoryEvidenceScopeOperations
{
    public const string Capture = "CAPTURE";
    public const string View = "VIEW";
    public const string ReviewLock = "REVIEW_LOCK";
    public const string Hold = "HOLD";
    public const string DeletionRequest = "DELETE_REQUEST";
}

public sealed record StatutoryEvidenceDurableRequestBinding(
    Guid StatutoryDiscountDecisionCommandId,
    Guid? StatutoryDiscountValidationId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string SourceChannel);

public sealed record StatutoryEvidenceCreateSetCommand(
    Guid StatutoryDiscountDecisionCommandId,
    Guid? StatutoryDiscountValidationId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string RequiredDocumentProfileCode,
    string RequiredDocumentProfileVersion,
    string RetentionClassCode,
    string RetentionPolicyVersion,
    string EnvironmentScope,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceAddItemCommand(
    Guid EvidenceSetReference,
    string DocumentType,
    string ItemRole,
    string ExpectedMediaClass,
    string? DeclaredContentType,
    string ProfileCode,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceLockForReviewCommand(
    Guid EvidenceSetReference,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceHoldCommand(
    Guid EvidenceSetReference,
    string ReasonCode,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceReleaseHoldCommand(
    Guid EvidenceSetReference,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceDeletionRequestCommand(
    Guid EvidenceSetReference,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceOperationOutcome(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    StatutoryEvidenceSetReadModel? EvidenceSet,
    StatutoryEvidenceItemReadModel? EvidenceItem);

public sealed record StatutoryEvidenceSetReadModel(
    Guid EvidenceSetReference,
    Guid StatutoryDiscountDecisionCommandId,
    Guid? StatutoryDiscountValidationId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string SourceChannel,
    string SetStatus,
    string RequiredDocumentProfileCode,
    string RequiredDocumentProfileVersion,
    string RetentionClassCode,
    string RetentionPolicyVersion,
    string RetentionStatus,
    string DeletionStatus,
    bool HoldActive,
    string? HoldReasonCode,
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StatutoryEvidenceItemReadModel> Items);

public sealed record StatutoryEvidenceItemReadModel(
    Guid EvidenceItemReference,
    string DocumentType,
    string ItemRole,
    string UploadStatus,
    string ValidationStatus,
    string ScanStatus,
    string ReviewabilityStatus,
    string BindingStatus,
    string RetentionStatus,
    string DeletionStatus,
    bool HoldActive,
    string ExpectedMediaClass,
    string? DeclaredContentType,
    string ProfileCode,
    string? ValidationResultClassification,
    string? ScanResultClassification,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StatutoryEvidenceOperationReplay(
    string OperationStatus,
    string SemanticRequestHash,
    Guid? EvidenceSetId,
    Guid? EvidenceItemId);

public sealed record StatutoryEvidenceCreatedSet(Guid EvidenceSetId, StatutoryEvidenceSetReadModel ReadModel);

public sealed record StatutoryEvidenceCreatedItem(
    Guid EvidenceSetId,
    Guid EvidenceItemId,
    StatutoryEvidenceSetReadModel SetReadModel,
    StatutoryEvidenceItemReadModel ItemReadModel);

public interface IStatutoryEvidenceMetadataRepository
{
    Task<bool> ApprovedRetentionPolicyExistsAsync(string retentionClassCode, string retentionPolicyVersion, string environmentScope, CancellationToken cancellationToken);
    Task<StatutoryEvidenceDurableRequestBinding?> ResolveRequestBindingAsync(Guid statutoryDiscountDecisionCommandId, CancellationToken cancellationToken);
    Task<bool> ActorHasScopeAsync(StatutoryEvidenceActor actor, string operation, Guid siteId, Guid siteGroupId, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationReplay?> FindOperationAsync(string idempotencyScope, string idempotencyKey, CancellationToken cancellationToken);
    Task<StatutoryEvidenceCreatedSet> CreateEvidenceSetAsync(StatutoryEvidenceCreateSetCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceCreatedItem?> AddEvidenceItemAsync(StatutoryEvidenceAddItemCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetByIdAsync(Guid evidenceSetId, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, string semanticRequestHash, CancellationToken cancellationToken);
    Task RecordSemanticConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken);
    Task RecordAccessDeniedAsync(Guid? evidenceSetReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken);
}

public interface IStatutoryEvidenceMetadataService
{
    Task<StatutoryEvidenceOperationOutcome> CreateOrResolveSetAsync(StatutoryEvidenceCreateSetCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationOutcome> AddItemAsync(StatutoryEvidenceAddItemCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationOutcome> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationOutcome> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationOutcome> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOperationOutcome> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken);
}

public static class StatutoryEvidenceSemanticHash
{
    public static string For<T>(T command)
    {
        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
