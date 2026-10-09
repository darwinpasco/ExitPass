using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryDiscountServiceChannelReviewRepositoryTests
{
    [Fact]
    public async Task GetAsync_WhenReviewHasNoOriginalSnapshot_UsesCanonicalParkingSessionVendorIdentity()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(GetAsync_WhenReviewHasNoOriginalSnapshot_UsesCanonicalParkingSessionVendorIdentity),
            StatutoryDiscountSourceChannels.OperatorConsole);
        try
        {
            await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
            await connection.OpenAsync();
            await using (var clearSnapshot = new NpgsqlCommand(
                """
                UPDATE operator_console.statutory_discount_service_channel_reviews
                SET original_tariff_snapshot_id = NULL
                WHERE statutory_discount_decision_command_id = @decision_id;
                """,
                connection))
            {
                clearSnapshot.Parameters.Add("decision_id", NpgsqlDbType.Uuid).Value =
                    seeded.Decision.StatutoryDiscountDecisionCommandId;
                (await clearSnapshot.ExecuteNonQueryAsync()).Should().Be(1);
            }

            Guid vendorSystemId;
            await using (var readVendor = new NpgsqlCommand(
                "SELECT vendor_system_id FROM core.parking_sessions WHERE parking_session_id = @parking_session_id;",
                connection))
            {
                readVendor.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = seeded.Context.ParkingSessionId;
                vendorSystemId = (Guid)(await readVendor.ExecuteScalarAsync())!;
            }

            var detail = await StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository().GetAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                seeded.Context.CorrelationId,
                CancellationToken.None);

            detail.Should().NotBeNull();
            detail!.OriginalTariffSnapshotId.Should().BeNull();
            detail.VendorSystemId.Should().Be(vendorSystemId);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task ManagementQueue_HidesEvidencePendingWebPayRequestUntilCurrentEvidenceIsReviewable()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ManagementQueue_HidesEvidencePendingWebPayRequestUntilCurrentEvidenceIsReviewable),
            StatutoryDiscountSourceChannels.WebPay);
        try
        {
            var repository = new PostgresManagementStatutoryBenefitReviewRepository(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
            var query = new ManagementStatutoryBenefitReviewQuery(
                "PENDING_REVIEW", seeded.Context.SiteId, "WEBPAY", null, null, null,
                seeded.Context.ParkingSessionId.ToString(), 1, 25, seeded.Context.CorrelationId);

            var pending = await repository.ListAsync(query, new HashSet<Guid> { seeded.Context.SiteId }, CancellationToken.None);
            pending.Items.Should().NotContain(item => item.DecisionCommandReference == seeded.Decision.StatutoryDiscountDecisionCommandId);

            await ReplaceIntakeEvidenceWithCanonicalReviewableEvidenceAsync(seeded, Guid.NewGuid());
            var reviewable = await repository.ListAsync(query, new HashSet<Guid> { seeded.Context.SiteId }, CancellationToken.None);
            reviewable.Items.Should().ContainSingle(item => item.DecisionCommandReference == seeded.Decision.StatutoryDiscountDecisionCommandId);
        }
        finally
        {
            await DeleteCanonicalEvidenceAsync(seeded.Decision.StatutoryDiscountDecisionCommandId);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task UpsertIntake_ReplayIsIdempotent_AndListReturnsOnlyEligiblePendingRows()
    {
        var pending = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(UpsertIntake_ReplayIsIdempotent_AndListReturnsOnlyEligiblePendingRows),
            StatutoryDiscountSourceChannels.WebPay);
        var completed = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(UpsertIntake_ReplayIsIdempotent_AndListReturnsOnlyEligiblePendingRows) + "Completed",
            StatutoryDiscountSourceChannels.AssistedPaymentTerminal);

        try
        {
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();
            var intake = StatutoryDiscountReviewIntegrationTestSupport.IntakeCommand(
                pending.Context,
                pending.Decision,
                StatutoryDiscountSourceChannels.WebPay) with
            {
                IdControlReference = "12345678"
            };
            await repository.UpsertIntakeAsync(
                intake,
                CancellationToken.None);

            var pendingDetail = await repository.GetAsync(
                pending.Decision.StatutoryDiscountDecisionCommandId,
                pending.Context.CorrelationId,
                CancellationToken.None);
            pendingDetail.Should().NotBeNull();
            pendingDetail!.IdControlReference.Should().Be("12345678");
            pendingDetail.MaskedIdReference.Should().Be("SC-****-1234");

            await repository.RecordReviewCompletionAsync(
                completed.Decision.StatutoryDiscountDecisionCommandId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "APPROVE",
                "ELIGIBLE",
                ReviewedDocument(),
                completed.Context.CorrelationId,
                CancellationToken.None);
            await StatutoryDiscountReviewIntegrationTestSupport.CreateStagedService()
                .CompleteDecisionApprovedAsync(
                    completed.Decision.StatutoryDiscountDecisionCommandId,
                    statutoryDiscountValidationId: null,
                    completed.Decision.OriginalTariffSnapshotId,
                    completed.Decision.AppliedPolicyReferenceId,
                    completed.Decision.FallbackPolicyReferenceId,
                    completed.Decision.PolicyResolutionBasis,
                    completed.Decision.LocalOrdinanceApplied,
                    new StatutoryDiscountDecisionV2TariffFacts(
                        completed.Decision.GrossAmountMinorUnits,
                        completed.Decision.VatExclusiveAmountMinorUnits,
                        completed.Decision.VatAmountMinorUnits,
                        completed.Decision.StatutoryDiscountAmountMinorUnits,
                        completed.Decision.NetPayableAmountMinorUnits,
                        completed.Decision.Currency),
                    "ELIGIBLE",
                    completed.Context.CorrelationId,
                    CancellationToken.None);

            var list = await repository.ListAsync(
                new StatutoryDiscountServiceChannelReviewQueueQuery(
                    null,
                    null,
                    null,
                    null,
                    null,
                    StatutoryDiscountServiceChannelReviewStatuses.PendingReview,
                    null,
                    null,
                    null,
                    1,
                    25,
                    pending.Context.CorrelationId)
                {
                    HasGlobalScope = true
                },
                CancellationToken.None);

            list.Items.Should().ContainSingle(item =>
                item.StatutoryDiscountDecisionCommandId == pending.Decision.StatutoryDiscountDecisionCommandId &&
                item.SourceChannel == StatutoryDiscountSourceChannels.WebPay &&
                item.ReviewStatus == StatutoryDiscountServiceChannelReviewStatuses.PendingReview);
            list.Items.Should().NotContain(item => item.StatutoryDiscountDecisionCommandId == completed.Decision.StatutoryDiscountDecisionCommandId);
            (await StatutoryDiscountReviewIntegrationTestSupport.ReviewRowCountAsync(pending.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.SensitiveReviewColumnNamesAsync()).Should().BeEmpty();
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(pending.Context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(completed.Context);
        }
    }

    [Theory]
    [InlineData("APPROVE", StatutoryDiscountServiceChannelReviewStatuses.Approved, "WEBPAY")]
    [InlineData("REJECT", StatutoryDiscountServiceChannelReviewStatuses.Rejected, "ASSISTED_PAYMENT_TERMINAL")]
    public async Task RecordReviewCompletion_PersistsReviewerAttributionSeparately_AndPreservesOriginalSource(
        string decision,
        string expectedReviewStatus,
        string sourceChannel)
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(RecordReviewCompletion_PersistsReviewerAttributionSeparately_AndPreservesOriginalSource) + decision,
            sourceChannel);

        try
        {
            var reviewerUserId = Guid.NewGuid();
            var deviceBindingId = Guid.NewGuid();
            var shiftId = Guid.NewGuid();
            var accessEvaluationId = Guid.NewGuid();
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();

            var completed = await repository.RecordReviewCompletionAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                reviewerUserId,
                deviceBindingId,
                shiftId,
                accessEvaluationId,
                decision,
                decision == "APPROVE" ? "ELIGIBLE" : "DOCUMENT_INVALID",
                ReviewedDocument(),
                seeded.Context.CorrelationId,
                CancellationToken.None);

            completed.ReviewStatus.Should().Be(expectedReviewStatus);
            completed.SourceChannel.Should().Be(sourceChannel);
            completed.StatutoryDiscountDecisionCommandId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            completed.ReviewerUserId.Should().Be(reviewerUserId);
            completed.ReviewerAccessEvaluationId.Should().Be(accessEvaluationId);
            completed.ReviewerDecision.Should().Be(decision);
            completed.IdDocumentType.Should().Be("SENIOR_CITIZEN_ID");
            completed.IssuingAuthority.Should().Be("LOCAL_GOVERNMENT");
            completed.ExpiryDate.Should().Be(new DateOnly(2027, 9, 23));
            completed.BirthDate.Should().Be(new DateOnly(1955, 9, 30));
            completed.IdControlReference.Should().Be(decision == "APPROVE" ? "12345678" : null);
            completed.MaskedIdReference.Should().Be("SC-****-1234");
            completed.EvidenceReferences
                .Select(evidence => evidence.ReferenceNumberMasked)
                .Where(masked => masked is not null)
                .Should()
                .OnlyContain(masked => masked!.Contains("****"));
            completed.MaskedIdReference.Should().NotContain("123456789");

            var replayed = await repository.RecordReviewCompletionAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                Guid.NewGuid(),
                null,
                null,
                Guid.NewGuid(),
                decision,
                "REPLAY",
                new StatutoryDiscountServiceChannelReviewedDocument("PWD_ID", "OTHER_AUTHORITY", null, null, "87654321"),
                Guid.NewGuid(),
                CancellationToken.None);
            replayed.IdDocumentType.Should().Be("SENIOR_CITIZEN_ID");
            replayed.IssuingAuthority.Should().Be("LOCAL_GOVERNMENT");
            replayed.ExpiryDate.Should().Be(new DateOnly(2027, 9, 23));
            replayed.BirthDate.Should().Be(new DateOnly(1955, 9, 30));
            replayed.IdControlReference.Should().Be(decision == "APPROVE" ? "12345678" : null);
            replayed.MaskedIdReference.Should().Be("SC-****-1234");
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(seeded.Context.ParkingSessionId)).Should().Be(0);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task Detail_ReturnsSafeFacts_ForPendingAndCompletedHistoricalRows()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Detail_ReturnsSafeFacts_ForPendingAndCompletedHistoricalRows),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();
            var pending = await repository.GetAsync(seeded.Decision.StatutoryDiscountDecisionCommandId, seeded.Context.CorrelationId, CancellationToken.None);

            pending.Should().NotBeNull();
            pending!.ReviewStatus.Should().Be(StatutoryDiscountServiceChannelReviewStatuses.PendingReview);
            pending.IdDocumentType.Should().Be("SENIOR_CITIZEN_ID");
            pending.MaskedIdReference.Should().Be("SC-****-1234");
            pending.EvidenceReferences.Should().ContainSingle();
            pending.EvidenceReferences[0].StorageReference.Should().Be("evidence-ref-001");
            pending.EvidenceReferences[0].ReferenceNumberMasked.Should().Be("SC-****-1234");

            await repository.RecordReviewCompletionAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                Guid.NewGuid(),
                null,
                null,
                Guid.NewGuid(),
                "REJECT",
                "DOCUMENT_INVALID",
                ReviewedDocument(),
                seeded.Context.CorrelationId,
                CancellationToken.None);

            var completed = await repository.GetAsync(seeded.Decision.StatutoryDiscountDecisionCommandId, seeded.Context.CorrelationId, CancellationToken.None);
            completed.Should().NotBeNull();
            completed!.ReviewStatus.Should().Be(StatutoryDiscountServiceChannelReviewStatuses.Rejected);
            completed.ReviewerUserId.Should().NotBeNull();
            completed.ReviewerDecision.Should().Be("REJECT");
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task PendingReview_UsesFrozenRequiredEvidence_WhenLegacyEvidenceArrayIsEmpty()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(PendingReview_UsesFrozenRequiredEvidence_WhenLegacyEvidenceArrayIsEmpty),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            await ClearLegacyEvidenceRequirementAsync(seeded.Decision.StatutoryDiscountDecisionCommandId);
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();

            var detail = await repository.GetAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var queue = await repository.ListAsync(
                new StatutoryDiscountServiceChannelReviewQueueQuery(
                    seeded.Context.SiteId,
                    seeded.Context.SiteGroupId,
                    StatutoryDiscountSourceChannels.WebPay,
                    "SENIOR_CITIZEN",
                    seeded.Context.ParkingSessionId,
                    StatutoryDiscountServiceChannelReviewStatuses.PendingReview,
                    null,
                    null,
                    null,
                    1,
                    25,
                    seeded.Context.CorrelationId)
                {
                    HasGlobalScope = true
                },
                CancellationToken.None);

            detail.Should().NotBeNull();
            detail!.EvidenceReferences.Should().BeEmpty();
            detail.EvidenceRequired.Should().BeTrue();
            detail.GoverningPolicy.Should().NotBeNull();
            detail.GoverningPolicy!.RequiredEvidenceTypes.Should().ContainSingle(requirement =>
                requirement.EvidenceType == "SENIOR_CITIZEN_ID" &&
                requirement.RequirementStatus == "REQUIRED");
            queue.Items.Should().ContainSingle(item =>
                item.StatutoryDiscountDecisionCommandId == seeded.Decision.StatutoryDiscountDecisionCommandId &&
                item.EvidenceRequired);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task OperatorConsoleReview_UsesValidationFrozenPolicy_WhenDecisionAuthorityIsMissing()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(OperatorConsoleReview_UsesValidationFrozenPolicy_WhenDecisionAuthorityIsMissing),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            var validationId = await LinkOperatorConsoleValidationAndRemoveDecisionAuthorityAsync(seeded);
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();

            var detail = await repository.GetAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                seeded.Context.CorrelationId,
                CancellationToken.None);

            detail.Should().NotBeNull();
            detail!.StatutoryDiscountValidationId.Should().Be(validationId);
            detail.GoverningPolicy.Should().NotBeNull();
            detail.GoverningPolicy!.StatutoryDiscountPolicyVersionId
                .Should().Be(seeded.Decision.AppliedPolicyReferenceId!.Value);
            detail.GoverningPolicy.BeneficiaryResidencyScope.Should().Be("RESIDENT_ONLY");
            detail.GoverningPolicy.RequiredEvidenceTypes.Should().ContainSingle(requirement =>
                requirement.EvidenceType == "SENIOR_CITIZEN_ID" &&
                requirement.RequirementStatus == "REQUIRED");
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task OperatorConsoleReview_MaterializesDecisionAuthorityFromExactFrozenValidationIdempotently()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(OperatorConsoleReview_MaterializesDecisionAuthorityFromExactFrozenValidationIdempotently),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            var validationId = await LinkOperatorConsoleValidationAndRemoveDecisionAuthorityAsync(seeded);
            var repository = new PostgresStatutoryDiscountParkingEligibilityRepository(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);

            var first = await repository.EnsureDecisionPolicyAuthorityFromFrozenValidationAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                validationId,
                seeded.Context.ParkingSessionId,
                "SENIOR_CITIZEN",
                seeded.Context.SiteId,
                seeded.Context.SiteGroupId,
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var replay = await repository.EnsureDecisionPolicyAuthorityFromFrozenValidationAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                validationId,
                seeded.Context.ParkingSessionId,
                "SENIOR_CITIZEN",
                seeded.Context.SiteId,
                seeded.Context.SiteGroupId,
                seeded.Context.CorrelationId,
                CancellationToken.None);

            first.Established.Should().BeTrue();
            replay.Established.Should().BeTrue();
            seeded.Decision.AppliedPolicyReferenceId.Should().NotBeNull();
            first.Authority!.StatutoryDiscountPolicyVersionId.Should().Be(seeded.Decision.AppliedPolicyReferenceId!.Value);
            replay.Authority!.PolicyAuthoritySemanticHash.Should().Be(first.Authority.PolicyAuthoritySemanticHash);
            (await CountDecisionAuthoritiesAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(1);

            var detail = await StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository().GetAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                seeded.Context.CorrelationId,
                CancellationToken.None);
            detail!.GoverningPolicy!.StatutoryDiscountPolicyVersionId.Should().Be(seeded.Decision.AppliedPolicyReferenceId.Value);
            detail.GoverningPolicy.BeneficiaryResidencyScope.Should().Be("RESIDENT_ONLY");
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task OperatorConsoleReview_FrozenValidationContextMismatchFailsClosedWithoutAuthority()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(OperatorConsoleReview_FrozenValidationContextMismatchFailsClosedWithoutAuthority),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            var validationId = await LinkOperatorConsoleValidationAndRemoveDecisionAuthorityAsync(seeded);
            var repository = new PostgresStatutoryDiscountParkingEligibilityRepository(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);

            var wrongSession = await repository.EnsureDecisionPolicyAuthorityFromFrozenValidationAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                validationId,
                Guid.NewGuid(),
                "SENIOR_CITIZEN",
                seeded.Context.SiteId,
                seeded.Context.SiteGroupId,
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var wrongEntitlement = await repository.EnsureDecisionPolicyAuthorityFromFrozenValidationAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                validationId,
                seeded.Context.ParkingSessionId,
                "PWD",
                seeded.Context.SiteId,
                seeded.Context.SiteGroupId,
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var wrongScope = await repository.EnsureDecisionPolicyAuthorityFromFrozenValidationAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                validationId,
                seeded.Context.ParkingSessionId,
                "SENIOR_CITIZEN",
                Guid.NewGuid(),
                Guid.NewGuid(),
                seeded.Context.CorrelationId,
                CancellationToken.None);

            new[] { wrongSession, wrongEntitlement, wrongScope }
                .Should().OnlyContain(result =>
                    !result.Established && result.ErrorCode == "STATUTORY_DISCOUNT_POLICY_AUTHORITY_REQUIRED");
            (await CountDecisionAuthoritiesAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task ApprovedValidationReviewerAuthority_ReturnsCanonicalReviewOperatingContext()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ApprovedValidationReviewerAuthority_ReturnsCanonicalReviewOperatingContext),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            var reviewerUserId = Guid.NewGuid();
            var deviceBindingId = Guid.NewGuid();
            var shiftId = Guid.NewGuid();
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();

            await repository.RecordReviewCompletionAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                reviewerUserId,
                deviceBindingId,
                shiftId,
                Guid.NewGuid(),
                "APPROVE",
                "ELIGIBLE",
                ReviewedDocument(),
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var linkage = await repository.EnsureApprovedValidationLinkageAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                reviewerUserId,
                "ELIGIBLE",
                ReviewedDocument(),
                seeded.Context.CorrelationId,
                CancellationToken.None);

            linkage.Should().NotBeNull();
            (await ReadAuthoritativeIdControlReferenceAsync(linkage!.StatutoryDiscountValidationId))
                .Should().Be("12345678");
            (await ReadBirthDateAsync(linkage.StatutoryDiscountValidationId))
                .Should().Be(new DateOnly(1955, 9, 30));
            var authority = await repository.GetValidationReviewerAuthorityAsync(
                linkage.StatutoryDiscountValidationId,
                CancellationToken.None);

            authority.Should().Be(new StatutoryDiscountServiceChannelReviewerAuthority(
                reviewerUserId,
                deviceBindingId,
                shiftId,
                seeded.Context.SiteId,
                seeded.Context.SiteGroupId));
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task ApprovedValidationLinkage_UsesCanonicalReviewableWebPayEvidence_WhenIntakeHasNoLegacyReference()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ApprovedValidationLinkage_UsesCanonicalReviewableWebPayEvidence_WhenIntakeHasNoLegacyReference),
            StatutoryDiscountSourceChannels.WebPay);
        var evidenceItemReference = Guid.NewGuid();

        try
        {
            await ReplaceIntakeEvidenceWithCanonicalReviewableEvidenceAsync(seeded, evidenceItemReference);
            var repository = StatutoryDiscountReviewIntegrationTestSupport.CreateReviewRepository();

            var linkage = await repository.EnsureApprovedValidationLinkageAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                seeded.Context.RequestedByUserId,
                "ELIGIBLE",
                ReviewedDocument(),
                seeded.Context.CorrelationId,
                CancellationToken.None);

            linkage.Should().NotBeNull();
            (await ReadCapturedEvidenceStorageReferencesAsync(linkage!.StatutoryDiscountValidationId))
                .Should().ContainSingle($"evidence-item:{evidenceItemReference:D}");
        }
        finally
        {
            await DeleteCanonicalEvidenceAsync(seeded.Decision.StatutoryDiscountDecisionCommandId);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    private static async Task<Guid> LinkOperatorConsoleValidationAndRemoveDecisionAuthorityAsync(
        SeededServiceChannelReview seeded)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_policy_versions
               SET beneficiary_residency_scope = 'RESIDENT_ONLY'
             WHERE statutory_discount_policy_version_id = @policy_version_id;

            INSERT INTO discounts.statutory_discount_validations (
                statutory_discount_validation_id,
                parking_session_id,
                entitlement_type,
                id_document_type,
                issuing_authority,
                statutory_discount_policy_version_id,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                evidence_required,
                evidence_captured,
                requester_attestation,
                requested_at,
                correlation_id)
            VALUES (
                @validation_id,
                @parking_session_id,
                'SENIOR_CITIZEN',
                'SENIOR_CITIZEN_ID',
                'OSCA',
                @policy_version_id,
                'LOCAL_ORDINANCE_APPLIED',
                true,
                false,
                'OPERATOR_ASSISTED',
                'REQUESTED',
                true,
                true,
                true,
                now(),
                @correlation_id);

            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET statutory_discount_validation_id = @validation_id,
                   statutory_discount_policy_version_id = NULL,
                   statutory_discount_decision_policy_authority_id = NULL,
                   source_channel = 'OPERATOR_CONSOLE',
                   expiry_date = NULL,
                   birth_date = DATE '1955-09-30',
                   submitted_by_user_id = @submitted_by_user_id
             WHERE statutory_discount_decision_command_id = @decision_command_id;

            UPDATE discounts.statutory_discount_decision_commands
               SET statutory_discount_validation_id = @validation_id,
                   source_channel = 'OPERATOR_CONSOLE'
             WHERE statutory_discount_decision_command_id = @decision_command_id;

            DELETE FROM discounts.statutory_discount_decision_policy_authorities
             WHERE statutory_discount_decision_command_id = @decision_command_id;
            """;

        var validationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = seeded.Context.ParkingSessionId;
        command.Parameters.Add("policy_version_id", NpgsqlDbType.Uuid).Value = seeded.Decision.AppliedPolicyReferenceId!.Value;
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = seeded.Decision.StatutoryDiscountDecisionCommandId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = seeded.Context.CorrelationId;
        command.Parameters.Add("submitted_by_user_id", NpgsqlDbType.Uuid).Value = seeded.Context.RequestedByUserId;
        await command.ExecuteNonQueryAsync();
        return validationId;
    }

    private static async Task<long> CountDecisionAuthoritiesAsync(Guid decisionCommandId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM discounts.statutory_discount_decision_policy_authorities WHERE statutory_discount_decision_command_id = @decision_command_id;",
            connection);
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = decisionCommandId;
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task ReplaceIntakeEvidenceWithCanonicalReviewableEvidenceAsync(
        SeededServiceChannelReview seeded,
        Guid evidenceItemReference)
    {
        const string sql = """
            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET evidence_references = '[]'::jsonb
             WHERE statutory_discount_decision_command_id = @decision_command_id;

            INSERT INTO discounts.statutory_evidence_retention_policies (
                retention_class_code, retention_policy_version, policy_status, environment_scope,
                purpose_code, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @retention_class_code, 'v1', 'APPROVED_ENABLED', 'LOCAL_TEST',
                'WEBPAY_REVIEW_LINKAGE_REGRESSION', now() - interval '1 minute',
                @service_identity_id, @service_identity_id);

            WITH evidence_set AS (
                INSERT INTO discounts.statutory_evidence_sets (
                    evidence_set_reference, statutory_discount_decision_command_id, parking_session_id,
                    site_id, site_group_id, entitlement_type, source_channel, set_status,
                    required_document_profile_code, required_document_profile_version,
                    retention_class_code, retention_policy_version, correlation_id,
                    created_by_service_identity_id, updated_by_service_identity_id)
                VALUES (
                    gen_random_uuid(), @decision_command_id, @parking_session_id,
                    @site_id, @site_group_id, 'SENIOR_CITIZEN', 'WEBPAY', 'LOCKED_FOR_REVIEW',
                    'SENIOR_CITIZEN_ID', 'v1', @retention_class_code, 'v1', @correlation_id,
                    @service_identity_id, @service_identity_id)
                RETURNING statutory_evidence_set_id
            )
            INSERT INTO discounts.statutory_evidence_items (
                evidence_item_reference, statutory_evidence_set_id, document_type, item_role,
                upload_status, validation_status, scan_status, reviewability_status, binding_status,
                retention_status, deletion_status, expected_media_class, declared_content_type,
                profile_code, internal_storage_locator_ref, internal_checksum_sha256,
                validation_result_classification, scan_result_classification, uploaded_at,
                reviewable_at, correlation_id, created_by_service_identity_id, updated_by_service_identity_id)
            SELECT
                @evidence_item_reference, statutory_evidence_set_id, 'SENIOR_CITIZEN_ID', 'SINGLE_DOCUMENT',
                'UPLOADED', 'PASSED', 'CLEAN', 'REVIEWABLE', 'BOUND',
                'ACTIVE', 'NOT_REQUESTED', 'IMAGE_JPEG', 'image/jpeg',
                'SENIOR_CITIZEN_ID', 'upload-authorization:' || gen_random_uuid()::text, repeat('a', 64),
                'PASSED', 'CLEAN', now(), now(), @correlation_id,
                @service_identity_id, @service_identity_id
            FROM evidence_set;
            """;

        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = seeded.Decision.StatutoryDiscountDecisionCommandId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = seeded.Context.ParkingSessionId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = seeded.Context.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = seeded.Context.SiteGroupId;
        command.Parameters.Add("evidence_item_reference", NpgsqlDbType.Uuid).Value = evidenceItemReference;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = seeded.Context.CorrelationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = seeded.Context.RequestedByUserId;
        command.Parameters.AddWithValue("retention_class_code", $"R23_{evidenceItemReference:N}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearLegacyEvidenceRequirementAsync(Guid decisionCommandId)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_decision_commands
               SET evidence_required = false,
                   evidence_recorded = false
             WHERE statutory_discount_decision_command_id = @decision_command_id;

            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET evidence_references = '[]'::jsonb
             WHERE statutory_discount_decision_command_id = @decision_command_id;
            """;

        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = decisionCommandId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadCapturedEvidenceStorageReferencesAsync(Guid validationId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT evidence_storage_ref FROM discounts.discount_evidence_references WHERE statutory_discount_validation_id = @validation_id ORDER BY evidence_storage_ref;",
            connection);
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static StatutoryDiscountServiceChannelReviewedDocument ReviewedDocument() =>
        new("SENIOR_CITIZEN_ID", "LOCAL_GOVERNMENT", new DateOnly(2027, 9, 23), new DateOnly(1955, 9, 30), "12345678");

    private static async Task<DateOnly?> ReadBirthDateAsync(Guid validationId)
    {
        const string sql = """
            SELECT birth_date
              FROM discounts.statutory_discount_validations
             WHERE statutory_discount_validation_id = @validation_id;
            """;
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return (DateOnly?)await command.ExecuteScalarAsync();
    }

    private static async Task<string?> ReadAuthoritativeIdControlReferenceAsync(Guid validationId)
    {
        const string sql = """
            SELECT id_control_reference
              FROM discounts.statutory_discount_validations
             WHERE statutory_discount_validation_id = @validation_id;
            """;
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("validation_id", NpgsqlDbType.Uuid, validationId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task DeleteCanonicalEvidenceAsync(Guid decisionCommandId)
    {
        const string sql = """
            DELETE FROM discounts.statutory_evidence_items
             WHERE statutory_evidence_set_id IN (
                 SELECT statutory_evidence_set_id FROM discounts.statutory_evidence_sets
                  WHERE statutory_discount_decision_command_id = @decision_command_id);
            DELETE FROM discounts.statutory_evidence_sets
             WHERE statutory_discount_decision_command_id = @decision_command_id;
            DELETE FROM discounts.statutory_evidence_retention_policies
             WHERE purpose_code = 'WEBPAY_REVIEW_LINKAGE_REGRESSION';
            """;
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = decisionCommandId;
        await command.ExecuteNonQueryAsync();
    }
}
