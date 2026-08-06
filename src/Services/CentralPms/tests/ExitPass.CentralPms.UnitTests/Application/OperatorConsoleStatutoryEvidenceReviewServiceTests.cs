using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleStatutoryEvidenceReviewServiceTests
{
    private static readonly Guid UserId = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceId = Guid.Parse("91000000-0000-0000-0000-000000000002");
    private static readonly Guid ShiftId = Guid.Parse("91000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("91000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("91000000-0000-0000-0000-000000000005");
    private static readonly Guid DecisionId = Guid.Parse("91000000-0000-0000-0000-000000000006");
    private static readonly Guid SetId = Guid.Parse("91000000-0000-0000-0000-000000000007");
    private static readonly Guid SetReference = Guid.Parse("91000000-0000-0000-0000-000000000008");
    private static readonly Guid ItemId = Guid.Parse("91000000-0000-0000-0000-000000000009");
    private static readonly Guid ItemReference = Guid.Parse("91000000-0000-0000-0000-00000000000a");
    private static readonly Guid AuthorizationId = Guid.Parse("91000000-0000-0000-0000-00000000000b");
    private static readonly Guid AuthorizationReference = Guid.Parse("91000000-0000-0000-0000-00000000000c");
    private static readonly Guid ParkingSessionId = Guid.Parse("91000000-0000-0000-0000-00000000000d");
    private static readonly Guid CorrelationId = Guid.Parse("91000000-0000-0000-0000-00000000000e");
    private const string Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void DedicatedPolicy_RequiresOnlyReviewPreviewPermission()
    {
        var permissions = CentralPmsRbacPolicyCatalog.ResolvePermissions(OperatorConsoleStatutoryEvidenceReviewConstants.Policy);

        permissions.Should().ContainSingle().Which.Should().Be(OperatorConsoleStatutoryEvidenceReviewConstants.Permission);
        permissions.Should().NotContain("statutory-discounts.evidence.capture");
        permissions.Should().NotContain("statutory-discounts.evidence.view");
        permissions.Should().NotContain("statutory-discounts.evidence-governance.view");
        permissions.Should().NotContain("reconciliation.manage");
    }

    [Fact]
    public async Task ReadAsync_AuthorizedScopedReviewer_ReturnsSafeLifecycleAndAudits()
    {
        var fixture = CreateFixture(Record());

        var result = await fixture.Sut.ReadAsync(DecisionId, AccessContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.EvidenceSetReference.Should().Be(SetReference);
        result.Items.Should().ContainSingle();
        result.Items[0].PreviewPermitted.Should().BeTrue();
        result.Items[0].AuthoritativeContentType.Should().Be("image/jpeg");
        await fixture.AccessService.Received(1).EvaluateAsync(
            Arg.Is<OperatorConsoleAccessEvaluationCommand>(command =>
                command.ControlledActionCode == OperatorConsoleActionCodes.ReviewEvidence &&
                command.EvidenceAccessIntent == "REVIEW_PREVIEW"),
            Arg.Any<CancellationToken>());
        fixture.Repository.ReceivedCalls().Should().Contain(call =>
            call.GetMethodInfo().Name == nameof(IOperatorConsoleStatutoryEvidenceReviewRepository.RecordAccessEventAsync) &&
            ((OperatorConsoleStatutoryEvidenceAccessEvent)call.GetArguments()[0]!).SafeReasonCode == "OPERATOR_CONSOLE_EVIDENCE_METADATA_READ");
    }

    [Fact]
    public async Task ReadAsync_CrossSite_IsAntiEnumeratedAndAuditedWithoutTargetIds()
    {
        var fixture = CreateFixture(Record() with { SiteId = Guid.NewGuid() });

        var result = await fixture.Sut.ReadAsync(DecisionId, AccessContext(), CancellationToken.None);

        result.Should().BeNull();
        await fixture.Repository.Received(1).RecordAccessEventAsync(
            Arg.Is<OperatorConsoleStatutoryEvidenceAccessEvent>(accessEvent =>
                accessEvent.EventType == "ACCESS_DENIED" &&
                accessEvent.EvidenceSetId == null &&
                accessEvent.EvidenceItemId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_CrossSiteGroup_IsAntiEnumeratedAndAuditedWithoutTargetIds()
    {
        var fixture = CreateFixture(Record() with { SiteGroupId = Guid.NewGuid() });

        var result = await fixture.Sut.ReadAsync(DecisionId, AccessContext(), CancellationToken.None);

        result.Should().BeNull();
        await fixture.Repository.Received(1).RecordAccessEventAsync(
            Arg.Is<OperatorConsoleStatutoryEvidenceAccessEvent>(accessEvent =>
                accessEvent.EventType == "ACCESS_DENIED" &&
                accessEvent.EvidenceSetId == null &&
                accessEvent.EvidenceItemId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_MissingDurableScope_IsDenied()
    {
        var fixture = CreateFixture(Record(), access: AccessResult() with
        {
            SiteContext = new OperatorConsoleSiteContextResult(null, null, Assigned: false)
        });

        var action = () => fixture.Sut.ReadAsync(DecisionId, AccessContext(), CancellationToken.None);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        await fixture.Repository.DidNotReceive().ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(DeniedLifecycleCases))]
    public async Task OpenPreviewAsync_NonEligibleLifecycle_FailsClosed(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        string expectedCode)
    {
        var fixture = CreateFixture(record);

        var result = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be(expectedCode);
        result.Content.Should().BeNull();
        await fixture.Storage.DidNotReceiveWithAnyArgs().OpenObjectContentStreamAsync(default!, default);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task OpenPreviewAsync_CurrentCleanReviewableImage_ReturnsProtectedStream(string contentType)
    {
        var bytes = contentType == "image/png" ? new byte[] { 0x89, 0x50, 0x4e, 0x47 } : new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
        var fixture = CreateFixture(Record(Item() with
        {
            DeclaredContentType = contentType,
            VerifiedContentType = contentType,
            VerifiedContentLength = bytes.Length
        }));
        fixture.Storage.OpenObjectContentStreamAsync(Arg.Any<StatutoryEvidenceObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StatutoryEvidenceObjectContent(new MemoryStream(bytes), contentType, bytes.Length, Checksum, "version-1", "AES256"));
        fixture.Repository.IsCurrentPreviewTargetAsync(Arg.Any<OperatorConsoleStatutoryEvidencePreviewTarget>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.Content.Should().NotBeNull();
        result.Content!.ContentType.Should().Be(contentType);
        result.AuditContext.Should().NotBeNull();
        await result.Content.DisposeAsync();
    }

    [Fact]
    public async Task OpenPreviewAsync_ProviderMetadataChanged_IsStaleAndDisposesStream()
    {
        var stream = Substitute.For<Stream>();
        var fixture = CreateFixture(Record());
        fixture.Storage.OpenObjectContentStreamAsync(Arg.Any<StatutoryEvidenceObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StatutoryEvidenceObjectContent(stream, "image/png", 4, Checksum, "version-1", "AES256"));

        var result = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        result.ErrorCode.Should().Be("OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STALE");
        await stream.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task OpenPreviewAsync_CurrentVersionChangedBeforeStream_IsStale()
    {
        var fixture = CreateFixture(Record());
        fixture.Repository.IsCurrentPreviewTargetAsync(Arg.Any<OperatorConsoleStatutoryEvidencePreviewTarget>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        result.ErrorCode.Should().Be("OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STALE");
    }

    [Fact]
    public async Task OpenPreviewAsync_StorageUnavailable_ReturnsSafeRetryableFailure()
    {
        var fixture = CreateFixture(Record());
        fixture.Storage.OpenObjectContentStreamAsync(Arg.Any<StatutoryEvidenceObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<StatutoryEvidenceObjectContent>>(_ => throw new InvalidOperationException("provider detail"));

        var result = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        result.ErrorCode.Should().Be("OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task OpenPreviewAsync_HeldEvidence_RemainsReviewableButCannotBeReplaced()
    {
        var fixture = CreateFixture(Record(Item() with
        {
            RetentionStatus = "HELD",
            HoldActive = true
        }) with
        {
            RetentionStatus = "HELD",
            HoldActive = true
        });

        var read = await fixture.Sut.ReadAsync(DecisionId, AccessContext(), CancellationToken.None);
        var preview = await fixture.Sut.OpenPreviewAsync(DecisionId, ItemReference, AccessContext(), CancellationToken.None);

        read!.ReplacementPosture.Should().Be("REPLACEMENT_NOT_ALLOWED");
        read.Items.Single().PreviewPermitted.Should().BeTrue();
        preview.Classification.Should().Be("ACCEPTED");
        await preview.Content!.DisposeAsync();
    }

    [Theory]
    [InlineData("COMPLETED", "ALLOWED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_COMPLETED")]
    [InlineData("CANCELLED", "FAILED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_CANCELLED")]
    [InlineData("FAILED", "FAILED", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STREAM_FAILED")]
    public async Task RecordPreviewStreamOutcomeAsync_UsesPrivacySafeControlledEvent(
        string outcome,
        string eventResult,
        string reasonCode)
    {
        var fixture = CreateFixture(Record());
        var target = Target();

        await fixture.Sut.RecordPreviewStreamOutcomeAsync(
            new OperatorConsoleStatutoryEvidencePreviewAuditContext(
                target,
                new StatutoryEvidenceActor(UserId, null, "OPERATOR_CONSOLE")),
            outcome,
            CancellationToken.None);

        await fixture.Repository.Received(1).RecordAccessEventAsync(
            Arg.Is<OperatorConsoleStatutoryEvidenceAccessEvent>(accessEvent =>
                accessEvent.EventResult == eventResult &&
                accessEvent.SafeReasonCode == reasonCode &&
                accessEvent.Actor.SourceChannel == "OPERATOR_CONSOLE"),
            Arg.Any<CancellationToken>());
    }

    public static IEnumerable<object[]> DeniedLifecycleCases()
    {
        yield return [Record() with { EvidenceRequired = false }, "STATUTORY_EVIDENCE_NOT_REQUIRED"];
        yield return [Record(Item() with { UploadStatus = "AUTHORIZED" }), "STATUTORY_EVIDENCE_UPLOAD_NOT_FINALIZED"];
        yield return [Record(Item() with { ValidationStatus = "PENDING" }), "STATUTORY_EVIDENCE_VALIDATION_PENDING"];
        yield return [Record(Item() with { ValidationStatus = "FAILED" }), "STATUTORY_EVIDENCE_VALIDATION_FAILED"];
        yield return [Record(Item() with { ScanStatus = "PENDING" }), "STATUTORY_EVIDENCE_SCAN_PENDING"];
        yield return [Record(Item() with { ScanStatus = "ERROR_RETRYABLE" }), "STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE"];
        yield return [Record(Item() with { ScanStatus = "UNAVAILABLE" }), "STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE"];
        yield return [Record(Item() with { ScanStatus = "TIMEOUT" }), "STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE"];
        yield return [Record(Item() with { ScanStatus = "MALICIOUS" }), "STATUTORY_EVIDENCE_MALWARE_DETECTED"];
        yield return [Record(Item() with { ScanStatus = "SUSPICIOUS" }), "STATUTORY_EVIDENCE_MALWARE_DETECTED"];
        yield return [Record(Item() with { ScanStatus = "UNKNOWN" }), "STATUTORY_EVIDENCE_SCAN_FAILED"];
        yield return [Record(Item() with { ReviewabilityStatus = "NOT_REVIEWABLE" }), "STATUTORY_EVIDENCE_NOT_REVIEWABLE"];
        yield return [Record(Item() with { BindingStatus = "SUPERSEDED" }), "STATUTORY_EVIDENCE_STALE"];
        yield return [Record(Item() with { DeletionStatus = "REQUESTED" }), "STATUTORY_EVIDENCE_DELETION_IN_PROGRESS"];
        yield return [Record() with { DeletionStatus = "COMPLETED" }, "STATUTORY_EVIDENCE_DELETION_IN_PROGRESS"];
        yield return [Record(Item() with { RetentionStatus = "EXPIRED" }), "STATUTORY_EVIDENCE_RETENTION_INACCESSIBLE"];
        yield return [Record(Item() with { VerifiedContentType = "application/pdf", DeclaredContentType = "application/pdf" }), "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA"];
        yield return [Record(Item() with { DeclaredContentType = "image/png" }), "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA"];
        yield return [Record(Item() with { InternalStorageLocatorReference = "upload-authorization:00000000-0000-0000-0000-000000000000" }), "STATUTORY_EVIDENCE_PREVIEW_STALE"];
    }

    private static Fixture CreateFixture(
        OperatorConsoleStatutoryEvidenceReviewRecord record,
        OperatorConsoleAccessEvaluationResult? access = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(access ?? AccessResult());
        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with { Persisted = true });
        var repository = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewRepository>();
        repository.ReadAsync(DecisionId, Arg.Any<CancellationToken>()).Returns(record);
        repository.IsCurrentPreviewTargetAsync(Arg.Any<OperatorConsoleStatutoryEvidencePreviewTarget>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var storage = Substitute.For<IStatutoryEvidenceProtectedObjectStorageAdapter>();
        storage.OpenObjectContentStreamAsync(Arg.Any<StatutoryEvidenceObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StatutoryEvidenceObjectContent(new MemoryStream([0xff, 0xd8, 0xff, 0xd9]), "image/jpeg", 4, Checksum, "version-1", "AES256"));
        var sut = new OperatorConsoleStatutoryEvidenceReviewService(
            accessService,
            accessWriter,
            repository,
            storage,
            Options.Create(new StatutoryEvidenceUploadOptions
            {
                BucketName = "private-evidence",
                MaxContentLengthBytes = 1024
            }));
        return new Fixture(sut, accessService, repository, storage);
    }

    private static OperatorConsoleReviewAccessContext AccessContext() =>
        new(UserId, DeviceId, ShiftId, SiteId, SiteGroupId, CorrelationId, "review-key");

    private static OperatorConsoleAccessEvaluationResult AccessResult() =>
        new(
            Guid.NewGuid(),
            true,
            "ALLOWED",
            [],
            "SUPERVISOR",
            new OperatorConsoleDeviceTrustResult(DeviceId, "ACTIVE", "TRUSTED", true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, true),
            DateTimeOffset.UtcNow,
            false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                null,
                DeviceId,
                ShiftId,
                null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.ReviewEvidence,
                OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
                null,
                null));

    private static OperatorConsoleStatutoryEvidenceReviewRecord Record(
        OperatorConsoleStatutoryEvidenceReviewItemRecord? item = null) =>
        new(
            DecisionId,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            "WEBPAY",
            "NOT_DECIDED",
            "PENDING_REVIEW",
            true,
            true,
            SetId,
            SetReference,
            "OPEN",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            1,
            [item ?? Item()]);

    private static OperatorConsoleStatutoryEvidenceReviewItemRecord Item() =>
        new(
            ItemId,
            ItemReference,
            "PWD_ID",
            "FRONT",
            "UPLOADED",
            "PASSED",
            "CLEAN",
            "REVIEWABLE",
            "UNBOUND",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            "image/jpeg",
            $"upload-authorization:{AuthorizationReference:D}",
            Checksum,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            2,
            AuthorizationId,
            AuthorizationReference,
            "CONSUMED",
            "private-evidence-ref",
            "internal/key",
            "image/jpeg",
            4,
            Checksum,
            "version-1",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            2,
            DateTimeOffset.UtcNow.AddMinutes(-1));

    private static OperatorConsoleStatutoryEvidencePreviewTarget Target() =>
        new(
            DecisionId,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            SetId,
            SetReference,
            1,
            ItemId,
            ItemReference,
            2,
            AuthorizationId,
            AuthorizationReference,
            2,
            "internal/key",
            "image/jpeg",
            4,
            Checksum,
            "version-1",
            CorrelationId,
            UserId);

    private sealed record Fixture(
        OperatorConsoleStatutoryEvidenceReviewService Sut,
        IOperatorConsoleAccessEvaluationService AccessService,
        IOperatorConsoleStatutoryEvidenceReviewRepository Repository,
        IStatutoryEvidenceProtectedObjectStorageAdapter Storage);
}
