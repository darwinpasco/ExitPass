using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class StatutoryEvidenceUploadServiceTests
{
    private readonly FakeUploadRepository _repository = new();
    private readonly FakeStorageAdapter _storage = new();
    private readonly StatutoryEvidenceUploadService _sut;

    public StatutoryEvidenceUploadServiceTests()
    {
        _sut = new StatutoryEvidenceUploadService(_repository, _storage, Options());
    }

    [Fact]
    public async Task AuthorizeUpload_WhenJpegIsSupported_IssuesShortLivedPutAuthorization()
    {
        var result = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.UploadAuthorization.Should().NotBeNull();
        result.UploadAuthorization!.UploadMethod.Should().Be("PUT");
        result.UploadAuthorization.AcceptedContentType.Should().Be("image/jpeg");
        result.UploadAuthorization.UploadUrl.ToString().Should().Contain("https://storage.local/upload/");
        result.UploadAuthorization.RequiredHeaders.Should().ContainKey("Content-Type");
        result.EvidenceItem!.UploadStatus.Should().Be("AUTHORIZED");
        _repository.Authorizations.Should().HaveCount(1);
    }

    [Fact]
    public async Task AuthorizeUpload_WhenPngIsSupported_IssuesAuthorization()
    {
        var result = await _sut.AuthorizeUploadAsync(Authorize() with { DeclaredContentType = "image/png", IdempotencyKey = "png" }, CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.UploadAuthorization!.AcceptedContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task AuthorizeUpload_WhenPdfDeclared_Rejects()
    {
        var result = await _sut.AuthorizeUploadAsync(Authorize() with { DeclaredContentType = "application/pdf" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("UNSUPPORTED_CONTENT_TYPE");
        _repository.Authorizations.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeUpload_WhenMaximumSizeMissing_FailsClosed()
    {
        var options = Options();
        options.MaxContentLengthBytes = 0;
        var sut = new StatutoryEvidenceUploadService(_repository, _storage, options);

        var result = await sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("UPLOAD_PROFILE_UNAVAILABLE");
    }

    [Fact]
    public async Task AuthorizeUpload_WhenContentLengthTooLarge_Rejects()
    {
        var result = await _sut.AuthorizeUploadAsync(Authorize() with { DeclaredContentLength = 10_000_000 }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("CONTENT_LENGTH_EXCEEDED");
    }

    [Fact]
    public async Task AuthorizeUpload_WhenChecksumIsMissing_RejectsWithoutThrowing()
    {
        var result = await _sut.AuthorizeUploadAsync(Authorize() with { DeclaredChecksumSha256 = null! }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be(StatutoryEvidenceUploadConstants.ChecksumMismatch);
        _repository.Authorizations.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeUpload_WhenSameKeyAndSameSemantics_ReplaysOriginalAuthorization()
    {
        var first = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        var replay = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        replay.Classification.Should().Be("IDEMPOTENT_REPLAY");
        replay.UploadAuthorization!.UploadAuthorizationReference.Should().Be(first.UploadAuthorization!.UploadAuthorizationReference);
        _repository.Authorizations.Should().HaveCount(1);
    }

    [Fact]
    public async Task AuthorizeUpload_WhenSameKeyAndDifferentSemantics_ConflictsWithoutMutation()
    {
        await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        var conflict = await _sut.AuthorizeUploadAsync(Authorize() with { DeclaredContentLength = 2048 }, CancellationToken.None);

        conflict.Classification.Should().Be("SEMANTIC_CONFLICT");
        conflict.ErrorCode.Should().Be("IDEMPOTENCY_SEMANTIC_CONFLICT");
        _repository.Authorizations.Should().HaveCount(1);
        _repository.ConflictEvents.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizeUpload_WhenAnotherSessionIsActive_RejectsWithoutCreatingAnotherAuthorization()
    {
        await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        var result = await _sut.AuthorizeUploadAsync(Authorize() with { IdempotencyKey = "another-session" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("ACTIVE_UPLOAD_SESSION_EXISTS");
        _repository.Authorizations.Should().ContainSingle();
    }

    [Fact]
    public async Task AuthorizeUpload_WhenPriorSessionExpired_ExpiresItAndIssuesReplacement()
    {
        var first = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _repository.SetExpiration(first.UploadAuthorization!.UploadAuthorizationReference, DateTimeOffset.UtcNow.AddSeconds(-1));

        var replacement = await _sut.AuthorizeUploadAsync(Authorize() with { IdempotencyKey = "replacement" }, CancellationToken.None);

        replacement.Classification.Should().Be("ACCEPTED");
        replacement.UploadAuthorization!.UploadAuthorizationReference.Should().NotBe(first.UploadAuthorization.UploadAuthorizationReference);
        _repository.Authorizations.Should().Contain(auth => auth.UploadAuthorizationReference == first.UploadAuthorization.UploadAuthorizationReference && auth.AuthorizationStatus == "EXPIRED");
        _repository.ExpiredEvents.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizeUpload_WhenPrincipalOutsideScope_RejectsAndAudits()
    {
        _repository.ScopeAllowed = false;

        var result = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
        _repository.DeniedEvents.Should().Be(1);
        _repository.Authorizations.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeUpload_WhenReviewLocked_Rejects()
    {
        _repository.Target = _repository.Target with { EvidenceSet = _repository.Target.EvidenceSet with { SetStatus = "LOCKED_FOR_REVIEW" } };

        var result = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("REVIEW_LOCKED");
    }

    [Fact]
    public async Task FinalizeUpload_WhenProviderMetadataMatches_MarksUploadedOnly()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/jpeg", 1024, Hash, "v1", "AES256");

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.EvidenceItem!.UploadStatus.Should().Be("UPLOADED");
        result.EvidenceItem.ValidationStatus.Should().Be("NOT_STARTED");
        result.EvidenceItem.ScanStatus.Should().Be("NOT_STARTED");
        result.EvidenceItem.ReviewabilityStatus.Should().Be("NOT_REVIEWABLE");
    }

    [Fact]
    public async Task FinalizeUpload_WhenChecksumDiffers_RejectsWithoutUploadTransition()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/jpeg", 1024, "0".PadLeft(64, '0'), "v1", "AES256");

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("CHECKSUM_MISMATCH");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenContentTypeDiffers_RejectsWithoutUploadTransition()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/png", 1024, Hash, "v1", "AES256");

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("CONTENT_TYPE_MISMATCH");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenContentLengthDiffers_RejectsWithoutUploadTransition()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/jpeg", 2048, Hash, "v1", "AES256");

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("CONTENT_LENGTH_MISMATCH");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenObjectMetadataIsMissing_RejectsWithoutUploadTransition()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = null;

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("OBJECT_NOT_FOUND");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenProviderUnavailable_ReturnsRetryableSafeRejection()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.ThrowOnMetadata = true;

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.Retryable.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_UNAVAILABLE");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenAuthorizationExpired_Rejects()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _repository.Authorizations[0] = _repository.Authorizations[0] with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };

        var result = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("AUTHORIZATION_EXPIRED");
        _repository.Target.EvidenceItem.UploadStatus.Should().Be("AUTHORIZED");
        _repository.VerificationFailures.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeUpload_WhenSameKeyAndSameSemantics_ReplaysUploadedItem()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/jpeg", 1024, Hash, "v1", "AES256");
        await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);

        var replay = await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization.UploadAuthorizationReference), CancellationToken.None);

        replay.Classification.Should().Be("IDEMPOTENT_REPLAY");
        replay.EvidenceItem!.UploadStatus.Should().Be("UPLOADED");
    }

    [Fact]
    public async Task FinalizeUpload_WhenSameKeyAndDifferentSemantics_ConflictsBeforeProviderLookup()
    {
        var authorization = await _sut.AuthorizeUploadAsync(Authorize(), CancellationToken.None);
        _storage.Metadata = new StatutoryEvidenceObjectMetadata("image/jpeg", 1024, Hash, "v1", "AES256");
        await _sut.FinalizeUploadAsync(Finalize(authorization.UploadAuthorization!.UploadAuthorizationReference), CancellationToken.None);
        _storage.MetadataLookups = 0;

        var conflict = await _sut.FinalizeUploadAsync(Finalize(Guid.NewGuid()), CancellationToken.None);

        conflict.Classification.Should().Be("SEMANTIC_CONFLICT");
        conflict.ErrorCode.Should().Be("IDEMPOTENCY_SEMANTIC_CONFLICT");
        _repository.ConflictEvents.Should().Be(1);
        _storage.MetadataLookups.Should().Be(0);
    }

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static StatutoryEvidenceUploadOptions Options() =>
        new()
        {
            Endpoint = "https://storage.local",
            PublicUploadEndpoint = "https://storage.local",
            BucketName = "private-evidence",
            BucketReference = "configured-private-evidence-bucket",
            AccessKeyId = "unit-test-access-key-id",
            SecretAccessKey = "unit-test-signing-material",
            MaxContentLengthBytes = 5_000_000,
            AuthorizationTtlSeconds = 300
        };

    private static StatutoryEvidenceUploadAuthorizationCommand Authorize() =>
        new(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            "image/jpeg",
            1024,
            "DOCUMENT_IMAGE",
            "SHA256",
            Hash,
            "upload-auth",
            "auth-key",
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            Actor());

    private static StatutoryEvidenceUploadFinalizationCommand Finalize(Guid authorizationReference) =>
        new(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            authorizationReference,
            "upload-finalize",
            "finalize-key",
            Guid.Parse("30000000-0000-0000-0000-000000000004"),
            Actor());

    private static StatutoryEvidenceActor Actor() =>
        new(null, Guid.Parse("30000000-0000-0000-0000-000000000005"), "WEBPAY");

    private sealed class FakeStorageAdapter : IStatutoryEvidenceProtectedObjectStorageAdapter
    {
        public StatutoryEvidenceObjectMetadata? Metadata { get; set; }
        public bool ThrowOnMetadata { get; set; }
        public int MetadataLookups { get; set; }

        public Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(StatutoryEvidenceObjectUploadAuthorizationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceObjectUploadAuthorization(
                new Uri($"https://storage.local/upload/{Guid.NewGuid():N}"),
                new Dictionary<string, string>
                {
                    ["Content-Type"] = request.ContentType,
                    ["x-amz-checksum-sha256"] = request.ChecksumSha256
                }));

        public Task<StatutoryEvidenceObjectUploadResult> UploadObjectAsync(StatutoryEvidenceObjectUploadRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceObjectUploadResult("ACCEPTED", false));

        public Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(StatutoryEvidenceObjectMetadataRequest request, CancellationToken cancellationToken)
        {
            MetadataLookups++;
            if (ThrowOnMetadata)
            {
                throw new InvalidOperationException("synthetic provider unavailable");
            }

            return Task.FromResult(Metadata);
        }

        public Task<StatutoryEvidenceObjectContent> GetObjectContentAsync(StatutoryEvidenceObjectContentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUploadRepository : IStatutoryEvidenceUploadRepository
    {
        private readonly Dictionary<(string Scope, string Key), (string Hash, StatutoryEvidenceUploadAuthorizationStorageRecord Authorization)> _operations = new();

        public StatutoryEvidenceUploadTarget Target { get; set; } = CreateTarget();
        public List<StatutoryEvidenceUploadAuthorizationStorageRecord> Authorizations { get; } = [];
        public bool ScopeAllowed { get; set; } = true;
        public int DeniedEvents { get; private set; }
        public int ConflictEvents { get; private set; }
        public int VerificationFailures { get; private set; }
        public int ExpiredEvents { get; private set; }

        public Task<StatutoryEvidenceUploadTarget?> GetUploadTargetAsync(Guid evidenceSetReference, Guid evidenceItemReference, CancellationToken cancellationToken) =>
            Task.FromResult(Target.EvidenceSet.EvidenceSetReference == evidenceSetReference && Target.EvidenceItem.EvidenceItemReference == evidenceItemReference ? Target : null);

        public Task<StatutoryEvidenceUploadSession?> GetUploadSessionAsync(Guid uploadAuthorizationReference, CancellationToken cancellationToken)
        {
            var authorization = Authorizations.SingleOrDefault(auth => auth.UploadAuthorizationReference == uploadAuthorizationReference);
            return Task.FromResult<StatutoryEvidenceUploadSession?>(authorization is null ? null : new StatutoryEvidenceUploadSession(Target, authorization));
        }

        public Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> FindActiveUploadAuthorizationAsync(Guid evidenceSetId, Guid evidenceItemId, DateTimeOffset evaluatedAt, CancellationToken cancellationToken) =>
            Task.FromResult(Authorizations.SingleOrDefault(auth => auth.EvidenceSetId == evidenceSetId && auth.EvidenceItemId == evidenceItemId && auth.AuthorizationStatus == "ISSUED" && auth.ExpiresAt > evaluatedAt));

        public Task<int> ExpireUploadAuthorizationsAsync(StatutoryEvidenceUploadTarget target, DateTimeOffset evaluatedAt, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
        {
            var count = 0;
            for (var index = 0; index < Authorizations.Count; index++)
            {
                var authorization = Authorizations[index];
                if (authorization.EvidenceSetId == target.EvidenceSetId && authorization.EvidenceItemId == target.EvidenceItemId && authorization.AuthorizationStatus == "ISSUED" && authorization.ExpiresAt <= evaluatedAt)
                {
                    Authorizations[index] = authorization with { AuthorizationStatus = "EXPIRED", FailureClassification = "AUTHORIZATION_EXPIRED" };
                    count++;
                }
            }

            ExpiredEvents += count > 0 ? 1 : 0;
            return Task.FromResult(count);
        }

        public void SetExpiration(Guid reference, DateTimeOffset expiresAt)
        {
            var index = Authorizations.FindIndex(auth => auth.UploadAuthorizationReference == reference);
            Authorizations[index] = Authorizations[index] with { ExpiresAt = expiresAt };
        }

        public Task<bool> ActorHasScopeAsync(StatutoryEvidenceActor actor, string operation, Guid siteId, Guid siteGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(ScopeAllowed &&
                operation == StatutoryEvidenceScopeOperations.Capture &&
                actor.ServiceIdentityId.HasValue &&
                siteId == Target.EvidenceSet.SiteId &&
                siteGroupId == Target.EvidenceSet.SiteGroupId);

        public Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> FindUploadAuthorizationByOperationAsync(string idempotencyScope, string idempotencyKey, string semanticRequestHash, CancellationToken cancellationToken) =>
            Task.FromResult(_operations.TryGetValue((idempotencyScope, idempotencyKey), out var replay) && replay.Hash == semanticRequestHash ? replay.Authorization : null);

        public Task<bool> HasSemanticConflictAsync(string idempotencyScope, string idempotencyKey, string semanticRequestHash, CancellationToken cancellationToken) =>
            Task.FromResult(_operations.TryGetValue((idempotencyScope, idempotencyKey), out var replay) && replay.Hash != semanticRequestHash);

        public Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> CreateUploadAuthorizationAsync(StatutoryEvidenceUploadAuthorizationCommand command, StatutoryEvidenceUploadTarget target, string semanticRequestHash, string providerType, string bucketReference, string internalObjectKey, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            var authorization = new StatutoryEvidenceUploadAuthorizationStorageRecord(Guid.NewGuid(), Guid.NewGuid(), target.EvidenceSetId, target.EvidenceItemId, Guid.NewGuid(), providerType, bucketReference, internalObjectKey, "PUT", command.DeclaredContentType, command.DeclaredContentLength, "SHA256", command.DeclaredChecksumSha256, "ISSUED", DateTimeOffset.UtcNow, expiresAt, null, null, null, null, null, null, null);
            Authorizations.Add(authorization);
            _operations[(command.IdempotencyScope, command.IdempotencyKey)] = (semanticRequestHash, authorization);
            Target = target with { EvidenceItem = target.EvidenceItem with { UploadStatus = "AUTHORIZED", DeclaredContentType = command.DeclaredContentType } };
            return Task.FromResult<StatutoryEvidenceUploadAuthorizationStorageRecord?>(authorization);
        }

        public Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> GetUploadAuthorizationAsync(Guid authorizationReference, Guid evidenceSetId, Guid evidenceItemId, CancellationToken cancellationToken) =>
            Task.FromResult(Authorizations.SingleOrDefault(auth => auth.UploadAuthorizationReference == authorizationReference && auth.EvidenceSetId == evidenceSetId && auth.EvidenceItemId == evidenceItemId));

        public Task<StatutoryEvidenceItemReadModel?> FinalizeUploadAsync(StatutoryEvidenceUploadFinalizationCommand command, StatutoryEvidenceUploadTarget target, StatutoryEvidenceUploadAuthorizationStorageRecord authorization, StatutoryEvidenceObjectMetadata metadata, string semanticRequestHash, CancellationToken cancellationToken)
        {
            var item = target.EvidenceItem with { UploadStatus = "UPLOADED" };
            var consumed = authorization with { AuthorizationStatus = "CONSUMED", ConsumedAt = DateTimeOffset.UtcNow };
            var index = Authorizations.FindIndex(auth => auth.UploadAuthorizationReference == authorization.UploadAuthorizationReference);
            if (index >= 0)
            {
                Authorizations[index] = consumed;
            }

            Target = target with { EvidenceItem = item };
            _operations[(command.IdempotencyScope, command.IdempotencyKey)] = (semanticRequestHash, consumed);
            return Task.FromResult<StatutoryEvidenceItemReadModel?>(item);
        }

        public Task RecordUploadDeniedAsync(Guid? evidenceSetReference, Guid? evidenceItemReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken)
        {
            DeniedEvents++;
            return Task.CompletedTask;
        }

        public Task RecordUploadConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
        {
            ConflictEvents++;
            return Task.CompletedTask;
        }

        public Task RecordUploadVerificationFailureAsync(StatutoryEvidenceUploadFinalizationCommand command, StatutoryEvidenceUploadTarget target, StatutoryEvidenceUploadAuthorizationStorageRecord authorization, string reasonCode, CancellationToken cancellationToken)
        {
            VerificationFailures++;
            return Task.CompletedTask;
        }

        private static StatutoryEvidenceUploadTarget CreateTarget()
        {
            var set = new StatutoryEvidenceSetReadModel(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Guid.Parse("30000000-0000-0000-0000-000000000010"),
                Guid.Parse("30000000-0000-0000-0000-000000000011"),
                Guid.Parse("30000000-0000-0000-0000-000000000012"),
                Guid.Parse("30000000-0000-0000-0000-000000000013"),
                Guid.Parse("30000000-0000-0000-0000-000000000014"),
                "SENIOR_CITIZEN",
                "WEBPAY",
                "OPEN",
                "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
                "1",
                "PH_STATUTORY_PARKING_STANDARD",
                "2026-07-28",
                "ACTIVE",
                "NOT_REQUESTED",
                false,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                []);
            var item = new StatutoryEvidenceItemReadModel(
                Guid.Parse("30000000-0000-0000-0000-000000000002"),
                "SENIOR_CITIZEN_ID",
                "FRONT",
                "NOT_AUTHORIZED",
                "NOT_STARTED",
                "NOT_STARTED",
                "NOT_REVIEWABLE",
                "UNBOUND",
                "ACTIVE",
                "NOT_REQUESTED",
                false,
                "DOCUMENT_IMAGE",
                null,
                "SENIOR_CITIZEN_ID_FRONT_V1",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            return new StatutoryEvidenceUploadTarget(Guid.NewGuid(), Guid.NewGuid(), set, item);
        }
    }
}
