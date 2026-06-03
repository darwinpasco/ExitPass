namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for metadata-only statutory discount evidence capture.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceCaptureCommand(
    Guid DraftId,
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
/// Query for statutory discount evidence metadata.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceListQuery(
    Guid DraftId,
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid CorrelationId);

/// <summary>
/// Minimal draft context needed for evidence access checks.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceDraftContext(
    Guid DraftId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string ValidationStatus,
    bool EvidenceRequired,
    bool EvidenceCaptured);

/// <summary>
/// Persistence command for statutory discount evidence metadata capture.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidencePersistenceCommand(
    Guid DraftId,
    string EvidenceType,
    string CaptureMethod,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? StorageReference,
    string? ReferenceNumberMasked,
    Guid CapturedByUserId,
    Guid CorrelationId);

/// <summary>
/// Evidence capture result.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceCaptureResult(
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
/// Evidence metadata list result.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceListResult(
    Guid DraftId,
    bool EvidenceRequired,
    bool EvidenceRequiredSatisfied,
    IReadOnlyList<string> RequiredEvidenceTypes,
    int EvidenceCount,
    string? LatestEvidenceStatus,
    IReadOnlyList<OperatorConsoleStatutoryDiscountEvidenceMetadataResult> Items,
    Guid CorrelationId);

/// <summary>
/// One statutory discount evidence metadata row.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountEvidenceMetadataResult(
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
