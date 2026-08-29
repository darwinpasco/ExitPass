using ExitPass.CentralPms.Application.StatutoryDiscounts;
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
            await repository.UpsertIntakeAsync(
                StatutoryDiscountReviewIntegrationTestSupport.IntakeCommand(
                    pending.Context,
                    pending.Decision,
                    StatutoryDiscountSourceChannels.WebPay),
                CancellationToken.None);

            await repository.RecordReviewCompletionAsync(
                completed.Decision.StatutoryDiscountDecisionCommandId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "APPROVE",
                "ELIGIBLE",
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
                seeded.Context.CorrelationId,
                CancellationToken.None);

            completed.ReviewStatus.Should().Be(expectedReviewStatus);
            completed.SourceChannel.Should().Be(sourceChannel);
            completed.StatutoryDiscountDecisionCommandId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            completed.ReviewerUserId.Should().Be(reviewerUserId);
            completed.ReviewerAccessEvaluationId.Should().Be(accessEvaluationId);
            completed.ReviewerDecision.Should().Be(decision);
            completed.EvidenceReferences
                .Select(evidence => evidence.ReferenceNumberMasked)
                .Where(masked => masked is not null)
                .Should()
                .OnlyContain(masked => masked!.Contains("****"));
            completed.MaskedIdReference.Should().Contain("****");
            completed.MaskedIdReference.Should().NotContain("123456789");
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
                seeded.Context.CorrelationId,
                CancellationToken.None);
            var linkage = await repository.EnsureApprovedValidationLinkageAsync(
                seeded.Decision.StatutoryDiscountDecisionCommandId,
                reviewerUserId,
                "ELIGIBLE",
                seeded.Context.CorrelationId,
                CancellationToken.None);

            linkage.Should().NotBeNull();
            var authority = await repository.GetValidationReviewerAuthorityAsync(
                linkage!.StatutoryDiscountValidationId,
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
