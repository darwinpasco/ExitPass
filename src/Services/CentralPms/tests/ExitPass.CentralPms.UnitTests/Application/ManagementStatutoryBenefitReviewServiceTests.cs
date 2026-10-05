using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Microsoft.Extensions.Options;
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
    public async Task Detail_WhenFrozenPolicyRequiresResidency_ExposesRequirementWithoutInventingAttestation()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                GoverningPolicy = ResidentOnlyPolicy(),
                IdControlReference = "ABC1234"
            }
        };
        var service = CreateService(AllowedRepository(), canonical);

        var result = await service.GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.BeneficiaryResidencyRequired.Should().BeTrue();
        result.Value.BeneficiaryResidencySatisfied.Should().BeNull();
    }

    [Theory]
    [InlineData("2234", "2234")]
    [InlineData("12345", "*2345")]
    [InlineData("12345678", "****5678")]
    [InlineData("ABCDEFGH", "****EFGH")]
    public async Task Detail_ForPendingAuthorizedProcessor_ReturnsFullReferenceAndSafeMask(
        string authoritativeReference,
        string expectedMask)
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                IdControlReference = authoritativeReference,
                MaskedIdReference = "legacy-mask"
            }
        };

        var result = await CreateService(AllowedRepository(idControlReference: authoritativeReference), canonical)
            .GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.IdControlReference.Should().Be(authoritativeReference);
        result.Value!.MaskedIdReference.Should().Be(expectedMask);
        result.Value.HasAuthoritativeIdControlReference.Should().BeTrue();
    }

    [Fact]
    public async Task Detail_ForFinalizedRequest_DoesNotReturnFullReference()
    {
        const string authoritativeReference = "12345678";
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail(status: "APPROVED", reviewerDecision: "APPROVE") with
            {
                IdControlReference = authoritativeReference,
                MaskedIdReference = "legacy-mask"
            }
        };

        var result = await CreateService(AllowedRepository(idControlReference: authoritativeReference), canonical)
            .GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.IdControlReference.Should().BeNull();
        result.Value.MaskedIdReference.Should().Be("****5678");
        result.Value.HasAuthoritativeIdControlReference.Should().BeTrue();
    }

    [Fact]
    public async Task Detail_WhenOnlyHistoricalMaskExists_ReportsNoAuthoritativeReference()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                IdControlReference = null,
                MaskedIdReference = "****5678"
            }
        };

        var result = await CreateService(AllowedRepository(), canonical)
            .GetAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        result.Value!.MaskedIdReference.Should().Be("****5678");
        result.Value.HasAuthoritativeIdControlReference.Should().BeFalse();
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
            Detail = Detail() with
            {
                GoverningPolicy = ResidentOnlyPolicy(),
                IdControlReference = "ABC1234"
            }
        };
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var service = CreateService(repository, canonical, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        await facade.Received(1).SubmitAsync(
            Arg.Is<StatutoryDiscountDecisionCommand>(application =>
                application.ApplyPayableBasis &&
                application.ParkingSessionId == ParkingSessionReference &&
                application.ReviewerAttestation &&
                application.BeneficiaryResidencySatisfied == true &&
                application.IdControlReference == "ABC1234" &&
                application.IdempotencyKey == $"management-review-auto-apply:{DecisionReference:N}" &&
                application.ServiceChannelCaller!.SourceChannel == "WEBPAY"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("APPROVE")]
    [InlineData("REJECT")]
    public async Task Decision_RequiredEvidenceNotReviewable_DoesNotReachDecisionService(string action)
    {
        var evidenceReview = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        evidenceReview.ReadAuthorizedAsync(DecisionReference, Arg.Any<StatutoryEvidenceAuthorizedReviewContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthoritativeEvidence() with { Items = [] });
        var decisions = new FakeDecisionService();
        var service = CreateService(AllowedRepository(), decisions: decisions, evidenceReview: evidenceReview);

        var result = await service.DecideAsync(Actor(), Command(action, action == "REJECT" ? "NOT_ELIGIBLE" : null), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Invalid);
        result.Classification.Should().Be("STATUTORY_BENEFIT_EVIDENCE_NOT_REVIEWABLE");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Approve_RequiredPhotoStillInValidation_DoesNotReachDecisionService()
    {
        var evidence = AuthoritativeEvidence();
        var evidenceReview = Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        evidenceReview.ReadAuthorizedAsync(DecisionReference, Arg.Any<StatutoryEvidenceAuthorizedReviewContext>(), Arg.Any<CancellationToken>())
            .Returns(evidence with
            {
                Items = [evidence.Items[0] with { ReviewabilityStatus = "VALIDATION_PENDING", PreviewPermitted = false }]
            });
        var decisions = new FakeDecisionService();
        var service = CreateService(AllowedRepository(), decisions: decisions, evidenceReview: evidenceReview);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Classification.Should().Be("STATUTORY_BENEFIT_EVIDENCE_NOT_REVIEWABLE");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Approve_WhenWebPayMetadataIsMissing_UsesSafeReviewerMetadata()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "WEBPAY",
            "statutory-discounts.decision.submit.webpay");
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                IdDocumentType = null,
                IssuingAuthority = null,
                ExpiryDate = null,
                MaskedIdReference = null
            }
        };
        var decisions = new FakeDecisionService();
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var service = CreateService(repository, canonical, decisions, facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.CapturedCommand!.ReviewedDocument.Should().Be(new StatutoryDiscountServiceChannelReviewedDocument(
            "PWD_ID", "LOCAL_GOVERNMENT", new DateOnly(2027, 8, 24), null, "ABC1234"));
    }

    [Fact]
    public async Task Approve_OperatorConsoleSeniorCitizen_UsesBirthDateWithoutRequiringExpiry()
    {
        var birthDate = new DateOnly(1955, 9, 30);
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                SourceChannel = StatutoryDiscountSourceChannels.OperatorConsole,
                EntitlementType = "SENIOR_CITIZEN",
                IdDocumentType = "SENIOR_CITIZEN_ID",
                IssuingAuthority = "OSCA",
                ExpiryDate = null,
                BirthDate = birthDate,
                IdControlReference = "123467"
            }
        };
        var decisions = new FakeDecisionService();
        var command = Command("APPROVE") with
        {
            IdDocumentType = "SENIOR_CITIZEN_ID",
            IssuingAuthority = "OSCA",
            ExpiryDate = null,
            BirthDate = birthDate,
            IdControlReference = null
        };

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), command, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.CapturedCommand!.ReviewedDocument.BirthDate.Should().Be(birthDate);
        decisions.CapturedCommand.ReviewedDocument.ExpiryDate.Should().BeNull();
        decisions.CapturedCommand.ReviewedDocument.IdControlReference.Should().Be("123467");
    }

    [Theory]
    [InlineData("SENIOR CITIZEN", "SENIOR_CITIZEN_ID")]
    [InlineData("SENIOR_CITIZEN", "SENIOR_CITIZEN_ID")]
    [InlineData("SENIOR_CITIZEN_ID", "SENIOR_CITIZEN_ID")]
    [InlineData("PWD", "PWD_ID")]
    [InlineData("PWD_ID", "PWD_ID")]
    public async Task Approve_NormalizesKnownReviewedDocumentTypes(string supplied, string expected)
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { IdDocumentType = supplied }
        };
        var decisions = new FakeDecisionService();
        var command = Command("APPROVE") with { IdDocumentType = supplied };

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), command, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.CapturedCommand!.ReviewedDocument.IdDocumentType.Should().Be(expected);
    }

    [Fact]
    public async Task Approve_WithoutReviewerAttestation_FailsBeforePersistence()
    {
        var decisions = new FakeDecisionService();

        var result = await CreateService(AllowedRepository(), decisions: decisions).DecideAsync(
            Actor(), Command("APPROVE") with { ReviewerAttestation = false }, CancellationToken.None);

        result.Classification.Should().Be("STATUTORY_BENEFIT_REVIEWER_ATTESTATION_REQUIRED");
        decisions.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Approve_ResidentOnlyPolicyWithoutResidencyAttestation_FailsBeforePersistence(bool? attestation)
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { GoverningPolicy = ResidentOnlyPolicy() }
        };
        var decisions = new FakeDecisionService();

        var result = await CreateService(AllowedRepository(), canonical, decisions).DecideAsync(
            Actor(), Command("APPROVE") with { BeneficiaryResidencySatisfied = attestation }, CancellationToken.None);

        result.Classification.Should().Be("STATUTORY_BENEFIT_RESIDENCY_ATTESTATION_REQUIRED");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Reject_DoesNotRequireReviewerOrResidencyAttestation()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { GoverningPolicy = ResidentOnlyPolicy() }
        };
        var decisions = new FakeDecisionService();

        var result = await CreateService(AllowedRepository(), canonical, decisions).DecideAsync(
            Actor(), Command("REJECT", "NOT_ELIGIBLE") with
            {
                ReviewerAttestation = false,
                BeneficiaryResidencySatisfied = null
            }, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Approve_NonResidentPolicyDoesNotManufactureResidencyFact()
    {
        var repository = AllowedRepository();
        repository.AutomaticApplicationCaller = new ManagementStatutoryBenefitAutomaticApplicationCaller(
            Guid.Parse("72000000-0000-4000-8000-000000000401"),
            "WEBPAY",
            "WEBPAY",
            "statutory-discounts.decision.submit.webpay");
        var facade = Substitute.For<IStatutoryDiscountDecisionFacadeService>();
        var service = CreateService(repository, decisionFacade: facade);

        var result = await service.DecideAsync(Actor(), Command("APPROVE"), CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        await facade.Received(1).SubmitAsync(
            Arg.Is<StatutoryDiscountDecisionCommand>(application =>
                application.ReviewerAttestation &&
                application.BeneficiaryResidencySatisfied == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_WhenIdReplacementIsOmitted_RetainsExistingAuthoritativeValue()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { IdControlReference = "124553456" }
        };
        var decisions = new FakeDecisionService();
        var command = Command("APPROVE") with
        {
            ExpiryDate = null,
            IdControlReference = null
        };

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), command, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.CapturedCommand!.ReviewedDocument.ExpiryDate.Should().Be(new DateOnly(2027, 8, 24));
        decisions.CapturedCommand.ReviewedDocument.IdControlReference.Should().Be("124553456");
    }

    [Fact]
    public async Task Approve_WithoutExistingOrReviewerMetadata_FailsClosed()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { IdDocumentType = null, IssuingAuthority = null, ExpiryDate = null, MaskedIdReference = null, IdControlReference = null }
        };
        var decisions = new FakeDecisionService();
        var command = Command("APPROVE") with { IdDocumentType = null, IssuingAuthority = null, ExpiryDate = null, IdControlReference = null };

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), command, CancellationToken.None);

        result.Classification.Should().Be("STATUTORY_BENEFIT_APPROVAL_METADATA_REQUIRED");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Approve_PreservesFourCharacterAndLongAuthoritativeReferences()
    {
        var accepted = new FakeDecisionService();
        var service = CreateService(AllowedRepository(), decisions: accepted);
        var result = await service.DecideAsync(
            Actor(), Command("APPROVE") with { IdControlReference = "8764" }, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        accepted.CapturedCommand!.ReviewedDocument.IdControlReference.Should().Be("8764");

        var longReference = new FakeDecisionService();
        result = await CreateService(AllowedRepository(), decisions: longReference).DecideAsync(
            Actor(), Command("APPROVE") with { IdControlReference = "12345678" }, CancellationToken.None);
        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        longReference.CapturedCommand!.ReviewedDocument.IdControlReference.Should().Be("12345678");
    }

    [Fact]
    public async Task Approve_DoesNotCopyAuthoritativeIdControlReferenceIntoAuditSummary()
    {
        var audit = new FakeAuditRepository();
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { GoverningPolicy = ResidentOnlyPolicy() }
        };
        var result = await CreateService(AllowedRepository(), canonical, audit: audit).DecideAsync(
            Actor(), Command("APPROVE") with { IdControlReference = "12345678" }, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        audit.Summaries.Should().Contain(summary => summary.Contains("idControlReferenceSupplied=True", StringComparison.Ordinal));
        audit.Summaries.Should().Contain(summary => summary.Contains("reviewerAttestation=True", StringComparison.Ordinal));
        audit.Summaries.Should().Contain(summary => summary.Contains("beneficiaryResidencyRequired=True", StringComparison.Ordinal));
        audit.Summaries.Should().Contain(summary => summary.Contains("beneficiaryResidencySatisfied=True", StringComparison.Ordinal));
        audit.Summaries.Should().OnlyContain(summary => !summary.Contains("12345678", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approve_RejectsUnsupportedDocumentTypeBeforePersistence()
    {
        var decisions = new FakeDecisionService();

        var result = await CreateService(AllowedRepository(), decisions: decisions).DecideAsync(
            Actor(), Command("APPROVE") with { IdDocumentType = "ARBITRARY_DOCUMENT" }, CancellationToken.None);

        result.Classification.Should().Be("INVALID_STATUTORY_BENEFIT_REVIEW_METADATA");
        decisions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Approve_RequiresAUsefulVisibleLastFourReference()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with { MaskedIdReference = "****", IdControlReference = null }
        };
        var decisions = new FakeDecisionService();
        var command = Command("APPROVE") with { IdControlReference = null };

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), command, CancellationToken.None);

        result.Classification.Should().Be("STATUTORY_BENEFIT_APPROVAL_METADATA_REQUIRED");
        decisions.Calls.Should().Be(0);
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
    public async Task Reject_RequiresOnlyReason_WhenHistoricalApprovalMetadataIsIncomplete()
    {
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                IdDocumentType = null,
                IssuingAuthority = null,
                ExpiryDate = null,
                IdControlReference = null,
                MaskedIdReference = "****5678"
            }
        };
        var decisions = new FakeDecisionService();

        var result = await CreateService(AllowedRepository(), canonical, decisions)
            .DecideAsync(Actor(), Command("REJECT", "IDENTITY_NOT_VERIFIABLE") with
            {
                IdDocumentType = null,
                IssuingAuthority = null,
                ExpiryDate = null,
                IdControlReference = null
            }, CancellationToken.None);

        result.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        decisions.Calls.Should().Be(1);
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
    public async Task OperatorConsoleEvidencePreview_StreamsLinkedRestrictedObjectAndAuditsCompletion()
    {
        var validationId = Guid.Parse("72000000-0000-4000-8000-000000000701");
        var evidenceId = Guid.Parse("72000000-0000-4000-8000-000000000702");
        const string objectReference = "operator-console/site-a/request/id-front.jpg";
        const string checksum = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var canonical = new FakeCanonicalRepository
        {
            Detail = Detail() with
            {
                StatutoryDiscountValidationId = validationId,
                SourceChannel = StatutoryDiscountSourceChannels.OperatorConsole
            }
        };
        var evidence = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>();
        evidence.ListAsync(validationId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountEvidenceListResult(
                validationId,
                EvidenceRequired: true,
                EvidenceRequiredSatisfied: true,
                RequiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
                EvidenceCount: 1,
                LatestEvidenceStatus: "RECORDED",
                Items:
                [
                    new OperatorConsoleStatutoryDiscountEvidenceMetadataResult(
                        evidenceId,
                        validationId,
                        "SENIOR_CITIZEN_ID",
                        "OPERATOR_CONSOLE_PHONE_CAMERA",
                        objectReference,
                        "****5678",
                        UserId,
                        SubmittedAt,
                        "NOT_REDACTED",
                        "RECORDED",
                        CorrelationId)
                ],
                CorrelationId));
        evidence.GetPreviewTargetAsync(validationId, evidenceId, Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountEvidencePreviewTarget(
                evidenceId,
                validationId,
                ParkingSessionReference,
                SiteA,
                SiteGroupId: Guid.Parse("72000000-0000-4000-8000-000000000703"),
                RequestedByUserId: UserId,
                "SENIOR_CITIZEN_ID",
                objectReference,
                checksum,
                "CAPTURED"));
        var storage = Substitute.For<IStatutoryEvidenceProtectedObjectStorageAdapter>();
        storage.OpenObjectContentStreamAsync(Arg.Any<StatutoryEvidenceObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StatutoryEvidenceObjectContent(
                new MemoryStream([1, 2, 3]),
                "image/jpeg",
                3,
                checksum,
                "version-1",
                "SERVER_SIDE_ENCRYPTED"));
        var audit = new FakeAuditRepository();
        var service = CreateService(
            AllowedRepository(),
            canonical,
            operatorConsoleEvidence: evidence,
            evidenceStorage: storage,
            audit: audit);

        var metadata = await service.GetEvidenceAsync(Actor(), DecisionReference, CorrelationId, CancellationToken.None);
        var preview = await service.OpenEvidencePreviewAsync(
            Actor(), DecisionReference, evidenceId, CorrelationId, CancellationToken.None);

        metadata.Outcome.Should().Be(ManagementStatutoryBenefitReviewOutcome.Success);
        metadata.Value!.Items.Should().ContainSingle(item =>
            item.EvidenceItemReference == evidenceId &&
            item.PreviewPermitted &&
            item.ReviewabilityStatus == "PENDING_REVIEW");
        preview.Classification.Should().Be("ACCEPTED");
        preview.Content!.ContentType.Should().Be("image/jpeg");
        preview.AuditContext.Should().NotBeNull();
        await service.RecordEvidencePreviewStreamOutcomeAsync(
            preview.AuditContext!, "COMPLETED", CancellationToken.None);
        audit.Summaries.Should().OnlyContain(summary =>
            !summary.Contains(objectReference, StringComparison.Ordinal) &&
            !summary.Contains(checksum, StringComparison.Ordinal));
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
        IOperatorConsoleStatutoryEvidenceReviewService? evidenceReview = null,
        IOperatorConsoleStatutoryDiscountEvidenceRepository? operatorConsoleEvidence = null,
        IStatutoryEvidenceProtectedObjectStorageAdapter? evidenceStorage = null,
        FakeAuditRepository? audit = null)
    {
        var review = evidenceReview ?? Substitute.For<IOperatorConsoleStatutoryEvidenceReviewService>();
        if (evidenceReview is null)
        {
            review.ReadAuthorizedAsync(DecisionReference, Arg.Any<StatutoryEvidenceAuthorizedReviewContext>(), Arg.Any<CancellationToken>())
                .Returns(AuthoritativeEvidence());
        }
        return new(
            repository,
            canonical ?? new FakeCanonicalRepository { Detail = Detail() },
            decisions ?? new FakeDecisionService(),
            decisionFacade ?? Substitute.For<IStatutoryDiscountDecisionFacadeService>(),
            review,
            operatorConsoleEvidence ?? Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>(),
            evidenceStorage ?? Substitute.For<IStatutoryEvidenceProtectedObjectStorageAdapter>(),
            Options.Create(new StatutoryEvidenceUploadOptions
            {
                BucketName = "unit-test-evidence",
                MaxContentLengthBytes = 5 * 1024 * 1024
            }),
            audit ?? new FakeAuditRepository());
    }

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

    private static FakeManagementRepository AllowedRepository(long version = 7, string? idControlReference = null) => new()
    {
        AuthorizedSites = new ManagementStatutoryBenefitAuthorizedSites(new HashSet<Guid> { SiteA, SiteB }, true),
        Metadata = new ManagementStatutoryBenefitReviewMetadata(SiteA, "SITE-A", "Site A", "Head Office Reviewer", version, idControlReference)
    };

    private static IdentityAdministrationActor Actor() => new(UserId, SessionId);

    private static ManagementStatutoryBenefitReviewQuery Query(string status) =>
        new(status, null, null, null, null, null, null, 1, 25, CorrelationId);

    private static ManagementStatutoryBenefitDecisionCommand Command(string decision, string? reason = null, long expectedVersion = 7) =>
        new(DecisionReference, decision, reason, expectedVersion, "idempotency-001", "PWD_ID", "LOCAL_GOVERNMENT", new DateOnly(2027, 8, 24), null, "ABC1234", true, true, CorrelationId);

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
        public Task<StatutoryDiscountServiceChannelValidationLinkage?> EnsureApprovedValidationLinkageAsync(Guid statutoryDiscountDecisionCommandId, Guid reviewerUserId, string? decisionReasonCode, StatutoryDiscountServiceChannelReviewedDocument reviewedDocument, Guid correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatutoryDiscountServiceChannelReviewerAuthority?> GetValidationReviewerAuthorityAsync(Guid statutoryDiscountValidationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatutoryDiscountServiceChannelReviewDetail> RecordReviewCompletionAsync(Guid statutoryDiscountDecisionCommandId, Guid reviewerUserId, Guid? operatorDeviceBindingId, Guid? operatorShiftId, Guid accessEvaluationId, string decision, string? decisionReasonCode, StatutoryDiscountServiceChannelReviewedDocument reviewedDocument, Guid correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDecisionService : IAuthorizedStatutoryBenefitDecisionService
    {
        public int Calls { get; private set; }
        public AuthorizedStatutoryBenefitDecisionCommand? CapturedCommand { get; private set; }
        public AuthorizedStatutoryBenefitDecisionResult Result { get; init; } = new(true, true, false, "APPROVED", "APPROVE", null, null, SubmittedAt.AddMinutes(5));
        public Task<AuthorizedStatutoryBenefitDecisionResult> DecideAuthorizedAsync(AuthorizedStatutoryBenefitDecisionCommand command, CancellationToken cancellationToken) { Calls++; CapturedCommand = command; return Task.FromResult(Result); }
    }

    private sealed class FakeAuditRepository : ICentralPmsRbacRepository
    {
        public List<string> Summaries { get; } = [];
        public Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ServiceIdentityIsActiveAsync(Guid serviceIdentityId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task RecordDeniedAsync(string policyName, Guid? userId, Guid? serviceIdentityId, Guid? correlationId, string requestPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordAuditEventAsync(string eventType, string eventResult, string eventReasonCode, string targetEntityType, Guid? targetEntityId, Guid? actorUserId, Guid? actorServiceIdentityId, Guid? correlationId, string summary, CancellationToken cancellationToken)
        {
            Summaries.Add(summary);
            return Task.CompletedTask;
        }
    }
}
