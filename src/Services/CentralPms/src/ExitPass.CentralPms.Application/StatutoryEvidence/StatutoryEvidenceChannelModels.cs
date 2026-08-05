namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public static class StatutoryEvidenceChannelConstants
{
    public const string WebPay = "WEBPAY";
    public const string AssistedPaymentTerminal = "ASSISTED_PAYMENT_TERMINAL";
    public const string BootstrapOperation = "BOOTSTRAP";
    public const string UploadSessionOperation = "OPAQUE_UPLOAD_SESSION";
    public const string UploadRelayOperation = "OPAQUE_UPLOAD_RELAY";
    public const string FinalizeUploadSessionOperation = "OPAQUE_UPLOAD_FINALIZE";

    public static readonly ISet<string> ReadyEvidenceStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NOT_REQUIRED",
        "APPLIED"
    };
}

public sealed class StatutoryEvidenceChannelOptions
{
    public const string SectionName = "CentralPms:StatutoryEvidence:Channel";

    public string EnvironmentScope { get; set; } = "LOCAL_TEST";
    public string SeniorCitizenDocumentProfileCode { get; set; } = "SENIOR_CITIZEN_ID_FRONT_BACK_V1";
    public string PwdDocumentProfileCode { get; set; } = "PWD_ID_FRONT_BACK_V1";
    public string RequiredDocumentProfileVersion { get; set; } = "1";
    public string SingleDocumentItemRole { get; set; } = "SINGLE_DOCUMENT";
    public string ExpectedJpegMediaClass { get; set; } = "IMAGE_JPEG";
}

public sealed record StatutoryEvidenceChannelBootstrapCommand(
    string SourceChannel,
    Guid StatutoryDiscountDecisionCommandId,
    string? ClientOperationKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceChannelStatusQuery(
    string SourceChannel,
    Guid? StatutoryDiscountDecisionCommandId,
    Guid? EvidenceSetReference,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceChannelUploadSessionCommand(
    string SourceChannel,
    Guid EvidenceSetReference,
    Guid EvidenceItemReference,
    string DeclaredContentType,
    long DeclaredContentLength,
    string DeclaredChecksumSha256,
    string? ClientOperationKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceChannelUploadCommand(
    string SourceChannel,
    Guid OpaqueUploadSessionReference,
    string? ContentType,
    long? ContentLength,
    Stream Content,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceChannelFinalizeCommand(
    string SourceChannel,
    Guid OpaqueUploadSessionReference,
    string? ClientOperationKey,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);

public sealed record StatutoryEvidenceChannelReadiness(
    string Classification,
    bool EvidenceRequired,
    bool Ready,
    bool Retryable,
    string? BlockingReasonCode,
    string Message);

public sealed record StatutoryEvidenceChannelResponse(
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

public sealed record StatutoryEvidenceOpaqueUploadSessionResponse(
    string Classification,
    bool Retryable,
    string? ErrorCode,
    Guid CorrelationId,
    Guid? OpaqueUploadSessionReference,
    string Method,
    DateTimeOffset? ExpiresAt,
    string AcceptedContentType,
    long MaximumContentLengthBytes);

public interface IStatutoryEvidenceChannelService
{
    Task<StatutoryEvidenceChannelResponse> BootstrapAsync(StatutoryEvidenceChannelBootstrapCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceChannelResponse> GetStatusAsync(StatutoryEvidenceChannelStatusQuery query, CancellationToken cancellationToken);
    Task<StatutoryEvidenceChannelReadiness> GetAptEvidenceReadinessAsync(Guid? statutoryDiscountDecisionCommandId, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOpaqueUploadSessionResponse> CreateUploadSessionAsync(StatutoryEvidenceChannelUploadSessionCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceOpaqueUploadSessionResponse> UploadAsync(StatutoryEvidenceChannelUploadCommand command, CancellationToken cancellationToken);
    Task<StatutoryEvidenceChannelResponse> FinalizeUploadSessionAsync(StatutoryEvidenceChannelFinalizeCommand command, CancellationToken cancellationToken);
}
