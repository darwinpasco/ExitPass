namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

public sealed class WebPayStatutoryEvidenceBootstrapRequest
{
    public Guid StatutoryDiscountDecisionCommandId { get; set; }
    public string? ClientOperationKey { get; set; }
}

public sealed class WebPayStatutoryEvidenceUploadSessionRequest
{
    public Guid EvidenceSetReference { get; set; }
    public Guid EvidenceItemReference { get; set; }
    public string? DeclaredContentType { get; set; }
    public long DeclaredContentLength { get; set; }
    public string? DeclaredChecksumSha256 { get; set; }
    public string? ClientOperationKey { get; set; }
}

public sealed class WebPayStatutoryEvidenceFinalizeRequest
{
    public string? ClientOperationKey { get; set; }
}

public sealed class WebPayStatutoryEvidenceChannelResponse
{
    public string Classification { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public string? ErrorCode { get; set; }
    public Guid CorrelationId { get; set; }
    public bool EvidenceRequired { get; set; }
    public Guid? EvidenceSetReference { get; set; }
    public Guid? EvidenceItemReference { get; set; }
    public IReadOnlyList<string> AllowedContentTypes { get; set; } = Array.Empty<string>();
    public long MaximumContentLengthBytes { get; set; }
    public int? MaximumImageWidth { get; set; }
    public int? MaximumImageHeight { get; set; }
    public long? MaximumImagePixelCount { get; set; }
    public string? RequiredDocumentType { get; set; }
    public string? RequiredItemRole { get; set; }
    public string? LifecycleClassification { get; set; }
    public string ReplacementPosture { get; set; } = string.Empty;
    public bool ReadyForReview { get; set; }
    public string? BlockingReasonCode { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class WebPayStatutoryEvidenceUploadSessionResponse
{
    public string Classification { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public string? ErrorCode { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? OpaqueUploadSessionReference { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string AcceptedContentType { get; set; } = string.Empty;
    public long MaximumContentLengthBytes { get; set; }
}
