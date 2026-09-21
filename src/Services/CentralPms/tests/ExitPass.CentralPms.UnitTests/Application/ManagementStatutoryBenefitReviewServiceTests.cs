using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementStatutoryBenefitReviewServiceTests
{
    private static readonly Guid UserId = Guid.Parse("72000000-0000-4000-8000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("72000000-0000-4000-8000-000000000002");
    private static readonly Guid SiteA = Guid.Parse("72000000-0000-4000-8000-000000000101");
    private static readonly Guid SiteB = Guid.Parse("72000000-0000-4000-8000-000000000102");
    private static readonly Guid SiteC = Guid.Parse("72000000-0000-4000-8000-000000000103");
    private static readonly Guid DecisionReference = Guid.Parse("72000000-0000-4000-8000-000000000201");
    private static readonly Guid ParkingSessionReference = Guid.Parse("72000000-0000-4000-8000-000000000203");
    private static readonly Guid CorrelationId = Guid.Parse("72000000-0000-4000-8000-000000000301");
    private static readonly DateTimeOffset SubmittedAt = DateTimeOffset.Parse("2026-08-24T01:00:00Z");

    [Fact]
    public async Task List_UsesAssignedEnterpriseSiteSetAndNormalizesPendingFilter()
    {
        var repository = new FakeManagementRepository { AuthorizedSites = new ManagementStatutoryBenefitAuthorizedSites(new HashSet<Guid> { SiteA, SiteB }, true) };
        var service = CreateService(repository);

        var result = await service.ListAsync(Actor(), Query("PENDING"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        repository.CapturedSites.Should().BeEquivalentTo([SiteA, SiteB]);
        repository.CapturedQuery!.Status.Should().Be("PENDING_REVIEW");
    }

    [Fact]
    public async Task List_WhenRequestedSiteIsOutsideAssignedScope_ReturnsConcealedNotFound()
    {
        var repository = new FakeManagementRepository { AuthorizedSites = new ManagementStatutoryBenefitAuthorizedSites(new HashSet<Guid> { SiteA, SiteB }, false) };
        var service = CreateService(repository);

        var result = await service.ListAsync(Actor(), Query("ALL") with { SiteReference = SiteC }, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.NotFound);
        repository.ListCalls.Should().Be(0);
    }

    [Fact]
    public async Task Detail_WhenMonetaryFactsAreNotPhp_FailsClosed()
    {
        var repository = AllowedRepository(version: 7);
        var canonical = new FakeCanonicalRepository { Detail = Detail(currency: "USD") };
        var service = CreateService(repository, canonical);

        var result = await service.GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.SourceUnavailable);
        result.Classification.Should().Be("STATUTORY_BENEFIT_CURRENCY_UNSUPPORTED");
    }

    [Fact]
    public async Task Detail_WhenFrozenPolicyRequiresResidency_ExposesConfirmedResidencyAttestation()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { GoverningPolicy = ResidentOnlyPolicy() }
        };
        var service = CreateService(AllowedRepository(), canonical);

        var result = await service.GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.BeneficiaryResidencySatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Reject_RequiresReasonBeforePersistence()
    {
        var decisions = new FakeDecisionService();
        var service = CreateService(AllowedRepository(), decisions: decisions);

        var result = await service.DecideAsync(Actor(), Command("REJECT", reason: null), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Invalid);
        result.Classification.Should().Be("STATUTORY_BENEFIT_REJECTION_REASON_REQUIRED");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Decision_RequiresIndependentDecisionPermission()
    {
        var repository = AllowedRepository();
        repository.AuthorizedSites = null;
        var decisions = new FakeDecisionService();
        var service = CreateService(repository, decisions: decisions);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Forbidden);
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Decision_WhenVersionChangedToOppositeTerminalState_ReturnsConflict()
    {
        var repository = AllowedRepository(version: 8);
        var canonical = new FakeCanonicalRepository { Detail = Detail(status: "REJECTED", reviewerDecision: "REJECT") };
        var decisions = new FakeDecisionService();
        var service = CreateService(repository, canonical, decisions);

        var result = await service.DecideAsync(Actor(), Command("APPROVE", expectedVersion: 7), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Conflict);
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Decision_WhenVersionChangedToSameTerminalState_ReplaysIdempotently()
    {
        var repository = AllowedRepository(version: 8);
        var canonical = new FakeCanonicalRepository { Detail = Detail(status: "APPROVED", reviewerDecision: "APPROVE") };
        var decisions = new FakeDecisionService
        {
            Result = new AuthorizedStatutoryBenefitDecisionResult(true, true, true, "APPROVED", "APPROVE", null, null, SubmittedAt.AddMinutes(5))
        };
        var service = CreateService(repository, canonical, decisions);

        var result = await service.DecideAsync(Actor(), Command("APPROVE", expectedVersion: 7), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.AlreadyDecided.Should().BeTrue();
        result.Value.DecidedAt.Should().Be(SubmittedAt.AddMinutes(5));
        decisions.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Approve_AutomaticallyRequestsCanonicalPayableBasisApplicationWithStableServerKey()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "WEBPAY",
            "statutory-discounts.decision.submit.webpay");
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { GoverningPolicy = ResidentOnlyPolicy() }
        };
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var service = CreateService(repository, canonical, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        await facade.Received(1).SubmitAsync(
            Arg.Is<StatutoryDiscountDecisionCommand>(application =>
                application.ApplyPayableBasis &&
                application.ParkingSessionId == ParkingSessionReference &&
                application.BeneficiaryResidencySatisfied == true &&
                application.IdempotencyKey == $"management-review-auto-apply:{DecisionReference:N}" &&
                application.ServiceChannelCaller!.SourceChannel == "WEBPAY"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_DoesNotRequestPayableBasisApplication()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "CENTRAL_PMS",
            "statutory-discounts.decision.submit.webpay");
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var service = CreateService(repository, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("REJECT", "NOT_ELIGIBLE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        await facade.DidNotReceiveWithAnyArgs().SubmitAsync(default!, default);
    }

    [Fact]
    public async Task Evidence_UsesAuthoritativeCurrentEvidenceRuntimeAndExposesNoStorageLocator()
    {
        var evidenceReview = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        evidenceReview.ReadAuthorizedAsync(DecisionReference, Arg.Any<StatutoryEvidenceAuthorizedReviewContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthoritativeEvidence());
        var service = CreateService(AllowedRepository(), evidenceReview: evidenceReview);

        var result = await service.GetEvidenceAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].EvidenceItemReference.Should().NotBeNull();
        result.Value.Items[0].ReviewabilityStatus.Should().Be("REVIEWABLE");
        result.Value.Items[0].PreviewPermitted.Should().BeTrue();
        result.Value.Items[0].GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
        await evidenceReview.Received(1).ReadAuthorizedAsync(
            DecisionReference,
            Arg.Is<StatutoryEvidenceAuthorizedReviewContext>(context =>
                context.SiteId == SiteA &&
                context.ExpectedDecisionSourceChannel == "WEBPAY" &&
                context.Actor.UserId == Actor().UserId &&
                context.Actor.SourceChannel == "CENTRAL_PMS"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvidencePreview_UsesCentralPmsMediatedAuditChannelForAuthorizedHumanReviewer()
    {
        var itemReference = Guid.Parse("72000000-0000-4000-8000-000000000502");
        var evidenceReview = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        evidenceReview.OpenAuthorizedPreviewAsync(
                DecisionReference,
                itemReference,
                Arg.Any<StatutoryEvidenceAuthorizedReviewContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryEvidencePreviewResult(
                "REJECTED", "PREVIEW_UNAVAILABLE", false, CorrelationId, null, null));
        var service = CreateService(AllowedRepository(), evidenceReview: evidenceReview);

        await service.OpenEvidencePreviewAsync(
            Actor(), DecisionReference, itemReference, CorrelationId, CancellationToken.None);

        await evidenceReview.Received(1).OpenAuthorizedPreviewAsync(
            DecisionReference,
            itemReference,
            Arg.Is<StatutoryEvidenceAuthorizedReviewContext>(context =>
                context.SiteId == SiteA &&
                context.ExpectedDecisionSourceChannel == "WEBPAY" &&
                context.Actor.UserId == Actor().UserId &&
                context.Actor.SourceChannel == "CENTRAL_PMS"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvidencePreview_WhenReviewerSiteScopeDoesNotMatch_IsConcealedBeforeEvidenceRead()
    {
        var repository = AllowedRepository();
        repository.AuthorizedSites = new ManagementStatutoryBenefitAuthorizedSites(new HashSet<Guid> { SiteB }, false);
        var evidenceReview = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        var service = CreateService(repository, evidenceReview: evidenceReview);

        var result = await service.OpenEvidencePreviewAsync(
            Actor(),
            DecisionReference,
            Guid.Parse("72000000-0000-4000-8000-000000000502"),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("NOT_FOUND");
        await evidenceReview.DidNotReceiveWithAnyArgs().OpenAuthorizedPreviewAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Approve_WhenApplicationAttemptFails_ReturnsCanonicalRetryableApplicationState()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "WEBPAY",
            "statutory-discounts.decision.submit.webpay");
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        facade.SubmitAsync(Arg.Any<StatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<StatutoryDiscountDecisionResult>>(_ => throw new TimeoutException("transient"));
        facade.GetAsync(DecisionReference, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(DecisionResult(StatutoryDiscountApplicationStageStatuses.FailedRetryable, retryable: true));
        var service = CreateService(repository, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.FailedRetryable);
        result.Value.ApplicationRetryable.Should().BeTrue();
        result.Value.PayableBasisReady.Should().BeFalse();
    }

    [Fact]
    public async Task Approve_WhenAutomaticApplicationAuthorizationFails_DoesNotReportNotRequested()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "WEBPAY",
            "statutory-discounts.decision.submit.webpay");
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        facade.SubmitAsync(Arg.Any<StatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<StatutoryDiscountDecisionResult>>(_ => throw new StatutoryDiscountDecisionRejectedException(
                "ACCESS_DENIED",
                "Service-channel application authorization was denied."));
        facade.GetAsync(DecisionReference, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(DecisionResult(StatutoryDiscountApplicationStageStatuses.NotRequested, retryable: false));
        var service = CreateService(repository, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.FailedNonRetryable);
        result.Value.ApplicationRetryable.Should().BeFalse();
        result.Value.ApplicationRecoveryAction.Should().Be(StatutoryDiscountDecisionRecoveryActions.DoNotRetry);
    }

    private static ManagementStatutoryBenefitReviewService CreateService(
        FakeManagementRepository repository,
        FakeCanonicalRepository? canonical = null,
        FakeDecisionService? decisions = null,
        IStatutoryDiscountDecisionFacadeService? decisionFacade = null,
        IOperatorConsoleStatutoryEvidenceReviewService? evidenceReview = null) =>
        new(
            repository,
            canonical ?? new FakeCanonicalRepository { Detail = Detail() },
            decisions ?? new FakeDecisionService(),
            decisionFacade ?? Substitute.For<IStatutoryDiscountDecisionFacadeService>(),
            evidenceReview ?? Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>(),
            new FakeAuditRepository());

    private static OperatorConsoleStatutoryEvidenceReviewResult AuthoritativeEvidence()
    {
        var now = DateTimeOffset.Parse("2026-08-24T01:01:00Z");
        return new OperatorConsoleStatutoryEvidenceReviewResult(
            DecisionReference,
            Guid.Parse("72000000-0000-4000-8000-000000000501"),
            "WEBPAY",
            "PENDING_REVIEW",
            "PENDING_REVIEW",
            true,
            true,
            "ACTIVE",
            "ACTIVE",
            "NOT_REQUESTED",
            false,
            "REPLACEMENT_ALLOWED",
            [new OperatorConsoleStatutoryEvidenceReviewItemResult(
                Guid.Parse("72000000-0000-4000-8000-000000000502"),
                "SENIOR_CITIZEN_ID",
                "ENTITLEMENT_ID_FRONT",
                "image/jpeg",
                "image/jpeg",
                128,
                "UPLOADED",
                "PASSED",
                "CLEAN",
                "REVIEWABLE",
                "BOUND",
                "ACTIVE",
                "NOT_REQUESTED",
                false,
                now,
                now,
                now,
                now,
                now,
                true,
                null)],
            CorrelationId);
    }

    private static StatutoryDiscountDecisionResult DecisionResult(string applicationStatus, bool retryable) => new(
        DecisionReference,
        Guid.Parse("72000000-0000-4000-8000-000000000202"),
        null,
        ParkingSessionReference,
        "WEBPAY",
        "PWD",
        "APPROVED",
        null,
        null,
        null,
        false,
        10_000,
        10_000,
        0,
        "PHP",
        true,
        true,
        null,
        null,
        CorrelationId,
        SubmittedAt,
        SubmittedAt.AddMinutes(5),
        null,
        null,
        null,
        "APPLICATION_RETRYABLE",
        "V2",
        ApplicationRequested: true,
        ApplicationCommandStatus: applicationStatus,
        ApplicationRetryable: retryable,
        ApplicationRecoveryAction: StatutoryDiscountDecisionRecoveryActions.RetrySameRequestWithOriginalKey);

    private static FakeManagementRepository AllowedRepository(long version = 7) => new()
    {
        AuthorizedSites = new ManagementStatutoryBenefitAuthorizedSites(new HashSet<Guid> { SiteA, SiteB }, true),
        Metadata = new ManagementStatutoryBenefitReviewMetadata(SiteA, "SITE-A", "Site A", "Head Office Reviewer", version)
    };

    private static IdentityAdministrationActor Actor() => new(UserId, SessionId);

    private static ManagementStatutoryBenefitReviewQuery Query(string status) =>
        new(status, null, null, null, null, null, null, 1, 25, CorrelationId);

    private static ManagementStatutoryBenefitDecisionCommand Command(string decision, string? reason = null, long expectedVersion = 7) =>
        new(DecisionReference, decision, reason, expectedVersion, "idempotency-001", CorrelationId);

    private static StatutoryDiscountServiceChannelReviewDetail Detail(
        string status = "PENDING_REVIEW",
        string currency = "PHP",
        string? reviewerDecision = null) => new(
            StatutoryDiscountDecisionCommandId: DecisionReference,
            StatutoryDiscountValidationId: null,
            RequestReference: Guid.Parse("72000000-0000-4000-8000-000000000202"),
            ParkingSessionId: ParkingSessionReference,
            SourceChannel: "WEBPAY",
            SiteId: SiteA,
            SiteGroupId: null,
            TicketReference: "SAFE-001",
            PlateNumber: null,
            EntitlementType: "PWD",
            CommandStatus: status == "PENDING_REVIEW" ? "AWAITING_REVIEW" : "COMPLETED",
            DecisionResultStatus: status,
            ReviewStatus: status,
            IdDocumentType: "PWD_ID",
            IssuingAuthority: "LOCAL_GOVERNMENT",
            ExpiryDate: new DateOnly(2027, 8, 24),
            MaskedIdReference: "***1234",
            EvidenceReferences: [new StatutoryDiscountServiceChannelReviewEvidenceFact("GOVERNMENT_ID", "UPLOAD", null, "***1234", "RECORDED")],
            RequesterAttestation: true,
            AttestationNotes: null,
            ReasonCode: null,
            EvidenceRequired: true,
            EvidenceRecorded: true,
            OriginalTariffSnapshotId: null,
            OriginalAmountMinorUnits: 10_000,
            VatExclusiveAmountMinorUnits: null,
            VatAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: 2_000,
            FinalPayableAmountMinorUnits: 8_000,
            Currency: currency,
            GoverningPolicy: null,
            ReviewerUserId: reviewerDecision is null ? null : UserId,
            ReviewerAccessEvaluationId: reviewerDecision is null ? null : SessionId,
            ReviewerDecision: reviewerDecision,
            ReviewerReasonCode: null,
            SubmittedAt: SubmittedAt,
            ReviewedAt: reviewerDecision is null ? null : SubmittedAt.AddMinutes(5),
            PayableBasisApplicationStatus: null,
            CorrelationId: CorrelationId);

    private static StatutoryDiscountServiceChannelReviewPolicyAuthority ResidentOnlyPolicy() => new(
        StatutoryDiscountPolicyVersionId: Guid.Parse("72000000-0000-4000-8000-000000000601"),
        JurisdictionId: Guid.Parse("72000000-0000-4000-8000-000000000602"),
        JurisdictionCode: "PARANAQUE",
        JurisdictionDisplayName: "City of Paranaque",
        PolicyCode: "PITX-SENIOR",
        PolicyVersion: "1",
        OrdinanceNumber: null,
        OrdinanceTitle: null,
        SourceVerificationStatus: "VERIFIED",
        TransactionPublicationStatus: "PUBLISHED",
        DetailedRuleVerificationStatus: "VERIFIED",
        ParkingServiceApplicability: "APPLICABLE",
        BenefitType: "FULL_FEE_EXEMPTION",
        BeneficiaryResidencyScope: "RESIDENT_ONLY",
        OfficialSourceAvailable: true,
        OrdinanceTextAvailable: true,
        OrdinanceNumberAvailable: false,
        EffectiveFrom: SubmittedAt.AddDays(-1),
        EffectiveTo: null,
        RequiredEvidenceTypes: [],
        LegalApprovabilityReason: "VERIFIED_POLICY");

    private sealed class FakeManagementRepository : IManagementStatutoryBenefitReviewRepository
    {
        public ManagementStatutoryBenefitAuthorizedSites? AuthorizedSites { get; set; }
        public ManagementStatutoryBenefitReviewMetadata? Metadata { get; init; }
        public ManagementStatutoryBenefitAutomaticApplicationCaller? AutomaticApplicationCaller { get; set; }
        public ManagementStatutoryBenefitReviewQuery? CapturedQuery { get; private set; }
        public IReadOnlySet<Guid>? CapturedSites { get; private set; }
        public int ListCalls { get; private set; }

        public Task<ManagementStatutoryBenefitAuthorizedSites?> ResolveAuthorizedSitesAsync(IdentityAdministrationActor actor, string permission, CancellationToken cancellationToken) => Task.FromResult(AuthorizedSites);
        public Task<ManagementStatutoryBenefitReviewQueue> ListAsync(ManagementStatutoryBenefitReviewQuery query, IReadOnlySet<Guid> authorizedSites, CancellationToken cancellationToken)
        {
            ListCalls++; CapturedQuery = query; CapturedSites = authorizedSites;
            return Task.FromResult(new ManagementStatutoryBenefitReviewQueue(ManagementStatutoryBenefitReviewValues.ContractVersion, [], query.Page, query.PageSize, 0, false, query.CorrelationId));
        }
        public Task<ManagementStatutoryBenefitReviewMetadata?> GetMetadataAsync(Guid decisionCommandReference, CancellationToken cancellationToken) => Task.FromResult(Metadata);
        public Task<ManagementStatutoryBenefitAutomaticApplicationCaller?> ResolveAutomaticApplicationCallerAsync(string sourceChannel, Guid siteReference, CancellationToken cancellationToken) => Task.FromResult(AutomaticApplicationCaller);
    }

    private sealed class FakeCanonicalRepository : IStatutoryDiscountServiceChannelReviewRepository
    {
        public StatutoryDiscountServiceChannelReviewDetail? Detail { get; init; }
        public Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(Guid statutoryDiscountDecisionCommandId, Guid correlationId, CancellationToken cancellationToken) => Task.FromResult(Detail);
        public Task UpsertIntakeAsync(StatutoryDiscountServiceChannelReviewIntakeCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(StatutoryDiscountServiceChannelReviewQueueQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatutoryDiscountServiceChannelValidationLinkage?> EnsureApprovedValidationLinkageAsync(Guid statutoryDiscountDecisionCommandId, Guid reviewerUserId, string? decisionReasonCode, Guid correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatutoryDiscountServiceChannelReviewerAuthority?> GetValidationReviewerAuthorityAsync(Guid statutoryDiscountValidationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatutoryDiscountServiceChannelReviewDetail> RecordReviewCompletionAsync(Guid statutoryDiscountDecisionCommandId, Guid reviewerUserId, Guid? operatorDeviceBindingId, Guid? operatorShiftId, Guid accessEvaluationId, string decision, string? decisionReasonCode, Guid correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDecisionService : IAuthorizedStatutoryBenefitDecisionService
    {
        public int Calls { get; private set; }
        public AuthorizedStatutoryBenefitDecisionResult Result { get; init; } = new(true, true, false, "APPROVED", "APPROVE", null, null, SubmittedAt.AddMinutes(5));
        public Task<AuthorizedStatutoryBenefitDecisionResult> DecideAuthorizedAsync(AuthorizedStatutoryBenefitDecisionCommand command, CancellationToken cancellationToken) { Calls++; return Task.FromResult(Result); }
    }

    private sealed class FakeAuditRepository : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ServiceIdentityIsActiveAsync(Guid serviceIdentityId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task RecordDeniedAsync(string policyName, Guid? userId, Guid? serviceIdentityId, Guid? correlationId, string requestPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordAuditEventAsync(string eventType, string eventResult, string eventReasonCode, string targetEntityType, Guid? targetEntityId, Guid? actorUserId, Guid? actorServiceIdentityId, Guid? correlationId, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
