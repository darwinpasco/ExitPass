using System.Security.Cryptography;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

public sealed class OperatorConsoleStatutoryIdPhotoOptions
{
    public const string SectionName = "OperatorConsoleStatutoryIdPhoto";

    public int ReceiptLifetimeMinutes { get; set; } = 15;
}

public sealed record OperatorConsoleStatutoryIdPhotoUploadCommand(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid ParkingSessionId,
    string EntitlementType,
    string ContentType,
    long? ContentLength,
    Guid CorrelationId);

public sealed record OperatorConsoleStatutoryIdPhotoUploadOutcome(
    bool Accepted,
    string? UploadReceipt,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    string Message,
    bool Retryable,
    Guid CorrelationId);

public sealed record OperatorConsoleStatutoryIdPhotoReceipt(
    Guid UserId,
    Guid SiteId,
    Guid ParkingSessionId,
    string EntitlementType,
    string StorageReference,
    string ChecksumSha256,
    string ContentType,
    long ContentLength);

public interface IOperatorConsoleStatutoryIdPhotoService
{
    Task<OperatorConsoleStatutoryIdPhotoUploadOutcome> UploadAsync(
        OperatorConsoleStatutoryIdPhotoUploadCommand command,
        Stream content,
        CancellationToken cancellationToken);

    OperatorConsoleStatutoryIdPhotoReceipt? ResolveReceipt(
        string? receipt,
        Guid expectedUserId,
        Guid expectedSiteId,
        Guid expectedParkingSessionId,
        string expectedEntitlementType);
}

/// <summary>
/// Uploads one Operator Console statutory ID photo to the existing restricted evidence store
/// and returns a short-lived protected receipt. The receipt is bound to the operator, Site,
/// parking session, and entitlement and contains no customer PII or image bytes.
/// </summary>
public sealed class OperatorConsoleStatutoryIdPhotoService : IOperatorConsoleStatutoryIdPhotoService
{
    public const string ProtectorPurpose = "ExitPass.OperatorConsole.StatutoryIdPhotoReceipt.v1";
    private const int PayloadVersion = 1;
    private const long DefaultMaximumContentLength = 5 * 1024 * 1024;

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IOperatorConsoleSessionLookupReadRepository _sessions;
    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluation;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessWriter;
    private readonly IStatutoryEvidenceProtectedObjectStorageAdapter _storage;
    private readonly StatutoryEvidenceUploadOptions _uploadOptions;
    private readonly OperatorConsoleStatutoryIdPhotoOptions _options;
    private readonly TimeProvider _timeProvider;

    public OperatorConsoleStatutoryIdPhotoService(
        IDataProtectionProvider dataProtectionProvider,
        IOperatorConsoleSessionLookupReadRepository sessions,
        IOperatorConsoleAccessEvaluationService accessEvaluation,
        IOperatorConsoleAccessEvaluationWriter accessWriter,
        IStatutoryEvidenceProtectedObjectStorageAdapter storage,
        IOptions<StatutoryEvidenceUploadOptions> uploadOptions,
        IOptions<OperatorConsoleStatutoryIdPhotoOptions> options,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose).ToTimeLimitedDataProtector();
        _sessions = sessions;
        _accessEvaluation = accessEvaluation;
        _accessWriter = accessWriter;
        _storage = storage;
        _uploadOptions = uploadOptions.Value;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<OperatorConsoleStatutoryIdPhotoUploadOutcome> UploadAsync(
        OperatorConsoleStatutoryIdPhotoUploadCommand command,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(content);

        var entitlementType = NormalizeEntitlement(command.EntitlementType);
        var contentType = NormalizeContentType(command.ContentType);
        var maximumLength = _uploadOptions.MaxContentLengthBytes > 0
            ? _uploadOptions.MaxContentLengthBytes
            : DefaultMaximumContentLength;

        if (command.UserId == Guid.Empty || command.ParkingSessionId == Guid.Empty ||
            command.CorrelationId == Guid.Empty || !command.SiteId.HasValue || command.SiteId == Guid.Empty)
        {
            return Rejected(command.CorrelationId, "ACCESS_DENIED", "The statutory ID photo upload is not authorized.");
        }

        if (entitlementType is not ("SENIOR_CITIZEN" or "PWD"))
        {
            return Rejected(command.CorrelationId, "INVALID_ENTITLEMENT_TYPE", "A Senior Citizen or PWD entitlement is required.");
        }

        if (!StatutoryEvidenceUploadConstants.SupportedContentTypes.Contains(contentType) ||
            command.ContentLength is null or <= 0 || command.ContentLength > maximumLength)
        {
            return Rejected(command.CorrelationId, "STATUTORY_ID_PHOTO_INVALID", "Use a JPEG or PNG ID photo within the configured size limit.");
        }

        var access = await _accessEvaluation.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId,
                command.SiteGroupId,
                command.OperatorShiftId,
                OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
                OperatorConsoleActionCodes.CreateStatutoryDiscountDraft,
                command.ParkingSessionId,
                "CAPTURE_REQUIRED_ID_PHOTO",
                $"operator-console-id-photo-{command.ParkingSessionId:N}-{command.CorrelationId:N}",
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);
        var persistedAccess = await _accessWriter.PersistAsync(access, cancellationToken).ConfigureAwait(false);
        if (!persistedAccess.Allowed ||
            !persistedAccess.SiteContext.Assigned ||
            persistedAccess.SiteContext.SiteId != command.SiteId)
        {
            return Rejected(command.CorrelationId, "ACCESS_DENIED", "The statutory ID photo upload is not authorized.");
        }

