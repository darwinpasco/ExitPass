using System.Security.Cryptography;
using System.Text;

namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public sealed class StatutoryEvidenceUploadService : IStatutoryEvidenceUploadService
{
    private readonly IStatutoryEvidenceUploadRepository _repository;
    private readonly IStatutoryEvidenceProtectedObjectStorageAdapter _storageAdapter;
    private readonly StatutoryEvidenceUploadOptions _options;

    public StatutoryEvidenceUploadService(
        IStatutoryEvidenceUploadRepository repository,
        IStatutoryEvidenceProtectedObjectStorageAdapter storageAdapter,
        StatutoryEvidenceUploadOptions options)
    {
        _repository = repository;
        _storageAdapter = storageAdapter;
        _options = options;
    }

    public async Task<StatutoryEvidenceUploadAuthorizationOutcome> AuthorizeUploadAsync(
        StatutoryEvidenceUploadAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAuthorizationRequest(command);
        if (validation is not null)
        {
            return AuthorizationRejected(command.CorrelationId, validation);
        }

        var target = await ResolveAuthorizedTargetAsync(
            command.EvidenceSetReference,
            command.EvidenceItemReference,
            command.Actor,
            command.CorrelationId,
            cancellationToken);
        if (target is null)
        {
            return AuthorizationRejected(command.CorrelationId, "SCOPE_DENIED");
        }

        var lifecycle = ValidateAuthorizationLifecycle(target);
        if (lifecycle is not null)
        {
            return AuthorizationRejected(command.CorrelationId, lifecycle);
        }

        var profile = ValidateUploadProfile(command);
        if (profile is not null)
        {
            return AuthorizationRejected(command.CorrelationId, profile);
        }

        var semanticHash = SemanticHashFor(command);
        if (await _repository.HasSemanticConflictAsync(command.IdempotencyScope, command.IdempotencyKey, semanticHash, cancellationToken))
        {
            await _repository.RecordUploadConflictAsync("AUTHORIZE_UPLOAD", command.IdempotencyScope, command.IdempotencyKey, command.CorrelationId, command.Actor, cancellationToken);
            return new StatutoryEvidenceUploadAuthorizationOutcome("SEMANTIC_CONFLICT", false, "IDEMPOTENCY_SEMANTIC_CONFLICT", command.CorrelationId, null, null);
        }

        var replay = await _repository.FindUploadAuthorizationByOperationAsync(command.IdempotencyScope, command.IdempotencyKey, semanticHash, cancellationToken);
        if (replay is not null)
        {
            if (replay.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return AuthorizationRejected(command.CorrelationId, "AUTHORIZATION_EXPIRED");
            }

            var replayLease = await CreateStorageAuthorizationAsync(command, replay.InternalObjectKey, replay.ExpiresAt, cancellationToken);
            return new StatutoryEvidenceUploadAuthorizationOutcome(
                "IDEMPOTENT_REPLAY",
                false,
                null,
                command.CorrelationId,
                ToReadModel(replay, replayLease, command.CorrelationId),
                target.EvidenceItem);
        }

        await _repository.ExpireUploadAuthorizationsAsync(target, DateTimeOffset.UtcNow, command.CorrelationId, command.Actor, cancellationToken);

        if (await _repository.FindActiveUploadAuthorizationAsync(target.EvidenceSetId, target.EvidenceItemId, DateTimeOffset.UtcNow, cancellationToken) is not null)
        {
            return AuthorizationRejected(command.CorrelationId, "ACTIVE_UPLOAD_SESSION_EXISTS");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.AuthorizationTtlSeconds);
        var internalObjectKey = GenerateInternalObjectKey(target.EvidenceItemId);
        var lease = await CreateStorageAuthorizationAsync(command, internalObjectKey, expiresAt, cancellationToken);
        var stored = await _repository.CreateUploadAuthorizationAsync(
            command,
            target,
            semanticHash,
            StatutoryEvidenceUploadConstants.ProviderTypeS3Compatible,
            ResolveBucketReference(),
            internalObjectKey,
            expiresAt,
            cancellationToken);

        if (stored is null)
        {
            return AuthorizationRejected(command.CorrelationId, "ACTIVE_UPLOAD_SESSION_EXISTS");
        }

        return new StatutoryEvidenceUploadAuthorizationOutcome(
            "ACCEPTED",
            false,
            null,
            command.CorrelationId,
            ToReadModel(stored, lease, command.CorrelationId),
            target.EvidenceItem with { UploadStatus = "AUTHORIZED", DeclaredContentType = command.DeclaredContentType });
    }

    public async Task<StatutoryEvidenceUploadFinalizationOutcome> FinalizeUploadAsync(
        StatutoryEvidenceUploadFinalizationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.EvidenceSetReference == Guid.Empty ||
            command.EvidenceItemReference == Guid.Empty ||
            command.UploadAuthorizationReference == Guid.Empty)
        {
            return FinalizationRejected(command.CorrelationId, "INVALID_REQUEST");
        }

        var idempotency = RequireIdempotency(command.IdempotencyScope, command.IdempotencyKey);
        if (idempotency is not null)
        {
            return FinalizationRejected(command.CorrelationId, idempotency);
        }

        var target = await ResolveAuthorizedTargetAsync(
            command.EvidenceSetReference,
            command.EvidenceItemReference,
            command.Actor,
            command.CorrelationId,
            cancellationToken);
        if (target is null)
        {
            return FinalizationRejected(command.CorrelationId, "SCOPE_DENIED");
        }

        var semanticHash = SemanticHashFor(command);
        if (await _repository.HasSemanticConflictAsync(command.IdempotencyScope, command.IdempotencyKey, semanticHash, cancellationToken))
        {
            await _repository.RecordUploadConflictAsync("FINALIZE_UPLOAD", command.IdempotencyScope, command.IdempotencyKey, command.CorrelationId, command.Actor, cancellationToken);
            return new StatutoryEvidenceUploadFinalizationOutcome("SEMANTIC_CONFLICT", false, "IDEMPOTENCY_SEMANTIC_CONFLICT", command.CorrelationId, null);
        }

        var authorization = await _repository.GetUploadAuthorizationAsync(
            command.UploadAuthorizationReference,
            target.EvidenceSetId,
            target.EvidenceItemId,
            cancellationToken);
        if (authorization is null)
        {
            await _repository.RecordUploadDeniedAsync(command.EvidenceSetReference, command.EvidenceItemReference, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.CorrelationId, command.Actor, "UPLOAD_AUTHORIZATION_NOT_FOUND", cancellationToken);
            return FinalizationRejected(command.CorrelationId, "UPLOAD_AUTHORIZATION_NOT_FOUND");
        }

        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _repository.RecordUploadVerificationFailureAsync(command, target, authorization, "AUTHORIZATION_EXPIRED", cancellationToken);
            return FinalizationRejected(command.CorrelationId, "AUTHORIZATION_EXPIRED");
        }

        if (authorization.AuthorizationStatus == "CONSUMED")
        {
            return new StatutoryEvidenceUploadFinalizationOutcome("IDEMPOTENT_REPLAY", false, null, command.CorrelationId, target.EvidenceItem);
        }

        if (authorization.AuthorizationStatus != "ISSUED")
        {
            return FinalizationRejected(command.CorrelationId, "AUTHORIZATION_NOT_USABLE");
        }

        StatutoryEvidenceObjectMetadata? metadata;
        try
        {
            metadata = await _storageAdapter.GetObjectMetadataAsync(
                new StatutoryEvidenceObjectMetadataRequest(ResolveBucketName(), authorization.InternalObjectKey),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await _repository.RecordUploadVerificationFailureAsync(command, target, authorization, StatutoryEvidenceUploadConstants.ProviderUnavailable, cancellationToken);
            return new StatutoryEvidenceUploadFinalizationOutcome("REJECTED", true, StatutoryEvidenceUploadConstants.ProviderUnavailable, command.CorrelationId, null);
        }

        var failure = ValidateProviderMetadata(authorization, metadata);
        if (failure is not null)
        {
            await _repository.RecordUploadVerificationFailureAsync(command, target, authorization, failure, cancellationToken);
            return FinalizationRejected(command.CorrelationId, failure);
        }

        var item = await _repository.FinalizeUploadAsync(command, target, authorization, metadata!, semanticHash, cancellationToken);
        return item is null
            ? FinalizationRejected(command.CorrelationId, "LIFECYCLE_CONFLICT")
            : new StatutoryEvidenceUploadFinalizationOutcome("ACCEPTED", false, null, command.CorrelationId, item);
    }

    private async Task<StatutoryEvidenceUploadTarget?> ResolveAuthorizedTargetAsync(
        Guid evidenceSetReference,
        Guid evidenceItemReference,
        StatutoryEvidenceActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (evidenceSetReference == Guid.Empty || evidenceItemReference == Guid.Empty)
        {
            await _repository.RecordUploadDeniedAsync(null, null, null, null, null, correlationId, actor, "MALFORMED_REFERENCE", cancellationToken);
            return null;
        }

        var target = await _repository.GetUploadTargetAsync(evidenceSetReference, evidenceItemReference, cancellationToken);
        if (target is null)
        {
            await _repository.RecordUploadDeniedAsync(evidenceSetReference, evidenceItemReference, null, null, null, correlationId, actor, "UNKNOWN_REFERENCE", cancellationToken);
            return null;
        }

        if (!StatutoryEvidenceMetadataConstants.CodeComparer.Equals(actor.SourceChannel, target.EvidenceSet.SourceChannel) ||
            !await _repository.ActorHasScopeAsync(actor, StatutoryEvidenceScopeOperations.Capture, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, cancellationToken))
        {
            await _repository.RecordUploadDeniedAsync(evidenceSetReference, evidenceItemReference, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, correlationId, actor, "SCOPE_DENIED", cancellationToken);
            return null;
        }

        return target;
    }

    private string? ValidateUploadProfile(StatutoryEvidenceUploadAuthorizationCommand command)
    {
        if (_options.MaxContentLengthBytes <= 0 ||
            _options.AuthorizationTtlSeconds <= 0 ||
            string.IsNullOrWhiteSpace(_options.BucketName) ||
            string.IsNullOrWhiteSpace(_options.BucketReference) ||
            string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return StatutoryEvidenceUploadConstants.UploadProfileUnavailable;
        }

        if (_options.RequireTlsForNonLocal &&
            _options.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !IsLocalEndpoint(_options.Endpoint))
        {
            return StatutoryEvidenceUploadConstants.UploadProfileUnavailable;
        }

        if (!_options.AllowedContentTypes.Contains(command.DeclaredContentType, StringComparer.OrdinalIgnoreCase) ||
            !StatutoryEvidenceUploadConstants.SupportedContentTypes.Contains(command.DeclaredContentType))
        {
            return StatutoryEvidenceUploadConstants.UnsupportedContentType;
        }

        if (command.DeclaredContentLength > _options.MaxContentLengthBytes)
        {
            return StatutoryEvidenceUploadConstants.ContentLengthExceeded;
        }

        return null;
    }

    private static string? ValidateAuthorizationRequest(StatutoryEvidenceUploadAuthorizationCommand command)
    {
        if (command.EvidenceSetReference == Guid.Empty || command.EvidenceItemReference == Guid.Empty)
        {
            return "INVALID_REQUEST";
        }

        if (string.IsNullOrWhiteSpace(command.DeclaredContentType) ||
            command.DeclaredContentLength <= 0 ||
            string.IsNullOrWhiteSpace(command.MediaClass))
        {
            return "INVALID_UPLOAD_DECLARATION";
        }

        if (!StatutoryEvidenceUploadConstants.ChecksumAlgorithmSha256.Equals(command.ChecksumAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256Hex(command.DeclaredChecksumSha256))
        {
            return StatutoryEvidenceUploadConstants.ChecksumMismatch;
        }

        return RequireIdempotency(command.IdempotencyScope, command.IdempotencyKey);
    }

    private static string? ValidateAuthorizationLifecycle(StatutoryEvidenceUploadTarget target)
    {
        if (target.EvidenceSet.SetStatus == "LOCKED_FOR_REVIEW")
        {
            return "REVIEW_LOCKED";
        }

        if (target.EvidenceSet.SetStatus == "TOMBSTONED" ||
            target.EvidenceItem.DeletionStatus is "DELETED" or "REQUESTED" ||
            target.EvidenceItem.UploadStatus == "UPLOADED")
        {
            return "LIFECYCLE_CONFLICT";
        }

        return null;
    }

    private static string? ValidateProviderMetadata(
        StatutoryEvidenceUploadAuthorizationStorageRecord authorization,
        StatutoryEvidenceObjectMetadata? metadata)
    {
        if (metadata is null)
        {
            return StatutoryEvidenceUploadConstants.ObjectNotFound;
        }

        if (!string.Equals(metadata.ContentType, authorization.ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return StatutoryEvidenceUploadConstants.ContentTypeMismatch;
        }

        if (metadata.ContentLength != authorization.ExpectedContentLength)
        {
            return StatutoryEvidenceUploadConstants.ContentLengthMismatch;
        }

        if (!string.Equals(metadata.ChecksumSha256, authorization.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
        {
            return StatutoryEvidenceUploadConstants.ChecksumMismatch;
        }

        return null;
    }

    private async Task<StatutoryEvidenceObjectUploadAuthorization> CreateStorageAuthorizationAsync(
        StatutoryEvidenceUploadAuthorizationCommand command,
        string internalObjectKey,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        await _storageAdapter.CreateUploadAuthorizationAsync(
            new StatutoryEvidenceObjectUploadAuthorizationRequest(
                ResolveBucketName(),
                internalObjectKey,
                command.DeclaredContentType,
                command.DeclaredContentLength,
            command.DeclaredChecksumSha256.ToLowerInvariant(),
            expiresAt),
            cancellationToken);

    private static string SemanticHashFor(StatutoryEvidenceUploadAuthorizationCommand command) =>
        StatutoryEvidenceSemanticHash.For(new
        {
            command.EvidenceSetReference,
            command.EvidenceItemReference,
            command.DeclaredContentType,
            command.DeclaredContentLength,
            command.MediaClass,
            command.ChecksumAlgorithm,
            DeclaredChecksumSha256 = command.DeclaredChecksumSha256.ToLowerInvariant(),
            command.IdempotencyScope,
            command.IdempotencyKey,
            command.Actor.UserId,
            command.Actor.ServiceIdentityId,
            command.Actor.SourceChannel
        });

    private static string SemanticHashFor(StatutoryEvidenceUploadFinalizationCommand command) =>
        StatutoryEvidenceSemanticHash.For(new
        {
            command.EvidenceSetReference,
            command.EvidenceItemReference,
            command.UploadAuthorizationReference,
            command.IdempotencyScope,
            command.IdempotencyKey,
            command.Actor.UserId,
            command.Actor.ServiceIdentityId,
            command.Actor.SourceChannel
        });

    private StatutoryEvidenceUploadAuthorizationReadModel ToReadModel(
        StatutoryEvidenceUploadAuthorizationStorageRecord stored,
        StatutoryEvidenceObjectUploadAuthorization lease,
        Guid correlationId) =>
        new(
            stored.UploadAuthorizationReference,
            stored.UploadMethod,
            lease.UploadUrl,
            lease.RequiredHeaders,
            stored.ExpiresAt,
            _options.MaxContentLengthBytes,
            stored.ExpectedContentType,
            correlationId);

    private static string GenerateInternalObjectKey(Guid evidenceItemId)
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return $"evidence/{random[..2]}/{random[2..4]}/{evidenceItemId:N}/{random}";
    }

    private string ResolveBucketName() =>
        !string.IsNullOrWhiteSpace(_options.BucketName) ? _options.BucketName : throw new InvalidOperationException("Evidence upload storage bucket is not configured.");

    private string ResolveBucketReference() =>
        !string.IsNullOrWhiteSpace(_options.BucketReference) ? _options.BucketReference : throw new InvalidOperationException("Evidence upload storage bucket reference is not configured.");

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsLocalEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase));

    private static string? RequireIdempotency(string scope, string key) =>
        string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(key)
            ? "IDEMPOTENCY_REQUIRED"
            : null;

    private static StatutoryEvidenceUploadAuthorizationOutcome AuthorizationRejected(Guid correlationId, string code) =>
        new("REJECTED", false, code, correlationId, null, null);

    private static StatutoryEvidenceUploadFinalizationOutcome FinalizationRejected(Guid correlationId, string code) =>
        new("REJECTED", false, code, correlationId, null);
}
