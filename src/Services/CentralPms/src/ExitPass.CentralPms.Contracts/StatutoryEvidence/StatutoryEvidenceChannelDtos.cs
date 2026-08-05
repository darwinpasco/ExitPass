namespace ExitPass.CentralPms.Contracts.StatutoryEvidence;

public sealed record StatutoryEvidenceChannelBootstrapRequest(
    Guid StatutoryDiscountDecisionCommandId,
    string? ClientOperationKey);

public sealed record StatutoryEvidenceChannelUploadSessionRequest(
    Guid EvidenceSetReference,
    Guid EvidenceItemReference,
    string DeclaredContentType,
    long DeclaredContentLength,
    string DeclaredChecksumSha256,
    string? ClientOperationKey);

public sealed record StatutoryEvidenceChannelFinalizeRequest(
    string? ClientOperationKey);

public sealed record StatutoryEvidenceChannelResponseDto(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    string SourceChannel,
    bool EvidenceRequired,
    Guid? EvidenceSetReference,
    Guid? EvidenceItemReference,
    IReadOnlyList<string> AllowedContentTypes,
    long MaximumContentLengthBytes,
    int? MaximumImageWidth,
    int? MaximumImageHeight,
    long? MaximumImagePixelCount,
    string? RequiredDocumentType,
    string? RequiredItemRole,
    string? LifecycleClassification,
    string ReplacementPosture,
    bool ReadyForReview,
    bool ReadyForAptPreCash,
    string? BlockingReasonCode,
    DateTimeOffset EvaluatedAt);

public sealed record StatutoryEvidenceOpaqueUploadSessionResponseDto(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    Guid? OpaqueUploadSessionReference,
    string Method,
    DateTimeOffset? ExpiresAt,
    string AcceptedContentType,
    long MaximumContentLengthBytes);
