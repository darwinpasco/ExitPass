using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

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
                    null,
                    null,
                    1,
                    25,
                    pending.Context.CorrelationId),
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
}
