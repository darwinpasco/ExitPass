namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public static class StatutoryEvidenceUploadConstants
{
    public const string ProviderTypeS3Compatible = "S3_COMPATIBLE";
    public const string UploadMethodPut = "PUT";
    public const string ChecksumAlgorithmSha256 = "SHA256";
    public const string UploadProfileUnavailable = "UPLOAD_PROFILE_UNAVAILABLE";
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";
    public const string ObjectMetadataMismatch = "OBJECT_METADATA_MISMATCH";
    public const string ContentTypeMismatch = "CONTENT_TYPE_MISMATCH";
    public const string ContentLengthMismatch = "CONTENT_LENGTH_MISMATCH";
    public const string ChecksumMismatch = "CHECKSUM_MISMATCH";
    public const string UnsupportedContentType = "UNSUPPORTED_CONTENT_TYPE";
    public const string ContentLengthExceeded = "CONTENT_LENGTH_EXCEEDED";

    public static readonly ISet<string> SupportedContentTypes = new HashSet<string>(
        ["image/jpeg", "image/png"],
        StringComparer.OrdinalIgnoreCase);
}

public sealed class StatutoryEvidenceUploadOptions
{
    public const string SectionName = "CentralPms:StatutoryEvidence:Upload";

    public string ProviderType { get; set; } = StatutoryEvidenceUploadConstants.ProviderTypeS3Compatible;
    public string? Endpoint { get; set; }
    public string? PublicUploadEndpoint { get; set; }
    public string? Region { get; set; } = "us-east-1";
    public string? BucketName { get; set; }
    public string? BucketReference { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string EnvironmentPartition { get; set; } = "local";
    public int AuthorizationTtlSeconds { get; set; } = 300;
    public long MaxContentLengthBytes { get; set; }
    public List<string> AllowedContentTypes { get; set; } = ["image/jpeg", "image/png"];
    public bool RequireSha256Checksum { get; set; } = true;
    public bool RequireTlsForNonLocal { get; set; } = true;
    public bool RequireServerSideEncryptionMetadata { get; set; }
}

public sealed record StatutoryEvidenceUploadAuthorizationCommand(
    Guid EvidenceSetReference,
    Guid EvidenceItemReference,
    string DeclaredContentType,
    long DeclaredContentLength,
    string MediaClass,
    string ChecksumAlgorithm,
    string DeclaredChecksumSha256,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceUploadFinalizationCommand(
    Guid EvidenceSetReference,
    Guid EvidenceItemReference,
    Guid UploadAuthorizationReference,
    string IdempotencyScope,
    string IdempotencyKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceUploadAuthorizationReadModel(
    Guid UploadAuthorizationReference,
    string UploadMethod,
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt,
    long MaxContentLengthBytes,
    string AcceptedContentType,
    Guid CorrelationId);

public sealed record StatutoryEvidenceUploadAuthorizationOutcome(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    StatutoryEvidenceUploadAuthorizationReadModel? UploadAuthorization,
    StatutoryEvidenceItemReadModel? EvidenceItem);

public sealed record StatutoryEvidenceUploadFinalizationOutcome(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    StatutoryEvidenceItemReadModel? EvidenceItem);

public sealed record StatutoryEvidenceUploadTarget(
    Guid EvidenceSetId,
    Guid EvidenceItemId,
    StatutoryEvidenceSetReadModel EvidenceSet,
    StatutoryEvidenceItemReadModel EvidenceItem);

public sealed record StatutoryEvidenceUploadAuthorizationStorageRecord(
    Guid UploadAuthorizationId,
    Guid UploadAuthorizationReference,
    Guid EvidenceSetId,
    Guid EvidenceItemId,
    Guid OperationId,
    string ProviderType,
    string BucketReference,
    string InternalObjectKey,
    string UploadMethod,
    string ExpectedContentType,
    long ExpectedContentLength,
    string ChecksumAlgorithm,
    string ExpectedChecksumSha256,
    string AuthorizationStatus,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    string? VerifiedContentType,
    long? VerifiedContentLength,
    string? VerifiedChecksumSha256,
    string? ProviderObjectVersion,
    string? ProviderEncryptionClassification,
    string? FailureClassification);

public sealed record StatutoryEvidenceObjectUploadAuthorizationRequest(
    string BucketName,
    string InternalObjectKey,
    string ContentType,
    long ContentLength,
    string ChecksumSha256,
    DateTimeOffset ExpiresAt);

public sealed record StatutoryEvidenceObjectUploadAuthorization(
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders);

public sealed record StatutoryEvidenceObjectMetadataRequest(
    string BucketName,
    string InternalObjectKey);

public sealed record StatutoryEvidenceObjectMetadata(
    string ContentType,
    long ContentLength,
    string? ChecksumSha256,
    string? ObjectVersion,
    string? EncryptionClassification);

public interface IStatutoryEvidenceProtectedObjectStorageAdapter
{
    Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(
        StatutoryEvidenceObjectUploadAuthorizationRequest request,
        CancellationToken cancellationToken);

    Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(
        StatutoryEvidenceObjectMetadataRequest request,
        CancellationToken cancellationToken);
}

public interface IStatutoryEvidenceUploadRepository
{
    Task<StatutoryEvidenceUploadTarget?> GetUploadTargetAsync(Guid evidenceSetReference, Guid evidenceItemReference, CancellationToken cancellationToken);
    Task<bool> ActorHasScopeAsync(StatutoryEvidenceActor actor, string operation, Guid siteId, Guid siteGroupId, CancellationToken cancellationToken);
    Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> FindUploadAuthorizationByOperationAsync(string idempotencyScope, string idempotencyKey, string semanticRequestHash, CancellationToken cancellationToken);
    Task<bool> HasSemanticConflictAsync(string idempotencyScope, string idempotencyKey, string semanticRequestHash, CancellationToken cancellationToken);
    Task<StatutoryEvidenceUploadAuthorizationStorageRecord> CreateUploadAuthorizationAsync(StatutoryEvidenceUploadAuthorizationCommand command, StatutoryEvidenceUploadTarget target, string semanticRequestHash, string providerType, string bucketReference, string internalObjectKey, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> GetUploadAuthorizationAsync(Guid authorizationReference, Guid evidenceSetId, Guid evidenceItemId, CancellationToken cancellationToken);
    Task<StatutoryEvidenceItemReadModel?> FinalizeUploadAsync(StatutoryEvidenceUploadFinalizationCommand command, StatutoryEvidenceUploadTarget target, StatutoryEvidenceUploadAuthorizationStorageRecord authorization, StatutoryEvidenceObjectMetadata metadata, string semanticRequestHash, CancellationToken cancellationToken);
    Task RecordUploadDeniedAsync(Guid? evidenceSetReference, Guid? evidenceItemReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken);
    Task RecordUploadConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken);
    Task RecordUploadVerificationFailureAsync(StatutoryEvidenceUploadFinalizationCommand command, StatutoryEvidenceUploadTarget target, StatutoryEvidenceUploadAuthorizationStorageRecord authorization, string reasonCode, CancellationToken cancellationToken);
}

public interface IStatutoryEvidenceUploadService
{
    Task<StatutoryEvidenceUploadAuthorizationOutcome> AuthorizeUploadAsync(StatutoryEvidenceUploadAuthorizationCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceUploadFinalizationOutcome> FinalizeUploadAsync(StatutoryEvidenceUploadFinalizationCommand command, CancellationToken cancellationToken);
}
