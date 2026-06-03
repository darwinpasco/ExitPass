namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Metadata-only evidence capture request for an Operator Console statutory discount draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceCaptureRequest(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumber,
    string? Notes,
    bool OperatorConfirmation,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Metadata-only evidence capture response for an Operator Console statutory discount draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceCaptureResponse(
    Guid EvidenceId,
    Guid DraftId,
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumberMasked,
    Guid? CapturedByUserId,
    DateTimeOffset CapturedAt,
    string RedactionStatus,
    string VerificationStatus,
    bool EvidenceRequiredSatisfied,
    string CurrentDraftStatus,
    bool AccessAllowed,
    string? ErrorCode,
    Guid CorrelationId);

/// <summary>
/// Evidence metadata list response for an Operator Console statutory discount draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceListResponse(
    Guid DraftId,
    bool EvidenceRequired,
    bool EvidenceRequiredSatisfied,
    IReadOnlyList<string> RequiredEvidenceTypes,
    int EvidenceCount,
    string? LatestEvidenceStatus,
    IReadOnlyList<OperatorConsoleStatutoryDiscountEvidenceItem> Items,
    Guid CorrelationId);

/// <summary>
/// Evidence metadata item for an Operator Console statutory discount draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceItem(
    Guid EvidenceId,
    Guid DraftId,
    string EvidenceType,
    string CaptureMethod,
    string? StorageReference,
    Guid? CapturedByUserId,
    DateTimeOffset CapturedAt,
    string RedactionStatus,
    string VerificationStatus,
    Guid? CorrelationId);
