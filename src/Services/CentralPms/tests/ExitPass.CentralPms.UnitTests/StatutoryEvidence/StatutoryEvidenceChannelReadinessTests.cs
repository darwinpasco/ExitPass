using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class StatutoryEvidenceChannelReadinessTests
{
    private static readonly Guid DecisionCommandId = Guid.Parse("42000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("42000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData("NOT_REQUIRED", true)]
    [InlineData("REQUIRED_NOT_STARTED", false)]
    [InlineData("ITEM_CREATED", false)]
    [InlineData("UPLOAD_SESSION_AVAILABLE", false)]
    [InlineData("UPLOAD_IN_PROGRESS", false)]
    [InlineData("UPLOADED", false)]
    [InlineData("VALIDATION_PENDING", false)]
    [InlineData("VALIDATION_FAILED", false)]
    [InlineData("SCAN_PENDING", false)]
    [InlineData("SCAN_RETRYABLE", false)]
    [InlineData("SCAN_FAILED", false)]
    [InlineData("MALWARE_DETECTED", false)]
    [InlineData("NOT_REVIEWABLE", false)]
    [InlineData("REVIEWABLE", false)]
    [InlineData("REVIEW_PENDING", false)]
    [InlineData("APPROVED", false)]
    [InlineData("REJECTED", false)]
    [InlineData("APPLIED", true)]
    [InlineData("UNKNOWN_FAIL_CLOSED", false)]
    public void AptEvidenceReadiness_RequiresNotRequiredOrAppliedPayableBasis(
        string lifecycle,
        bool expectedReady)
    {
        StatutoryEvidenceChannelConstants.ReadyEvidenceStatuses.Contains(lifecycle)
            .Should().Be(expectedReady);
    }

    public static IEnumerable<object[]> CanonicalLifecycleCases()
    {
        yield return Case("REQUIRED_NOT_STARTED", null, null, null, null, null, "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_REQUIRED_NOT_STARTED", false);
        yield return Case("ITEM_CREATED", "ACTIVE", "NOT_AUTHORIZED", "NOT_STARTED", "NOT_STARTED", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_NOT_READY", false);
        yield return Case("UPLOAD_SESSION_AVAILABLE", "ACTIVE", "AUTHORIZED", "NOT_STARTED", "NOT_STARTED", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_UPLOAD_PENDING", false);
        yield return Case("UPLOAD_IN_PROGRESS", "ACTIVE", "UPLOADING", "NOT_STARTED", "NOT_STARTED", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_UPLOAD_PENDING", false);
        yield return Case("VALIDATION_PENDING", "ACTIVE", "UPLOADED", "PENDING", "NOT_STARTED", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_VALIDATION_PENDING", false);
        yield return Case("VALIDATION_FAILED", "ACTIVE", "UPLOADED", "FAILED", "NOT_STARTED", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_VALIDATION_FAILED", false);
        yield return Case("SCAN_PENDING", "ACTIVE", "UPLOADED", "PASSED", "PENDING", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_SCAN_PENDING", false);
        yield return Case("SCAN_RETRYABLE", "ACTIVE", "UPLOADED", "PASSED", "ERROR_RETRYABLE", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_SCAN_RETRYABLE", true);
        yield return Case("SCAN_FAILED", "ACTIVE", "UPLOADED", "PASSED", "ERROR_TERMINAL", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_SCAN_FAILED", false);
        yield return Case("MALWARE_DETECTED", "ACTIVE", "UPLOADED", "PASSED", "MALICIOUS", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_MALWARE_DETECTED", false);
        yield return Case("NOT_REVIEWABLE", "ACTIVE", "UPLOADED", "PASSED", "CLEAN", "NOT_REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_NOT_READY", false);
        yield return Case("REVIEWABLE", "ACTIVE", "UPLOADED", "PASSED", "CLEAN", "REVIEWABLE", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_NOT_READY", false);
        yield return Case("REVIEW_PENDING", "LOCKED_FOR_REVIEW", "UPLOADED", "PASSED", "CLEAN", "LOCKED_FOR_REVIEW", "AWAITING_REVIEW", false, "STATUTORY_EVIDENCE_REVIEW_PENDING", false);
        yield return Case("APPROVED", "LOCKED_FOR_REVIEW", "UPLOADED", "PASSED", "CLEAN", "LOCKED_FOR_REVIEW", "APPROVED", false, "STATUTORY_EVIDENCE_APPROVED_NOT_APPLIED", false);
        yield return Case("REJECTED", "LOCKED_FOR_REVIEW", "UPLOADED", "PASSED", "CLEAN", "LOCKED_FOR_REVIEW", "REJECTED", false, "STATUTORY_EVIDENCE_REJECTED", false);
        yield return Case("APPLIED", "LOCKED_FOR_REVIEW", "UPLOADED", "PASSED", "CLEAN", "LOCKED_FOR_REVIEW", "APPLIED_PAYABLE_BASIS", true, null, false);
    }

    [Theory]
    [MemberData(nameof(CanonicalLifecycleCases))]
    public async Task GetAptEvidenceReadiness_MapsDurableCanonicalStateAndFailsClosedUntilApplied(
        string expectedLifecycle,
        string? setStatus,
        string? uploadStatus,
        string? validationStatus,
        string? scanStatus,
        string? reviewabilityStatus,
        string decisionStatus,
        bool payableBasisReady,
        string? expectedBlockingReason,
        bool expectedRetryable)
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        repository.GetEvidenceSetByDecisionCommandIdAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(setStatus is null
                ? null
                : EvidenceSet(setStatus, uploadStatus!, validationStatus!, scanStatus!, reviewabilityStatus!));
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        decisionService.GetAsync(DecisionCommandId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(Decision(decisionStatus, evidenceRequired: true, payableBasisReady));
        var service = CreateService(repository, decisionService);

        var readiness = await service.GetAptEvidenceReadinessAsync(
            DecisionCommandId,
            Actor(),
            CorrelationId,
            CancellationToken.None);

        readiness.Classification.Should().Be(expectedLifecycle);
        readiness.Ready.Should().Be(payableBasisReady);
        readiness.Retryable.Should().Be(expectedRetryable);
        readiness.BlockingReasonCode.Should().Be(expectedBlockingReason);
    }

    [Fact]
    public async Task GetAptEvidenceReadiness_WhenCanonicalContextMissing_FailsClosed()
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        decisionService.GetAsync(DecisionCommandId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns((StatutoryDiscountDecisionResult?)null);

        var readiness = await CreateService(repository, decisionService).GetAptEvidenceReadinessAsync(
            DecisionCommandId,
            Actor(),
            CorrelationId,
            CancellationToken.None);

        readiness.Classification.Should().Be("UNKNOWN_FAIL_CLOSED");
        readiness.Ready.Should().BeFalse();
        readiness.BlockingReasonCode.Should().Be("STATUTORY_EVIDENCE_CONTEXT_UNAVAILABLE");
    }

    [Fact]
    public async Task GetAptEvidenceReadiness_WhenEvidenceNotRequired_IsReadyWithoutMetadata()
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        decisionService.GetAsync(DecisionCommandId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(Decision("APPROVED", evidenceRequired: false, payableBasisReady: false));

        var readiness = await CreateService(repository, decisionService).GetAptEvidenceReadinessAsync(
            DecisionCommandId,
            Actor(),
            CorrelationId,
            CancellationToken.None);

        readiness.Classification.Should().Be("NOT_REQUIRED");
        readiness.Ready.Should().BeTrue();
        await repository.DidNotReceive().GetEvidenceSetByDecisionCommandIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStatus_WhenCanonicalEvidenceOriginatedInAnotherChannel_ReturnsCanonicalState()
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var siteId = Guid.Parse("42000000-0000-0000-0000-000000000006");
        var siteGroupId = Guid.Parse("42000000-0000-0000-0000-000000000007");
        repository.GetEvidenceSetByDecisionCommandIdAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(EvidenceSet("ACTIVE", "UPLOADED", "PASSED", "CLEAN", "REVIEWABLE", StatutoryDiscountSourceChannels.OperatorConsole));
        repository.ResolveRequestBindingAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(Binding(StatutoryDiscountSourceChannels.OperatorConsole));
        repository.ActorHasScopeAsync(
                Arg.Any<StatutoryEvidenceActor>(),
                StatutoryEvidenceScopeOperations.View,
                siteId,
                siteGroupId,
                Arg.Any<CancellationToken>())
            .Returns(true);
        decisionService.GetAsync(DecisionCommandId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(Decision("AWAITING_REVIEW", evidenceRequired: true, payableBasisReady: false));

        var result = await CreateService(repository, decisionService).GetStatusAsync(
            new StatutoryEvidenceChannelStatusQuery(
                StatutoryEvidenceChannelConstants.WebPay,
                DecisionCommandId,
                null,
                CorrelationId,
                WebPayActor()),
            CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.LifecycleClassification.Should().Be("REVIEWABLE");
        result.ReadyForReview.Should().BeTrue();
        result.BlockingReasonCode.Should().BeNull();
        result.EvidenceSetReference.Should().NotBeNull();
        result.EvidenceItemReference.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatus_WhenOperatorConsoleLegacyEvidenceExists_ReturnsSubmittedStateWithoutCreatingDuplicateEvidence()
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var operatorEvidence = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>();
        var binding = Binding(StatutoryDiscountSourceChannels.OperatorConsole, hasValidation: false);
        repository.ResolveRequestBindingAsync(DecisionCommandId, Arg.Any<CancellationToken>()).Returns(binding);
        repository.ActorHasScopeAsync(
                Arg.Any<StatutoryEvidenceActor>(),
                StatutoryEvidenceScopeOperations.View,
                binding.SiteId,
                binding.SiteGroupId,
                Arg.Any<CancellationToken>())
            .Returns(true);
        decisionService.GetAsync(DecisionCommandId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(Decision("AWAITING_REVIEW", evidenceRequired: true, payableBasisReady: false));
        operatorEvidence.ListAsync(binding.RequestReference, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(LegacyOperatorEvidence(binding.RequestReference));

        var result = await CreateService(repository, decisionService, operatorEvidence).GetStatusAsync(
            new StatutoryEvidenceChannelStatusQuery(
                StatutoryEvidenceChannelConstants.WebPay,
                DecisionCommandId,
                null,
                CorrelationId,
                WebPayActor()),
            CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.LifecycleClassification.Should().Be("REVIEWABLE");
        result.ReadyForReview.Should().BeTrue();
        result.BlockingReasonCode.Should().BeNull();
        result.ReplacementPosture.Should().Be("REPLACEMENT_NOT_ALLOWED");
        result.EvidenceSetReference.Should().BeNull();
        result.EvidenceItemReference.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_WhenActorDoesNotHaveRequestSiteScope_FailsClosed()
    {
        var repository = Substitute.For<IStatutoryEvidenceMetadataRepository>();
        var decisionService = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        repository.GetEvidenceSetByDecisionCommandIdAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(EvidenceSet("ACTIVE", "UPLOADED", "PASSED", "CLEAN", "REVIEWABLE", StatutoryDiscountSourceChannels.OperatorConsole));

        var result = await CreateService(repository, decisionService).GetStatusAsync(
            new StatutoryEvidenceChannelStatusQuery(
                StatutoryEvidenceChannelConstants.WebPay,
                DecisionCommandId,
                null,
                CorrelationId,
                WebPayActor()),
            CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
        await decisionService.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static object[] Case(
        string expectedLifecycle,
        string? setStatus,
        string? uploadStatus,
        string? validationStatus,
        string? scanStatus,
        string? reviewabilityStatus,
        string decisionStatus,
        bool payableBasisReady,
        string? expectedBlockingReason,
        bool expectedRetryable) =>
        [expectedLifecycle, setStatus!, uploadStatus!, validationStatus!, scanStatus!, reviewabilityStatus!, decisionStatus, payableBasisReady, expectedBlockingReason!, expectedRetryable];

    private static StatutoryEvidenceChannelService CreateService(
        IStatutoryEvidenceMetadataRepository repository,
        IStatutoryDiscountDecisionFacadeService decisionService,
        IOperatorConsoleStatutoryDiscountEvidenceRepository? operatorEvidence = null) =>
        new(
            repository,
            Substitute.For<IStatutoryEvidenceMetadataService>(),
            Substitute.For<IStatutoryEvidenceUploadRepository>(),
            Substitute.For<IStatutoryEvidenceUploadService>(),
            Substitute.For<IStatutoryEvidenceProtectedObjectStorageAdapter>(),
            decisionService,
            Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>(),
            operatorEvidence ?? Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>(),
            new StatutoryEvidenceChannelOptions(),
            new StatutoryEvidenceUploadOptions { MaxContentLengthBytes = 1_048_576 },
            new StatutoryEvidenceScanWorkerOptions());

    private static StatutoryEvidenceSetReadModel EvidenceSet(
        string setStatus,
        string uploadStatus,
        string validationStatus,
        string scanStatus,
        string reviewabilityStatus,
        string sourceChannel = StatutoryEvidenceChannelConstants.AssistedPaymentTerminal)
    {
        var now = DateTimeOffset.Parse("2026-08-05T00:00:00Z");
        var item = new StatutoryEvidenceItemReadModel(
            Guid.Parse("42000000-0000-0000-0000-000000000003"),
            "SENIOR_CITIZEN_ID",
            "SINGLE_DOCUMENT",
            uploadStatus,
            validationStatus,
            scanStatus,
            reviewabilityStatus,
            "BOUND",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            "IMAGE_JPEG",
            "image/jpeg",
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            validationStatus == "FAILED" ? "FAILED" : null,
            scanStatus is "MALICIOUS" or "ERROR_RETRYABLE" or "ERROR_TERMINAL" ? scanStatus : null,
            now,
            now);
        return new StatutoryEvidenceSetReadModel(
            Guid.Parse("42000000-0000-0000-0000-000000000004"),
            DecisionCommandId,
            null,
            Guid.Parse("42000000-0000-0000-0000-000000000005"),
            Guid.Parse("42000000-0000-0000-0000-000000000006"),
            Guid.Parse("42000000-0000-0000-0000-000000000007"),
            "SENIOR_CITIZEN",
            sourceChannel,
            setStatus,
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            "1",
            "LOCAL_TEST",
            "1",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            null,
            CorrelationId,
            now,
            now,
            [item]);
    }

    private static StatutoryDiscountDecisionResult Decision(
        string decisionStatus,
        bool evidenceRequired,
        bool payableBasisReady) =>
        new(
            DecisionCommandId,
            Guid.Parse("42000000-0000-0000-0000-000000000008"),
            null,
            Guid.Parse("42000000-0000-0000-0000-000000000005"),
            StatutoryEvidenceChannelConstants.AssistedPaymentTerminal,
            "SENIOR_CITIZEN",
            decisionStatus,
            "NATIONAL_RA9994",
            null,
            null,
            false,
            10_000,
            2_000,
            payableBasisReady ? 8_000 : null,
            "PHP",
            evidenceRequired,
            false,
            null,
            null,
            CorrelationId,
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            null,
            null,
            null,
            null,
            "ACCEPTED",
            StatutoryDiscountDecisionSemanticHash.SourceVersion,
            PayableBasisReady: payableBasisReady);

    private static StatutoryEvidenceActor Actor() =>
        new(
            null,
            Guid.Parse("42000000-0000-0000-0000-000000000009"),
            StatutoryEvidenceChannelConstants.AssistedPaymentTerminal);

    private static StatutoryEvidenceActor WebPayActor() =>
        new(
            null,
            Guid.Parse("42000000-0000-0000-0000-000000000010"),
            StatutoryEvidenceChannelConstants.WebPay);

    private static StatutoryEvidenceDurableRequestBinding Binding(string sourceChannel, bool hasValidation = true) =>
        new(
            DecisionCommandId,
            hasValidation ? Guid.Parse("42000000-0000-0000-0000-000000000011") : null,
            Guid.Parse("42000000-0000-0000-0000-000000000012"),
            Guid.Parse("42000000-0000-0000-0000-000000000005"),
            Guid.Parse("42000000-0000-0000-0000-000000000006"),
            Guid.Parse("42000000-0000-0000-0000-000000000007"),
            "SENIOR_CITIZEN",
            sourceChannel);

    private static OperatorConsoleStatutoryDiscountEvidenceListResult LegacyOperatorEvidence(Guid validationId) =>
        new(
            validationId,
            EvidenceRequired: true,
            EvidenceRequiredSatisfied: true,
            RequiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
            EvidenceCount: 1,
            LatestEvidenceStatus: "CAPTURED",
            Items:
            [
                new OperatorConsoleStatutoryDiscountEvidenceMetadataResult(
                    Guid.Parse("42000000-0000-0000-0000-000000000013"),
                    validationId,
                    "SENIOR_CITIZEN_ID",
                    "OPERATOR_CONSOLE_PHONE_CAMERA",
                    "protected/operator-console/evidence.jpg",
                    null,
                    Guid.Parse("42000000-0000-0000-0000-000000000014"),
                    DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
                    "NOT_REDACTED",
                    "CAPTURED",
                    CorrelationId)
            ],
            CorrelationId);
}