        var session = await _sessions.FindAsync(
            new OperatorConsoleSessionLookupReadRequest(
                command.ParkingSessionId,
                TicketReference: null,
                PlateNumber: null,
                command.SiteId,
                command.SiteGroupId,
                "PARKING_SESSION_ID"),
            cancellationToken).ConfigureAwait(false);

        if (session is null || session.ParkingSessionId != command.ParkingSessionId || session.SiteId != command.SiteId)
        {
            return Rejected(command.CorrelationId, "SESSION_NOT_FOUND", "The parking session is unavailable for this Site.");
        }

        if (string.Equals(session.PaymentConfirmationStatus, "RECORDED", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(session.ExitAuthorizationStatus, "ISSUED", StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                command.CorrelationId,
                "STATUTORY_DISCOUNT_REQUEST_COMPLETED_TRANSACTION",
                "Statutory discount request is no longer available because payment has been completed and exit authorization has been issued.");
        }

        if (string.IsNullOrWhiteSpace(_uploadOptions.BucketName))
        {
            return Rejected(command.CorrelationId, "STATUTORY_ID_IMAGE_STORAGE_UNAVAILABLE", "The protected evidence store is unavailable.", retryable: true);
        }

        await using var memory = new MemoryStream(capacity: (int)Math.Min(command.ContentLength.Value, maximumLength));
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumLength)
            {
                return Rejected(command.CorrelationId, "STATUTORY_ID_PHOTO_INVALID", "Use a JPEG or PNG ID photo within the configured size limit.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (total != command.ContentLength.Value)
        {
            return Rejected(command.CorrelationId, "STATUTORY_ID_PHOTO_INVALID", "The ID photo upload was incomplete.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var partition = string.IsNullOrWhiteSpace(_uploadOptions.EnvironmentPartition)
            ? "local"
            : _uploadOptions.EnvironmentPartition.Trim().ToLowerInvariant();
        var storageReference = $"{partition}/operator-console/statutory-id/{session.SiteId:N}/{command.ParkingSessionId:N}/{Guid.NewGuid():N}{extension}";
        memory.Position = 0;

        var storageResult = await _storage.UploadObjectAsync(
            new StatutoryEvidenceObjectUploadRequest(
                _uploadOptions.BucketName,
                storageReference,
                contentType,
                total,
                checksum,
                memory),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(storageResult.Classification, "ACCEPTED", StringComparison.Ordinal))
        {
            return Rejected(command.CorrelationId, "STATUTORY_ID_IMAGE_STORAGE_UNAVAILABLE", "The protected evidence store is unavailable.", storageResult.Retryable);
        }

        var lifetime = TimeSpan.FromMinutes(Math.Max(1, _options.ReceiptLifetimeMinutes));
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);
        var payload = JsonSerializer.Serialize(new ReceiptPayload(
            PayloadVersion,
            command.UserId,
            session.SiteId,
            command.ParkingSessionId,
            entitlementType,
            storageReference,
            checksum,
            contentType,
            total));
        var receipt = _protector.Protect(payload, lifetime);

        return new OperatorConsoleStatutoryIdPhotoUploadOutcome(
            Accepted: true,
            receipt,
            expiresAt,
            ErrorCode: null,
            "ID photo uploaded to restricted evidence storage.",
            Retryable: false,
            command.CorrelationId);
    }

    public OperatorConsoleStatutoryIdPhotoReceipt? ResolveReceipt(
        string? receipt,
        Guid expectedUserId,
        Guid expectedSiteId,
        Guid expectedParkingSessionId,
        string expectedEntitlementType)
    {
        if (string.IsNullOrWhiteSpace(receipt))
        {
            return null;
        }

        ReceiptPayload? payload;
        try
        {
            var serialized = _protector.Unprotect(receipt.Trim(), out var expiresAt);
            if (expiresAt <= _timeProvider.GetUtcNow())
            {
                return null;
            }

            payload = JsonSerializer.Deserialize<ReceiptPayload>(serialized);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }

        var entitlementType = NormalizeEntitlement(expectedEntitlementType);
        if (payload is null || payload.Version != PayloadVersion ||
            payload.UserId != expectedUserId || payload.SiteId != expectedSiteId ||
            payload.ParkingSessionId != expectedParkingSessionId ||
            !string.Equals(payload.EntitlementType, entitlementType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload.StorageReference) ||
            payload.ChecksumSha256.Length != 64 || payload.ContentLength <= 0 ||
            !StatutoryEvidenceUploadConstants.SupportedContentTypes.Contains(payload.ContentType))
        {
            return null;
        }

        return new OperatorConsoleStatutoryIdPhotoReceipt(
            payload.UserId,
            payload.SiteId,
            payload.ParkingSessionId,
            payload.EntitlementType,
            payload.StorageReference,
            payload.ChecksumSha256,
            payload.ContentType,
            payload.ContentLength);
    }

    private static OperatorConsoleStatutoryIdPhotoUploadOutcome Rejected(
        Guid correlationId,
        string errorCode,
        string message,
        bool retryable = false) =>
        new(false, null, null, errorCode, message, retryable, correlationId);

    private static string NormalizeEntitlement(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string NormalizeContentType(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Split(';', 2)[0].Trim().ToLowerInvariant();

    private sealed record ReceiptPayload(
        int Version,
        Guid UserId,
        Guid SiteId,
        Guid ParkingSessionId,
        string EntitlementType,
        string StorageReference,
        string ChecksumSha256,
        string ContentType,
        long ContentLength);
}
