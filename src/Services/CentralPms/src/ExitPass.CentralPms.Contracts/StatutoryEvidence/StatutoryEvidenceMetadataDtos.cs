namespace ExitPass.CentralPms.Contracts.StatutoryEvidence;

public sealed record StatutoryEvidenceCreateSetRequest(
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
    string SourceChannel);

public sealed record StatutoryEvidenceAddItemRequest(
    string DocumentType,
    string ItemRole,
    string ExpectedMediaClass,
    string? DeclaredContentType,
    string ProfileCode,
    string IdempotencyScope,
    string IdempotencyKey,
    string SourceChannel);

public sealed record StatutoryEvidenceHoldRequest(
    string ReasonCode,
    string IdempotencyScope,
    string IdempotencyKey,
    string SourceChannel);

public sealed record StatutoryEvidenceTransitionRequest(
    string IdempotencyScope,
    string IdempotencyKey,
    string SourceChannel);

public sealed record StatutoryEvidenceUploadAuthorizationRequest(
    string DeclaredContentType,
    long DeclaredContentLength,
    string MediaClass,
    string ChecksumAlgorithm,
    string DeclaredChecksumSha256,
    string IdempotencyScope,
    string IdempotencyKey,
    string SourceChannel);

public sealed record StatutoryEvidenceUploadFinalizationRequest(
    Guid UploadAuthorizationReference,
    string IdempotencyScope,
    string IdempotencyKey,
    string SourceChannel);

public sealed record StatutoryEvidenceOperationResponse(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    StatutoryEvidenceSetResponse? EvidenceSet,
    StatutoryEvidenceItemResponse? EvidenceItem);

public sealed record StatutoryEvidenceUploadAuthorizationResponse(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    StatutoryEvidenceUploadAuthorizationDetailsResponse? UploadAuthorization,
    StatutoryEvidenceItemResponse? EvidenceItem);

public sealed record StatutoryEvidenceUploadFinalizationResponse(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    StatutoryEvidenceItemResponse? EvidenceItem);

public sealed record StatutoryEvidenceUploadAuthorizationDetailsResponse(
    Guid UploadAuthorizationReference,
    Uri UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt,
    long MaximumContentLength,
    string AcceptedContentType);

public sealed record StatutoryEvidenceSetResponse(
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
    IReadOnlyList<StatutoryEvidenceItemResponse> Items);

public sealed record StatutoryEvidenceItemResponse(
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
