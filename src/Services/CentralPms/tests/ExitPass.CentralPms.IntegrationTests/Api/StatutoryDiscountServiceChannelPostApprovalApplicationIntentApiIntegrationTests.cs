using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.IntegrationTests.Shared;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests
{
    private readonly ITestOutputHelper _output;

    private const string SharedDecisionEndpoint = "/v1/statutory-discounts/decisions";
    private const string SharedReadbackEndpointTemplate = "/v1/statutory-discounts/decisions/{0}";
    private const string ReviewDecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}/decision";
    private const string ReviewDetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}";
    private const string LegacyOperatorApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";
    private static readonly Guid ReviewerDeviceBindingId = Guid.Parse("9b000000-0000-0000-0000-000000000002");
    private static readonly Guid ReviewerShiftId = Guid.Parse("9b000000-0000-0000-0000-000000000003");
    private static readonly Guid AccessEvaluationId = Guid.Parse("9b000000-0000-0000-0000-000000000004");
    private static readonly Guid WebPayServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000005");
    private static readonly Guid AptServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000006");

    public StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay)]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal)]
    public async Task ServiceChannel_RealReviewMediatedApplicationFlow_AppliesOnceReplaysAndPaymentInitiationUsesAppliedSnapshot(
        string sourceChannel)
    {
        var scenarioName = nameof(ServiceChannel_RealReviewMediatedApplicationFlow_AppliesOnceReplaysAndPaymentInitiationUsesAppliedSnapshot) + sourceChannel;
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(scenarioName);
        await SeedServiceAuthorizationAsync(context);
        await StatutoryDiscountReviewIntegrationTestSupport.RemoveOriginalPayableBasisAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var serviceClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, sourceChannel);
            using var operatorClient = factory.CreateClient();
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, sourceChannel, applyPayableBasis: false, includePayableBasis: false),
                $"svc-intake-{sourceChannel}-{context.ParkingSessionId:N}",
                context.CorrelationId,
                expectedStatus: HttpStatusCode.Created);
            intake.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            intake.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            intake.ApplicationRequested.Should().BeFalse();
            (await StatutoryDiscountReviewIntegrationTestSupport.OriginalPayableBasisRowCountAsync(context)).Should().Be(0);

            var approved = await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            approved.CurrentValidationStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
            approved.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);

            var reviewDetail = await GetReviewDetailAsync(operatorClient, intake.StatutoryDiscountDecisionCommandId);
            reviewDetail.StatutoryDiscountValidationId.Should().NotBeNull();
            reviewDetail.SourceChannel.Should().Be(sourceChannel);
            reviewDetail.SessionEligibilityStatus.Should().Be("ELIGIBLE");
            reviewDetail.PayableBasisStatus.Should().Be("NOT_YET_CREATED");
            reviewDetail.PayableBasisApplicationStatus.Should().BeNull();
            reviewDetail.OriginalAmountMinorUnits.Should().BeNull();
            reviewDetail.StatutoryDiscountAmountMinorUnits.Should().BeNull();
            reviewDetail.FinalPayableAmountMinorUnits.Should().BeNull();

            var eligibilityFacts = await StatutoryDiscountReviewIntegrationTestSupport
                .ApprovedEligibilityFactsForDecisionAsync(intake.StatutoryDiscountDecisionCommandId);
            eligibilityFacts.TariffSnapshotId.Should().BeNull();
            eligibilityFacts.Currency.Should().BeNull();
            eligibilityFacts.GrossAmount.Should().BeNull();
            eligibilityFacts.StatutoryDiscountAmount.Should().BeNull();
            eligibilityFacts.NetAmountAfterDiscount.Should().BeNull();

            await StatutoryDiscountReviewIntegrationTestSupport.CreateOriginalPayableBasisAsync(context);
            (await StatutoryDiscountReviewIntegrationTestSupport.OriginalPayableBasisRowCountAsync(context)).Should().Be(1);

            var validationId = await StatutoryDiscountReviewIntegrationTestSupport.ValidationIdForDecisionAsync(intake.StatutoryDiscountDecisionCommandId);
            validationId.Should().Be(reviewDetail.StatutoryDiscountValidationId);

            var beforePaymentBoundaries = await StatutoryDiscountReviewIntegrationTestSupport.PaymentBoundaryRowCountAsync(context.ParkingSessionId);
            beforePaymentBoundaries.Should().Be(0);

            var application = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, sourceChannel, applyPayableBasis: true),
                $"svc-apply-{sourceChannel}-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                expectedStatus: HttpStatusCode.OK);
            application.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            application.StatutoryDiscountValidationId.Should().Be(validationId);
            application.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
            application.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
            application.ApplicationRequested.Should().BeTrue();
            application.ApplicationCommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
            application.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
            application.AppliedTariffSnapshotId.Should().NotBeNull();
            application.SiteId.Should().Be(context.SiteId);
            application.SiteGroupId.Should().Be(context.SiteGroupId);
            application.GrossAmountMinorUnits.Should().BeGreaterThan(0);
            application.VatExclusiveBasisAmountMinorUnits.Should().BeGreaterThan(0);
            application.VatAmountMinorUnits.Should().BeGreaterThan(0);
            application.StatutoryDiscountAmountMinorUnits.Should().BeGreaterThan(0);
            application.FinalPayableAmountMinorUnits().Should().BeGreaterThan(0);
            application.Currency.Should().Be("PHP");
            application.VatTreatment.Should().Be("VAT_EXCLUSIVE");
            application.PayableBasisReady.Should().BeTrue();
            application.PayableBasisReadinessStatus.Should().Be(StatutoryDiscountPayableBasisReadinessStatuses.PayableBasisReady);
            application.PayableBasisReadinessAction.Should().BeNull();

            var appliedReviewDetail = await GetReviewDetailAsync(operatorClient, intake.StatutoryDiscountDecisionCommandId);
            appliedReviewDetail.SessionEligibilityStatus.Should().Be("ELIGIBLE");
            appliedReviewDetail.PayableBasisStatus.Should().Be("CREATED");
            appliedReviewDetail.PayableBasisApplicationStatus.Should().Be("APPLIED");
            appliedReviewDetail.OriginalAmountMinorUnits.Should().Be(application.GrossAmountMinorUnits);
            appliedReviewDetail.StatutoryDiscountAmountMinorUnits.Should().Be(application.StatutoryDiscountAmountMinorUnits);
            appliedReviewDetail.FinalPayableAmountMinorUnits.Should().Be(application.NetPayableAmountMinorUnits);
            appliedReviewDetail.Currency.Should().Be("PHP");

            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);

            var expectedServiceIdentityId = sourceChannel == StatutoryDiscountSourceChannels.WebPay
                ? WebPayServiceIdentityId
                : AptServiceIdentityId;
            var attribution = await ReadApplicationAttributionAsync(validationId!.Value);
            attribution.ValidatedByUserId.Should().Be(
                context.RequestedByUserId,
                "the human reviewer remains the eligibility-decision authority");
            attribution.ValidationUpdatedByServiceIdentityId.Should().Be(expectedServiceIdentityId);
            attribution.ApplicationChannel.Should().Be("SYSTEM");
            attribution.AppliedByUserId.Should().BeNull();
            attribution.AppliedByServiceIdentityId.Should().Be(expectedServiceIdentityId);
            attribution.CreatedByUserId.Should().BeNull();
            attribution.CreatedByServiceIdentityId.Should().Be(expectedServiceIdentityId);
            attribution.UpdatedByUserId.Should().BeNull();
            attribution.UpdatedByServiceIdentityId.Should().Be(expectedServiceIdentityId);
            attribution.AppliedTariffCreatedByServiceIdentityId.Should().Be(expectedServiceIdentityId);

            var replay = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, sourceChannel, applyPayableBasis: true),
                $"svc-apply-{sourceChannel}-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                expectedStatus: HttpStatusCode.OK);
            replay.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(application.StatutoryDiscountPayableBasisApplicationCommandId);
            replay.AppliedTariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);

            var readback = await GetSharedReadbackAsync(serviceClient, application.StatutoryDiscountDecisionCommandId);
            readback.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(application.StatutoryDiscountPayableBasisApplicationCommandId);
            readback.ApplicationCommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
            readback.AppliedTariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId);
            readback.SiteId.Should().Be(application.SiteId);
            readback.SiteGroupId.Should().Be(application.SiteGroupId);
            readback.GrossAmountMinorUnits.Should().Be(application.GrossAmountMinorUnits);
            readback.VatExclusiveBasisAmountMinorUnits.Should().Be(application.VatExclusiveBasisAmountMinorUnits);
            readback.VatAmountMinorUnits.Should().Be(application.VatAmountMinorUnits);
            readback.StatutoryDiscountAmountMinorUnits.Should().Be(application.StatutoryDiscountAmountMinorUnits);
            readback.NetPayableAmountMinorUnits.Should().Be(application.NetPayableAmountMinorUnits);
            readback.Currency.Should().Be(application.Currency);
            readback.VatTreatment.Should().Be(application.VatTreatment);
            readback.PayableBasisReady.Should().BeTrue();
            readback.PayableBasisReadinessStatus.Should().Be(StatutoryDiscountPayableBasisReadinessStatuses.PayableBasisReady);
            readback.ZeroPayableStatutoryFinality.Should().BeNull();

            var paymentAttempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString,
                context,
                $"payment-after-statutory-application-{context.ParkingSessionId:N}",
                "service-channel-application-intent-test",
                application.AppliedTariffSnapshotId!.Value);
            paymentAttempt.TariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId!.Value);

            if (sourceChannel == StatutoryDiscountSourceChannels.WebPay)
            {
                var paymentConfirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
                    StatutoryDiscountReviewIntegrationTestSupport.ConnectionString,
                    paymentAttempt.PaymentAttemptId,
                    $"provider-statutory-webpay-{context.ParkingSessionId:N}",
                    "statutory-adjusted-digital-issuance-integration-test",
                    context.CorrelationId);
                paymentConfirmation.Should().NotBeNull();

                var posServerId = Guid.Parse("9b000000-0000-0000-0000-000000000007");
                var reader = new PostgresDigitalPaymentFiscalContextReader(
                    StatutoryDiscountReviewIntegrationTestSupport.ConnectionString,
                    Options.Create(new FiscalIssuancePosServerIntegrationOptions
                    {
                        RuntimeEnvironment = "IntegrationTest",
                        Endpoints =
                        [
                            new SitePosServerEndpointOptions
                            {
                                SiteId = context.SiteId,
                                SitePosServerId = posServerId,
                                SitePosServerRef = "IST-SITE-POS-WEBPAY",
                                Environment = "IntegrationTest",
                                Enabled = true
                            }
                        ]
                    }),
                    new PostgresTerminalCashStatutoryFiscalLinkageReader(
                        StatutoryDiscountReviewIntegrationTestSupport.ConnectionString));

                var fiscalContext = await reader.ReadAsync(
                    paymentAttempt.PaymentAttemptId,
                    paymentConfirmation!.PaymentConfirmationId,
                    context.ParkingSessionId,
                    CancellationToken.None);

                fiscalContext.SiteId.Should().Be(context.SiteId);
                fiscalContext.SiteGroupId.Should().Be(context.SiteGroupId);
                fiscalContext.SitePosServerId.Should().Be(posServerId);
                fiscalContext.TariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId!.Value);
                fiscalContext.AmountMinorUnits.Should().Be(application.FinalPayableAmountMinorUnits());
                fiscalContext.AppliedStatutoryFiscalContext.Should().NotBeNull();
                fiscalContext.AppliedStatutoryFiscalContext!.StatutoryDiscountDecisionCommandId.Should()
                    .Be(intake.StatutoryDiscountDecisionCommandId);
                fiscalContext.AppliedStatutoryFiscalContext.StatutoryDiscountPayableBasisApplicationCommandId.Should()
                    .Be(application.StatutoryDiscountPayableBasisApplicationCommandId!.Value);
                fiscalContext.AppliedStatutoryFiscalContext.SourceChannel.Should().Be(StatutoryDiscountSourceChannels.WebPay);
            }
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await CleanupFiscalIssuanceReferencesAsync(context.ParkingSessionId);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Fact]
    public async Task WebPayFullFeeExemption_AfterApproval_IssuesExitAuthorizationWithoutPaymentArtifacts()
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(WebPayFullFeeExemption_AfterApproval_IssuesExitAuthorizationWithoutPaymentArtifacts),
            benefitType: OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionBenefitType,
            discountBaseScope: OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionDiscountBaseScope,
            fullFeeExempt: true);
        await SeedServiceAuthorizationAsync(context);

        try
        {
            var zeroPayablePosEvidence = new ZeroPayablePosCallEvidence();
            using var factory = CreateFactory(context, zeroPayablePosEvidence);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"full-fee-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                HttpStatusCode.Created);

            var beforeApprovalApply = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"full-fee-before-approval-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.Created);
            beforeApprovalApply.ApplicationRequested.Should().BeFalse();
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(
                intake.StatutoryDiscountDecisionCommandId)).Should().Be(0);

            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");

            var approvedReview = await GetReviewDetailAsync(operatorClient, intake.StatutoryDiscountDecisionCommandId);
            approvedReview.GoverningPolicy.Should().NotBeNull();
            approvedReview.GoverningPolicy!.BenefitType.Should().Be(
                OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionBenefitType);

            var application = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"full-fee-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);

            application.ApplicationCommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
            application.OriginalTariffSnapshotId.Should().Be(context.TariffSnapshotId);
            application.AppliedTariffSnapshotId.Should().NotBeNull();
            application.AppliedTariffSnapshotId.Should().NotBe(context.TariffSnapshotId);
            application.GrossAmountMinorUnits.Should().BeGreaterThan(0);
            application.VatExclusiveBasisAmountMinorUnits.Should().BeGreaterThan(0);
            application.VatAmountMinorUnits.Should().BeGreaterThan(0);
            application.StatutoryDiscountAmountMinorUnits.Should().Be(application.VatExclusiveBasisAmountMinorUnits);
            application.FinalPayableAmountMinorUnits().Should().Be(0);
            (application.StatutoryDiscountAmountMinorUnits + application.VatAmountMinorUnits)
                .Should().Be(application.GrossAmountMinorUnits);
            application.PayableBasisReady.Should().BeTrue();
            application.PaymentRequired.Should().BeFalse();
            application.ExitAuthorization.Should().NotBeNull();
            var zeroPayableAuthorization = application.ExitAuthorization!;
            zeroPayableAuthorization.ParkingSessionId.Should().Be(context.ParkingSessionId);
            zeroPayableAuthorization.PaymentAttemptId.Should().BeNull();
            zeroPayableAuthorization.CompletionBasis.Should().Be("ZERO_PAYABLE_STATUTORY_FINALITY");
            zeroPayableAuthorization.PaymentRequired.Should().BeFalse();
            zeroPayableAuthorization.AuthorizationStatus.Should().Be("ISSUED");
            zeroPayableAuthorization.TariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId);
            zeroPayableAuthorization.FiscalIssuanceReferenceId.Should().NotBeNull();
            _output.WriteLine(
                "ZERO_PAYABLE_EXIT_AUTHORIZATION_ID={0}",
                zeroPayableAuthorization.ExitAuthorizationId);

            var beforeReadback = await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId);
            var finalityReadback = await GetSharedReadbackAsync(
                serviceClient,
                intake.StatutoryDiscountDecisionCommandId);
            var repeatedReadback = await GetSharedReadbackAsync(
                serviceClient,
                intake.StatutoryDiscountDecisionCommandId);

            finalityReadback.ZeroPayableStatutoryFinality.Should().NotBeNull();
            var finality = finalityReadback.ZeroPayableStatutoryFinality!;
            finality.FinalityState.Should().Be("ZERO_PAYABLE_STATUTORY_FINALITY");
            finality.ParkingSessionId.Should().Be(context.ParkingSessionId);
            finality.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            finality.StatutoryDiscountPayableBasisApplicationCommandId.Should()
                .Be(application.StatutoryDiscountPayableBasisApplicationCommandId!.Value);
            finality.StatutoryDiscountValidationId.Should().Be(application.StatutoryDiscountValidationId!.Value);
            finality.AppliedPolicyReferenceId.Should().Be(application.AppliedPolicyReferenceId!.Value);
            finality.OriginalTariffSnapshotId.Should().Be(context.TariffSnapshotId);
            finality.AppliedTariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId!.Value);
            finality.SiteId.Should().Be(context.SiteId);
            finality.SiteGroupId.Should().Be(context.SiteGroupId);
            finality.EntitlementType.Should().Be("SENIOR_CITIZEN");
            finality.BenefitType.Should().Be(
                OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionBenefitType);
            finality.OriginalAmountMinorUnits.Should().Be(application.GrossAmountMinorUnits);
            finality.StatutoryWaiverAmountMinorUnits.Should().Be(application.GrossAmountMinorUnits);
            finality.VatAmountMinorUnits.Should().Be(application.VatAmountMinorUnits);
            finality.FinalPayableAmountMinorUnits.Should().Be(0);
            finality.Currency.Should().Be("PHP");
            finality.SourceChannel.Should().Be(StatutoryDiscountSourceChannels.WebPay);
            repeatedReadback.ZeroPayableStatutoryFinality.Should().BeEquivalentTo(finality);

            finalityReadback.CompletionAuthority.Should().NotBeNull();
            var completionAuthority = finalityReadback.CompletionAuthority!;
            completionAuthority.CompletionBasis.Should().Be("ZERO_PAYABLE_STATUTORY_FINALITY");
            completionAuthority.ParkingSessionId.Should().Be(context.ParkingSessionId);
            completionAuthority.TariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId!.Value);
            completionAuthority.SiteId.Should().Be(context.SiteId);
            completionAuthority.SiteGroupId.Should().Be(context.SiteGroupId);
            completionAuthority.PaymentAttemptId.Should().BeNull();
            completionAuthority.PaymentConfirmationId.Should().BeNull();
            completionAuthority.FinalPayableAmountMinorUnits.Should().Be(0);
            completionAuthority.AuthorityState.Should().Be("ESTABLISHED");
            repeatedReadback.CompletionAuthority.Should().BeEquivalentTo(completionAuthority);

            finalityReadback.ExitAuthorizationEligibility.Should().NotBeNull();
            finalityReadback.ExitAuthorizationEligibility!.CompletionAuthorityEligible.Should().BeTrue();
            finalityReadback.ExitAuthorizationEligibility.ExitAuthorizationIssuanceAllowed.Should().BeTrue();
            finalityReadback.ExitAuthorizationEligibility.Status.Should()
                .Be("ZERO_PAYABLE_FISCAL_PREREQUISITE_SATISFIED");
            finalityReadback.ExitAuthorizationEligibility.BlockedReason.Should().BeNull();
            repeatedReadback.ExitAuthorizationEligibility.Should()
                .BeEquivalentTo(finalityReadback.ExitAuthorizationEligibility);
            finalityReadback.ZeroPayableFiscalCompletion.Should().NotBeNull();
            finalityReadback.ZeroPayableFiscalCompletion!.FiscalPrerequisiteSatisfied.Should().BeTrue();
            finalityReadback.ZeroPayableFiscalCompletion.CompletionBasis.Should()
                .Be("ZERO_PAYABLE_STATUTORY_FINALITY");
            finalityReadback.ZeroPayableFiscalCompletion.ElectronicJournalEventReference.Should()
                .Be(ZeroPayablePosCallEvidence.ElectronicJournalReference);
            repeatedReadback.ZeroPayableFiscalCompletion.Should()
                .BeEquivalentTo(finalityReadback.ZeroPayableFiscalCompletion);
            zeroPayablePosEvidence.IssueCount.Should().Be(1);
            zeroPayablePosEvidence.LastMapping.Should().NotBeNull();
            zeroPayablePosEvidence.LastMapping!.Tenders.Should().BeEmpty();
            zeroPayablePosEvidence.LastMapping.CentralPmsPaymentAttemptRef.Should().BeNull();
            zeroPayablePosEvidence.LastMapping.CentralPmsPaymentConfirmationRef.Should().BeNull();
            zeroPayablePosEvidence.LastMapping.PaymentFinalityRef.Should().BeNull();

            var afterReadback = await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId);
            afterReadback.Should().BeEquivalentTo(beforeReadback);

            var replay = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"full-fee-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);
            replay.StatutoryDiscountPayableBasisApplicationCommandId.Should()
                .Be(application.StatutoryDiscountPayableBasisApplicationCommandId);
            replay.AppliedTariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId);
            replay.ExitAuthorization.Should().NotBeNull();
            replay.ExitAuthorization!.ExitAuthorizationId.Should().Be(zeroPayableAuthorization.ExitAuthorizationId);

            var counts = await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId);
            counts.ApplicationCommandCount.Should().Be(1);
            counts.PayableBasisApplicationCount.Should().Be(1);
            counts.AppliedTariffSnapshotCount.Should().Be(1);
            counts.PaymentAttemptCount.Should().Be(0);
            counts.PaymentConfirmationCount.Should().Be(0);
            counts.ProviderSessionCount.Should().Be(0);
            counts.ProviderOutcomeCount.Should().Be(0);
            counts.TerminalCashCommandCount.Should().Be(0);
            counts.TerminalCashCommandAuditCount.Should().Be(0);
            counts.FiscalIssuanceReferenceCount.Should().Be(1);
            counts.ExitAuthorizationCount.Should().Be(1);
            counts.VendorPaymentAcknowledgmentCount.Should().Be(0);

            await AssertFiscalCompletionAncestryConstraintsAsync(context.ParkingSessionId);
            await AssertZeroPayableReaderCardinalityConstraintsAsync();
            await AssertExitAuthorizationStatutoryAncestryConstraintsAsync(
                context,
                intake.StatutoryDiscountDecisionCommandId,
                application);
            await AssertEveryPaymentAttemptStatusBlocksFinalityAsync(
                serviceClient,
                context,
                intake.StatutoryDiscountDecisionCommandId,
                application.AppliedTariffSnapshotId!.Value);
            (await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId)).PaymentAttemptCount.Should().Be(0);

            await CorruptDecisionGrossAmountAsync(intake.StatutoryDiscountDecisionCommandId);
            var inconsistentState = await GetSharedReadbackErrorAsync(
                serviceClient,
                intake.StatutoryDiscountDecisionCommandId);
            inconsistentState.ErrorCode.Should().Be("ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await CleanupFiscalIssuanceReferencesAsync(context.ParkingSessionId);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay, StatutoryDiscountSourceChannels.AssistedPaymentTerminal)]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal, StatutoryDiscountSourceChannels.WebPay)]
    public async Task ServiceChannel_CrossChannelApplicationIntent_IsDeniedWithoutCreatingApplication(
        string intakeChannel,
        string applyChannel)
    {
        var scenarioName = nameof(ServiceChannel_CrossChannelApplicationIntent_IsDeniedWithoutCreatingApplication) + intakeChannel + applyChannel;
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(scenarioName);
        await SeedServiceAuthorizationAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var intakeClient = factory.CreateClient();
            using var applyClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(intakeClient, intakeChannel);
            AddServiceHeaders(applyClient, applyChannel);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                intakeClient,
                Request(context, intakeChannel, applyPayableBasis: false),
                $"cross-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                HttpStatusCode.Created);
            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");

            using var crossChannel = await SendSharedDecisionAsync(
                applyClient,
                Request(context, applyChannel, applyPayableBasis: true),
                $"cross-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid());
            crossChannel.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(0);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(0);

            var replay = await PostSharedDecisionAsync(
                intakeClient,
                Request(context, intakeChannel, applyPayableBasis: true),
                $"cross-intake-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);

            replay.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            replay.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
            replay.AppliedTariffSnapshotId.Should().NotBeNull();
            (await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Theory]
    [InlineData("WRONG_SITE", HttpStatusCode.NotFound, "STATUTORY_DISCOUNT_DECISION_NOT_FOUND")]
    [InlineData("WRONG_AUDIENCE", HttpStatusCode.BadRequest, "ACCESS_DENIED")]
    [InlineData("INACTIVE_IDENTITY", HttpStatusCode.BadRequest, "ACCESS_DENIED")]
    public async Task ServiceChannel_ProductionAuthorizationRejectsInvalidIdentityOrSiteScope(
        string invalidation,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(ServiceChannel_ProductionAuthorizationRejectsInvalidIdentityOrSiteScope) + invalidation);
        await SeedServiceAuthorizationAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"authorization-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                HttpStatusCode.Created);
            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            await InvalidateServiceAuthorizationAsync(context, invalidation);

            using var response = await SendSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"authorization-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid());
            response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync());
            var error = await response.Content.ReadFromJsonAsync<ExitPass.CentralPms.Contracts.Common.ErrorResponse>();
            error!.ErrorCode.Should().Be(expectedErrorCode);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(
                intake.StatutoryDiscountDecisionCommandId)).Should().Be(0);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(
                context.ParkingSessionId)).Should().Be(0);
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyOperatorConsoleApplyRoute_IsRemoved_AndServiceChannelApplicationIntent_RemainsAuthoritative(
        bool attemptLegacyRouteBeforeServiceApplication)
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(LegacyOperatorConsoleApplyRoute_IsRemoved_AndServiceChannelApplicationIntent_RemainsAuthoritative) + attemptLegacyRouteBeforeServiceApplication);
        await SeedServiceAuthorizationAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"oc-converge-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                HttpStatusCode.Created);
            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            var validationId = (await StatutoryDiscountReviewIntegrationTestSupport.ValidationIdForDecisionAsync(intake.StatutoryDiscountDecisionCommandId))!.Value;

            if (attemptLegacyRouteBeforeServiceApplication)
            {
                await ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);
            }

            var serviceApplication = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"oc-converge-service-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);

            if (!attemptLegacyRouteBeforeServiceApplication)
            {
                await ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);
            }

            serviceApplication.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            serviceApplication.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
            serviceApplication.AppliedTariffSnapshotId.Should().NotBeNull();
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Fact]
    public async Task RemovedLegacyOperatorConsoleApplyRoute_DoesNotWriteWorkflowPaymentCashFiscalOrParkingRows()
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(RemovedLegacyOperatorConsoleApplyRoute_DoesNotWriteWorkflowPaymentCashFiscalOrParkingRows));
        await SeedServiceAuthorizationAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"oc-no-write-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                HttpStatusCode.Created);
            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            var validationId = (await StatutoryDiscountReviewIntegrationTestSupport.ValidationIdForDecisionAsync(
                intake.StatutoryDiscountDecisionCommandId))!.Value;

            var before = await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId);

            before.DecisionCommandCount.Should().Be(1);
            before.ReviewCount.Should().Be(1);
            before.ApplicationCommandCount.Should().Be(0);
            before.PayableBasisApplicationCount.Should().Be(0);
            before.AppliedTariffSnapshotCount.Should().Be(0);
            before.PaymentAttemptCount.Should().Be(0);
            before.PaymentConfirmationCount.Should().Be(0);
            before.TerminalCashCommandCount.Should().Be(0);
            before.TerminalCashCommandAuditCount.Should().Be(0);
            before.FiscalIssuanceReferenceCount.Should().Be(0);
            before.ParkingSessionCount.Should().Be(1);

            await ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);

            var after = await StatutoryDiscountReviewIntegrationTestSupport.WorkflowBoundaryRowCountsAsync(
                context.ParkingSessionId,
                intake.StatutoryDiscountDecisionCommandId);

            after.Should().BeEquivalentTo(before);
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Fact]
    public async Task ServiceChannelApplicationIntent_WhenDecisionNotApprovedOrLinkageMissing_DoesNotCreateApplication()
    {
        var awaitingContext = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(ServiceChannelApplicationIntent_WhenDecisionNotApprovedOrLinkageMissing_DoesNotCreateApplication) + "Awaiting");
        var rejectedContext = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(ServiceChannelApplicationIntent_WhenDecisionNotApprovedOrLinkageMissing_DoesNotCreateApplication) + "Rejected");
        var missingLinkageContext = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(ServiceChannelApplicationIntent_WhenDecisionNotApprovedOrLinkageMissing_DoesNotCreateApplication) + "MissingLinkage");
        await SeedServiceAuthorizationAsync(awaitingContext);
        await SeedServiceAuthorizationAsync(rejectedContext);
        await SeedServiceAuthorizationAsync(missingLinkageContext);

        await StatutoryDiscountReviewIntegrationTestSupport.RemoveOriginalPayableBasisAsync(awaitingContext);
        await StatutoryDiscountReviewIntegrationTestSupport.RemoveOriginalPayableBasisAsync(rejectedContext);

        try
        {
            using var factory = CreateFactory(awaitingContext);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, awaitingContext);

            var awaiting = await PostSharedDecisionAsync(
                serviceClient,
                Request(awaitingContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false, includePayableBasis: false),
                $"negative-awaiting-intake-{awaitingContext.ParkingSessionId:N}",
                awaitingContext.CorrelationId,
                HttpStatusCode.Created);
            (await StatutoryDiscountReviewIntegrationTestSupport.OriginalPayableBasisRowCountAsync(awaitingContext)).Should().Be(0);
            await StatutoryDiscountReviewIntegrationTestSupport.CreateOriginalPayableBasisAsync(awaitingContext);
            var awaitingApply = await PostSharedDecisionAsync(
                serviceClient,
                Request(awaitingContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"negative-awaiting-apply-{awaitingContext.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.Created);
            awaitingApply.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            awaitingApply.ApplicationRequested.Should().BeFalse();
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(awaiting.StatutoryDiscountDecisionCommandId)).Should().Be(0);

            var rejectedIntake = await PostSharedDecisionAsync(
                serviceClient,
                Request(rejectedContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false, includePayableBasis: false),
                $"negative-rejected-intake-{rejectedContext.ParkingSessionId:N}",
                rejectedContext.CorrelationId,
                HttpStatusCode.Created);
            AddOperatorHeaders(operatorClient, rejectedContext);
            await CompleteReviewAsync(operatorClient, rejectedContext, rejectedIntake.StatutoryDiscountDecisionCommandId, "REJECT");
            (await StatutoryDiscountReviewIntegrationTestSupport.OriginalPayableBasisRowCountAsync(rejectedContext)).Should().Be(0);
            await StatutoryDiscountReviewIntegrationTestSupport.CreateOriginalPayableBasisAsync(rejectedContext);
            using var rejectedResponse = await SendSharedDecisionAsync(
                serviceClient,
                Request(rejectedContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"negative-rejected-apply-{rejectedContext.ParkingSessionId:N}",
                Guid.NewGuid());
            rejectedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(rejectedIntake.StatutoryDiscountDecisionCommandId)).Should().Be(0);

            var missingLinkageIntake = await PostSharedDecisionAsync(
                serviceClient,
                Request(missingLinkageContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"negative-missing-linkage-intake-{missingLinkageContext.ParkingSessionId:N}",
                missingLinkageContext.CorrelationId,
                HttpStatusCode.Created);

            await StatutoryDiscountReviewIntegrationTestSupport.CreateStagedService()
                .CompleteDecisionApprovedAsync(
                    missingLinkageIntake.StatutoryDiscountDecisionCommandId,
                    statutoryDiscountValidationId: null,
                    missingLinkageContext.TariffSnapshotId,
                    appliedPolicyReferenceId: null,
                    fallbackPolicyReferenceId: null,
                    policyResolutionBasis: null,
                    localOrdinanceApplied: false,
                    new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
                    "ELIGIBLE",
                    missingLinkageContext.CorrelationId,
                    CancellationToken.None);
            using var missingLinkageResponse = await SendSharedDecisionAsync(
                serviceClient,
                Request(missingLinkageContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"negative-missing-linkage-apply-{missingLinkageContext.ParkingSessionId:N}",
                Guid.NewGuid());
            missingLinkageResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(missingLinkageIntake.StatutoryDiscountDecisionCommandId)).Should().Be(0);
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(awaitingContext);
            await CleanupServiceAssignmentsAsync(rejectedContext);
            await CleanupServiceAssignmentsAsync(missingLinkageContext);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(awaitingContext);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(rejectedContext);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(missingLinkageContext);
        }
    }

    [Fact]
    public async Task ConcurrentServiceChannelAndOperatorConsoleApplicationIntent_CreatesOneApplicationAndOneAppliedSnapshot()
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(ConcurrentServiceChannelAndOperatorConsoleApplicationIntent_CreatesOneApplicationAndOneAppliedSnapshot));
        await SeedServiceAuthorizationAsync(context);

        try
        {
            using var factory = CreateFactory(context);
            using var webPayClient = factory.CreateClient();
            using var webPayReplayClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(webPayClient, StatutoryDiscountSourceChannels.WebPay);
            AddServiceHeaders(webPayReplayClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                webPayClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"concurrent-intake-{context.ParkingSessionId:N}",
                context.CorrelationId,
                    HttpStatusCode.Created);
            await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            var validationId = (await StatutoryDiscountReviewIntegrationTestSupport.ValidationIdForDecisionAsync(intake.StatutoryDiscountDecisionCommandId))!.Value;

            var serviceApply = PostSharedDecisionAsync(
                webPayClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"concurrent-webpay-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                expectedStatus: null);
            var replayApply = PostSharedDecisionAsync(
                webPayReplayClient,
                Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                $"concurrent-webpay-replay-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                expectedStatus: null);
            var operatorApply = ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);

            var serviceResult = await serviceApply;
            var replayResult = await replayApply;
            await operatorApply;

            var readback = await GetSharedReadbackAsync(webPayClient, intake.StatutoryDiscountDecisionCommandId);
            readback.ApplicationCommandStatus.Should().Be(
                StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied,
                "concurrent service-channel application intent must converge while the legacy Operator Console route is absent instead of leaving status {0}; service result {1}, replay result {2}",
                readback.ApplicationCommandStatus,
                serviceResult.ApplicationCommandStatus,
                replayResult.ApplicationCommandStatus);

            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);

            readback.ApplicationCommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
            readback.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
        }
        finally
        {
            await CleanupServiceAssignmentsAsync(context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    private static async Task<StatutoryDiscountDecisionResponse> PostSharedDecisionAsync(
        HttpClient client,
        StatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        HttpStatusCode? expectedStatus)
    {
        using var response = await SendSharedDecisionAsync(client, request, idempotencyKey, correlationId);
        if (expectedStatus is not null)
        {
            response.StatusCode.Should().Be(expectedStatus.Value, await response.Content.ReadAsStringAsync());
        }
        else
        {
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict],
                await response.Content.ReadAsStringAsync());
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await GetSharedReadbackAsync(client, request.ParkingSessionId, request.EntitlementType);
        }

        return (await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>())!;
    }

    private static async Task<HttpResponseMessage> SendSharedDecisionAsync(
        HttpClient client,
        StatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, SharedDecisionEndpoint)
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(message);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDecisionResponse> CompleteReviewAsync(
        HttpClient client,
        PaymentTestContext context,
        Guid statutoryDiscountDecisionCommandId,
        string decision)
    {
        using var response = await client.PostAsJsonAsync(
            string.Format(ReviewDecisionEndpointTemplate, statutoryDiscountDecisionCommandId),
            new OperatorConsoleCanonicalStatutoryReviewDecisionRequest(
                decision,
                decision == "APPROVE" ? "ELIGIBLE" : "DOCUMENT_INVALID",
                ReviewerAttestation: true,
                $"review-{decision}-{statutoryDiscountDecisionCommandId:N}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>())!;
    }

    private static async Task ApplyWithOperatorConsoleAsync(
        HttpClient client,
        PaymentTestContext context,
        Guid validationId)
    {
        using var response = await client.PostAsJsonAsync(
            string.Format(LegacyOperatorApplyEndpointTemplate, validationId),
            new
            {
                userId = context.RequestedByUserId,
                operatorDeviceBindingId = ReviewerDeviceBindingId,
                siteId = context.SiteId,
                siteGroupId = context.SiteGroupId,
                operatorShiftId = ReviewerShiftId,
                originalTariffSnapshotId = context.TariffSnapshotId,
                idempotencyKey = $"operator-apply-{validationId:N}",
                correlationId = Guid.NewGuid()
            });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    private static async Task<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse> GetReviewDetailAsync(
        HttpClient client,
        Guid statutoryDiscountDecisionCommandId)
    {
        using var response = await client.GetAsync(string.Format(ReviewDetailEndpointTemplate, statutoryDiscountDecisionCommandId));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse>())!;
    }

    private static async Task<StatutoryDiscountDecisionResponse> GetSharedReadbackAsync(
        HttpClient client,
        Guid statutoryDiscountDecisionCommandId)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, string.Format(SharedReadbackEndpointTemplate, statutoryDiscountDecisionCommandId));
        message.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(message);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>())!;
    }

    private static async Task<ErrorResponse> GetSharedReadbackErrorAsync(
        HttpClient client,
        Guid statutoryDiscountDecisionCommandId)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            string.Format(SharedReadbackEndpointTemplate, statutoryDiscountDecisionCommandId));
        message.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(message);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ErrorResponse>())!;
    }

    private static async Task AssertEveryPaymentAttemptStatusBlocksFinalityAsync(
        HttpClient client,
        PaymentTestContext context,
        Guid statutoryDiscountDecisionCommandId,
        Guid appliedTariffSnapshotId)
    {
        string[] statuses =
        [
            "REQUESTED",
            "PENDING_PROVIDER",
            "PENDING_FINALIZATION",
            "CONFIRMED",
            "FAILED",
            "EXPIRED",
            "CANCELLED"
        ];

        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();

        foreach (var status in statuses)
        {
            var paymentAttemptId = Guid.NewGuid();
            await using var insert = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO core.payment_attempts (
                    payment_attempt_id,
                    parking_session_id,
                    tariff_snapshot_id,
                    idempotency_key,
                    currency_code,
                    amount,
                    attempt_status,
                    requested_at,
                    expires_at,
                    finalized_at,
                    failure_reason_code,
                    correlation_id,
                    created_at,
                    created_by_service_identity_id,
                    updated_at,
                    updated_by_service_identity_id,
                    row_version)
                VALUES (
                    @payment_attempt_id,
                    @parking_session_id,
                    @tariff_snapshot_id,
                    @idempotency_key,
                    'PHP',
                    0.00,
                    CAST(@attempt_status AS core.payment_attempt_status_enum),
                    now(),
                    now() + interval '15 minutes',
                    CASE WHEN @attempt_status IN ('CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED') THEN now() ELSE NULL END,
                    CASE WHEN @attempt_status IN ('FAILED', 'EXPIRED', 'CANCELLED') THEN 'INTEGRATION_TEST' ELSE NULL END,
                    @correlation_id,
                    now(),
                    @service_identity_id,
                    now(),
                    @service_identity_id,
                    1);
                """,
                connection);
            insert.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
            insert.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
            insert.Parameters.AddWithValue("tariff_snapshot_id", appliedTariffSnapshotId);
            insert.Parameters.AddWithValue("idempotency_key", $"zero-finality-conflict-{status}-{paymentAttemptId:N}");
            insert.Parameters.AddWithValue("attempt_status", status);
            insert.Parameters.AddWithValue("correlation_id", Guid.NewGuid());
            insert.Parameters.AddWithValue("service_identity_id", WebPayServiceIdentityId);
            await insert.ExecuteNonQueryAsync();

            try
            {
                var error = await GetSharedReadbackErrorAsync(client, statutoryDiscountDecisionCommandId);
                error.ErrorCode.Should().Be(
                    "ZERO_PAYABLE_STATUTORY_PAYMENT_CONFLICT",
                    $"a {status} attempt still proves the zero-payable tariff entered a payment flow");
            }
            finally
            {
                await using var delete = new Npgsql.NpgsqlCommand(
                    "DELETE FROM core.payment_attempts WHERE payment_attempt_id = @payment_attempt_id;",
                    connection);
                delete.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
                await delete.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task AssertZeroPayableReaderCardinalityConstraintsAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'discounts'
                      AND tablename = 'statutory_discount_payable_basis_application_commands'
                      AND indexname = 'ux_stat_discount_pba_commands__decision_command'
                      AND indexdef ILIKE 'CREATE UNIQUE INDEX%'
                      AND indexdef LIKE '%(statutory_discount_decision_command_id)%'
                ) AS application_command_is_unique,
                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'discounts'
                      AND tablename = 'statutory_discount_decision_policy_authorities'
                      AND indexname = 'pk_statutory_discount_decision_policy_authorities'
                      AND indexdef ILIKE 'CREATE UNIQUE INDEX%'
                      AND indexdef LIKE '%(statutory_discount_decision_command_id)%'
                ) AS policy_authority_is_unique;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetBoolean(0).Should().BeTrue(
            "a decision must identify at most one payable-basis application command");
        reader.GetBoolean(1).Should().BeTrue(
            "a decision must identify at most one authoritative policy-version row");
    }

    private static async Task AssertFiscalCompletionAncestryConstraintsAsync(Guid parkingSessionId)
    {
        await AssertUpdateRejectedAsync(
            "completion_authority_reference_id = NULL",
            "a fiscal issuance reference cannot omit its completion source");
        await AssertUpdateRejectedAsync(
            "completion_basis = 'PAYMENT_FINALITY'",
            "payment completion cannot retain zero-payable statutory ancestry");

        async Task AssertUpdateRejectedAsync(string assignment, string reason)
        {
            await using var connection = new Npgsql.NpgsqlConnection(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new Npgsql.NpgsqlCommand(
                $"""
                UPDATE core.fiscal_issuance_references
                SET {assignment}
                WHERE parking_session_id = @parking_session_id
                  AND completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY';
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

            var action = async () => await command.ExecuteNonQueryAsync();
            await action.Should().ThrowAsync<Npgsql.PostgresException>(reason);
            await transaction.RollbackAsync();
        }
    }

    private static async Task AssertExitAuthorizationStatutoryAncestryConstraintsAsync(
        PaymentTestContext context,
        Guid statutoryDiscountDecisionCommandId,
        StatutoryDiscountDecisionResponse application)
    {
        await AssertInsertAsync(
            completionBasis: null,
            paymentAttemptId: null,
            paymentConfirmationId: null,
            statutoryDiscountDecisionCommandId: null,
            statutoryDiscountPayableBasisApplicationCommandId: null,
            statutoryDiscountValidationId: null,
            appliedPolicyReferenceId: null);

        await AssertInsertAsync(
            completionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY",
            paymentAttemptId: Guid.NewGuid(),
            paymentConfirmationId: Guid.NewGuid(),
            statutoryDiscountDecisionCommandId,
            application.StatutoryDiscountPayableBasisApplicationCommandId,
            application.StatutoryDiscountValidationId,
            application.AppliedPolicyReferenceId);

        async Task AssertInsertAsync(
            string? completionBasis,
            Guid? paymentAttemptId,
            Guid? paymentConfirmationId,
            Guid? statutoryDiscountDecisionCommandId,
            Guid? statutoryDiscountPayableBasisApplicationCommandId,
            Guid? statutoryDiscountValidationId,
            Guid? appliedPolicyReferenceId)
        {
            await using var connection = new Npgsql.NpgsqlConnection(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO core.exit_authorizations (
                    exit_authorization_id,
                    parking_session_id,
                    payment_attempt_id,
                    payment_confirmation_id,
                    authorization_token_hash,
                    authorization_status,
                    issued_at,
                    expires_at,
                    correlation_id,
                    created_at,
                    created_by_service_identity_id,
                    updated_at,
                    updated_by_service_identity_id,
                    tariff_snapshot_id,
                    completion_basis,
                    statutory_discount_decision_command_id,
                    statutory_discount_payable_basis_application_command_id,
                    statutory_discount_validation_id,
                    applied_policy_reference_id)
                VALUES (
                    @exit_authorization_id,
                    @parking_session_id,
                    @payment_attempt_id,
                    @payment_confirmation_id,
                    repeat('a', 64),
                    'ISSUED',
                    now(),
                    now() + interval '15 minutes',
                    @correlation_id,
                    now(),
                    @service_identity_id,
                    now(),
                    @service_identity_id,
                    @tariff_snapshot_id,
                    @completion_basis,
                    @decision_command_id,
                    @application_command_id,
                    @validation_id,
                    @policy_reference_id);
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("exit_authorization_id", Guid.NewGuid());
            command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
            command.Parameters.Add("payment_attempt_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)paymentAttemptId ?? DBNull.Value;
            command.Parameters.Add("payment_confirmation_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)paymentConfirmationId ?? DBNull.Value;
            command.Parameters.AddWithValue("correlation_id", Guid.NewGuid());
            command.Parameters.AddWithValue("service_identity_id", WebPayServiceIdentityId);
            command.Parameters.AddWithValue("tariff_snapshot_id", application.AppliedTariffSnapshotId!.Value);
            command.Parameters.Add("completion_basis", NpgsqlTypes.NpgsqlDbType.Varchar).Value =
                (object?)completionBasis ?? DBNull.Value;
            command.Parameters.Add("decision_command_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)statutoryDiscountDecisionCommandId ?? DBNull.Value;
            command.Parameters.Add("application_command_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)statutoryDiscountPayableBasisApplicationCommandId ?? DBNull.Value;
            command.Parameters.Add("validation_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)statutoryDiscountValidationId ?? DBNull.Value;
            command.Parameters.Add("policy_reference_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                (object?)appliedPolicyReferenceId ?? DBNull.Value;

            var action = async () => await command.ExecuteNonQueryAsync();
            await action.Should().ThrowAsync<Npgsql.PostgresException>();
        }
    }

    private static async Task CorruptDecisionGrossAmountAsync(Guid statutoryDiscountDecisionCommandId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            UPDATE discounts.statutory_discount_decision_commands
            SET gross_amount_minor_units = gross_amount_minor_units + 1,
                updated_at = now()
            WHERE statutory_discount_decision_command_id = @decision_command_id;
            """,
            connection);
        command.Parameters.AddWithValue("decision_command_id", statutoryDiscountDecisionCommandId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<StatutoryDiscountDecisionResponse> GetSharedReadbackAsync(
        HttpClient client,
        Guid parkingSessionId,
        string entitlementType)
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT statutory_discount_decision_command_id
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type = @entitlement_type;
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        command.Parameters.AddWithValue("entitlement_type", entitlementType);
        var decisionId = (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty);
        return await GetSharedReadbackAsync(client, decisionId);
    }

    private static StatutoryDiscountDecisionRequest Request(
        PaymentTestContext context,
        string sourceChannel,
        bool applyPayableBasis,
        bool includePayableBasis = true) =>
        new(
            Guid.NewGuid(),
            sourceChannel,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            $"TICKET-{context.SiteCode}",
            "ABC1234",
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "SC-****-1234",
            EvidenceCaptureRequested: true,
            EvidenceReferences:
            [
                new StatutoryDiscountEvidenceReferenceRequest(
                    "SENIOR_CITIZEN_ID",
                    "MANUAL_REFERENCE",
                    FileName: null,
                    ContentType: null,
                    SizeBytes: null,
                    StorageReference: "evidence-ref-001",
                    ReferenceNumberMasked: "SC-****-1234",
                    VerificationStatus: "VERIFIED")
            ],
            ActorUserId: Guid.Empty,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            RequesterAttestation: true,
            AttestationNotes: "Customer attested statutory discount eligibility.",
            ReasonCode: "CUSTOMER_REQUEST",
            Decision: null,
            DecisionReasonCode: null,
            ReviewerUserId: null,
            ReviewerAttestation: false,
            applyPayableBasis,
            includePayableBasis ? context.TariffSnapshotId : null);

    private static async Task SeedServiceAuthorizationAsync(PaymentTestContext context)
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            INSERT INTO identity.service_identities (
                service_identity_id, service_identity_code, service_identity_name,
                identity_type, identity_status, owning_service_name,
                effective_from, created_at, updated_at, row_version)
            VALUES
                (@webpay_id, 'IST_WEBPAY_STATUTORY_APPLICATION', 'IST WebPay statutory application',
                 'INTERNAL_SERVICE', 'ACTIVE', 'PAYMENT_ORCHESTRATOR', now() - interval '1 hour', now(), now(), 1),
                (@apt_id, 'IST_APT_STATUTORY_APPLICATION', 'IST APT statutory application',
                 'INTERNAL_SERVICE', 'ACTIVE', 'ASSISTED_PAYMENT_TERMINAL', now() - interval '1 hour', now(), now(), 1)
            ON CONFLICT (service_identity_id) DO UPDATE
            SET identity_status = 'ACTIVE',
                owning_service_name = EXCLUDED.owning_service_name,
                effective_from = EXCLUDED.effective_from,
                effective_to = NULL,
                revoked_at = NULL,
                credential_expires_at = NULL,
                updated_at = now(),
                row_version = identity.service_identities.row_version + 1;

            DELETE FROM sites.device_assignments
            WHERE service_identity_id IN (@webpay_id, @apt_id);

            INSERT INTO sites.device_assignments (
                device_assignment_id, site_id, service_identity_id,
                assignment_type, assignment_status, assigned_at,
                created_at, updated_at, row_version)
            VALUES
                (gen_random_uuid(), @site_id, @webpay_id, 'SERVICE_PRINCIPAL', 'ACTIVE', now() - interval '1 hour', now(), now(), 1),
                (gen_random_uuid(), @site_id, @apt_id, 'SERVICE_PRINCIPAL', 'ACTIVE', now() - interval '1 hour', now(), now(), 1);
            """,
            connection);
        command.Parameters.AddWithValue("webpay_id", WebPayServiceIdentityId);
        command.Parameters.AddWithValue("apt_id", AptServiceIdentityId);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupServiceAssignmentsAsync(PaymentTestContext context)
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            DELETE FROM sites.device_assignments
            WHERE site_id = @site_id
              AND service_identity_id IN (@webpay_id, @apt_id);
            """,
            connection);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("webpay_id", WebPayServiceIdentityId);
        command.Parameters.AddWithValue("apt_id", AptServiceIdentityId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupFiscalIssuanceReferencesAsync(Guid parkingSessionId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(
            StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "DELETE FROM core.fiscal_issuance_references WHERE parking_session_id = @parking_session_id;",
            connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InvalidateServiceAuthorizationAsync(PaymentTestContext context, string invalidation)
    {
        var sql = invalidation switch
        {
            "WRONG_SITE" =>
                "DELETE FROM sites.device_assignments WHERE site_id = @site_id AND service_identity_id = @service_identity_id;",
            "WRONG_AUDIENCE" =>
                "UPDATE identity.service_identities SET owning_service_name = 'ASSISTED_PAYMENT_TERMINAL', updated_at = now(), row_version = row_version + 1 WHERE service_identity_id = @service_identity_id;",
            "INACTIVE_IDENTITY" =>
                "UPDATE identity.service_identities SET identity_status = 'REVOKED', revoked_at = now(), updated_at = now(), row_version = row_version + 1 WHERE service_identity_id = @service_identity_id;",
            _ => throw new ArgumentOutOfRangeException(nameof(invalidation), invalidation, "Unsupported authorization invalidation.")
        };

        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("service_identity_id", WebPayServiceIdentityId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ApplicationAttribution> ReadApplicationAttributionAsync(Guid validationId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT
                validation.validated_by_user_id,
                validation.updated_by_service_identity_id,
                application.application_channel::text,
                application.applied_by_user_id,
                application.applied_by_service_identity_id,
                application.created_by_user_id,
                application.created_by_service_identity_id,
                application.updated_by_user_id,
                application.updated_by_service_identity_id,
                applied_tariff.created_by_service_identity_id AS applied_tariff_created_by_service_identity_id
            FROM discounts.statutory_discount_validations validation
            INNER JOIN discounts.statutory_discount_payable_basis_applications application
                ON application.statutory_discount_validation_id = validation.statutory_discount_validation_id
            INNER JOIN core.tariff_snapshots applied_tariff
                ON applied_tariff.tariff_snapshot_id = application.applied_tariff_snapshot_id
            WHERE validation.statutory_discount_validation_id = @validation_id;
            """,
            connection);
        command.Parameters.AddWithValue("validation_id", validationId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new ApplicationAttribution(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9));
    }

    private static void AddServiceHeaders(HttpClient client, string sourceChannel)
    {
        var serviceIdentityId = sourceChannel == StatutoryDiscountSourceChannels.WebPay
            ? WebPayServiceIdentityId
            : AptServiceIdentityId;
        var submitPermission = sourceChannel == StatutoryDiscountSourceChannels.WebPay
            ? "statutory-discounts.decision.submit.webpay"
            : "statutory-discounts.decision.submit.assisted-payment-terminal";
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, serviceIdentityId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, $"{submitPermission} statutory-discounts.decision.read");
    }

    private static void AddOperatorHeaders(HttpClient client, PaymentTestContext context)
    {
        client.DefaultRequestHeaders.Remove("X-Operator-User-Id");
        client.DefaultRequestHeaders.Remove("X-Operator-Device-Binding-Id");
        client.DefaultRequestHeaders.Remove("X-Operator-Shift-Id");
        client.DefaultRequestHeaders.Remove("X-Site-Id");
        client.DefaultRequestHeaders.Remove("X-Site-Group-Id");
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", context.RequestedByUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", ReviewerDeviceBindingId.ToString());
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", ReviewerShiftId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Id", context.SiteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", context.SiteGroupId.ToString());
    }

    private static CustomWebApplicationFactory CreateFactory(
        PaymentTestContext context,
        ZeroPayablePosCallEvidence? zeroPayablePosEvidence = null) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                var access = AllowedResult(context.SiteId, context.SiteGroupId, context.RequestedByUserId);
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAccessEvaluationService(access));
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAccessEvaluationWriter(access));
                if (zeroPayablePosEvidence is not null)
                {
                    var sitePosServerId = Guid.Parse("9b000000-0000-0000-0000-000000000007");
                    services.RemoveAll<FiscalIssuancePosServerIntegrationOptions>();
                    services.AddSingleton(new FiscalIssuancePosServerIntegrationOptions
                    {
                        RuntimeEnvironment = "IntegrationTest",
                        EnablePosServerFiscalIssuanceLiveCall = true,
                        EnableLiveFiscalIssuanceFromPaymentFlow = true,
                        Endpoints =
                        [
                            new SitePosServerEndpointOptions
                            {
                                SiteId = context.SiteId,
                                SitePosServerId = sitePosServerId,
                                SitePosServerRef = "IST-ZERO-PAYABLE-POS",
                                BaseUrl = "http://zero-payable-pos.invalid/",
                                ApiKeyFile = "integration-test-only",
                                Environment = "IntegrationTest",
                                Enabled = true,
                                FiscalDocumentTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000010"),
                                FiscalDocumentStatusCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000011"),
                                FiscalLineTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000012"),
                                FiscalTenderTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000013"),
                                FiscalTaxTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000014"),
                                FiscalTaxClassificationCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000015"),
                                FiscalDiscountPrivilegeTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000016"),
                                FiscalTotalTypeCodeId = Guid.Parse("9b000000-0000-0000-0000-000000000017")
                            }
                        ]
                    });
                    services.RemoveAll<IFiscalIssuancePosServerLiveIntegrationService>();
                    services.AddScoped<IFiscalIssuancePosServerLiveIntegrationService>(provider =>
                        new RecordingZeroPayablePosIntegration(
                            provider.GetRequiredService<IFiscalIssuanceOrchestrationService>(),
                            zeroPayablePosEvidence));
                }
            });

    private sealed class ZeroPayablePosCallEvidence
    {
        public const string ElectronicJournalReference = "EJ:zero-payable-integration-test";

        public int IssueCount { get; private set; }

        public CentralPmsFiscalDocumentMappingContext? LastMapping { get; private set; }

        public void Record(CentralPmsFiscalDocumentMappingContext mapping)
        {
            IssueCount++;
            LastMapping = mapping;
        }
    }

    private sealed class RecordingZeroPayablePosIntegration : IFiscalIssuancePosServerLiveIntegrationService
    {
        private readonly IFiscalIssuanceOrchestrationService _orchestration;
        private readonly ZeroPayablePosCallEvidence _evidence;

        public RecordingZeroPayablePosIntegration(
            IFiscalIssuanceOrchestrationService orchestration,
            ZeroPayablePosCallEvidence evidence)
        {
            _orchestration = orchestration;
            _evidence = evidence;
        }

        public async Task<FiscalIssuancePosServerLiveIntegrationResult> TryIssueFiscalDocumentViaPosServerAsync(
            Guid fiscalIssuanceReferenceId,
            CentralPmsFiscalDocumentMappingContext fiscalContext,
            PosServerCreateResultRecordingContext recordingContext,
            CancellationToken cancellationToken)
        {
            _evidence.Record(fiscalContext);
            await _orchestration.MarkRequestedAsync(
                fiscalIssuanceReferenceId,
                new FiscalIssuanceTransitionContext(recordingContext.CorrelationId, recordingContext.ServiceIdentityId),
                cancellationToken);
            var result = new PosServerFiscalDocumentCreateResult(
                PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: 201,
                Code: "fiscal_document_created",
                Message: "Fiscal document created.",
                FiscalDocumentId: Guid.Parse("9b000000-0000-0000-0000-000000000009"),
                ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalIdentityId: Guid.Parse("9b000000-0000-0000-0000-000000000018"),
                FiscalDocumentStatusCodeId: Guid.Parse("9b000000-0000-0000-0000-000000000011"),
                FiscalSequencePolicyId: Guid.Parse("9b000000-0000-0000-0000-000000000019"),
                FiscalSequenceValue: 1,
                FiscalDocumentNumber: "SI-ZERO-000001",
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-ZERO-",
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: DateTimeOffset.UtcNow,
                FiscalNumberAssignedByRef: "pos-server-integration-test",
                ErrorPosture: null,
                CompletionBasis: FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality,
                CompletionAuthorityRef: fiscalContext.CompletionAuthorityRef,
                ElectronicJournalEventReference: ZeroPayablePosCallEvidence.ElectronicJournalReference);
            var reference = await _orchestration.ApplyPosServerCreateResultAsync(
                fiscalIssuanceReferenceId,
                result,
                recordingContext,
                cancellationToken);
            return FiscalIssuancePosServerLiveIntegrationResult.Applied(
                new PosServerFiscalDocumentRequestMapper().Map(fiscalContext),
                result,
                reference);
        }

        public Task<FiscalIssuancePosServerDiagnosticResult> RunPosServerFiscalIssuanceDiagnosticAsync(
            Guid fiscalIssuanceReferenceId,
            CentralPmsFiscalDocumentMappingContext fiscalContext,
            PosServerCreateResultRecordingContext recordingContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagnostics are not used by this zero-payable integration test.");
    }

    private static OperatorConsoleAccessEvaluationResult AllowedResult(Guid siteId, Guid siteGroupId, Guid reviewerUserId) =>
        new(
            AccessEvaluationId,
            Allowed: true,
            Decision: "ALLOW",
            DenialReasons: [],
            EffectiveRole: "STATUTORY_DISCOUNT_REVIEWER",
            new OperatorConsoleDeviceTrustResult(ReviewerDeviceBindingId, "TRUSTED", "BOUND_DEVICE", Trusted: true),
            new OperatorConsoleShiftContextResult(ReviewerShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(siteId, siteGroupId, Assigned: true),
            DateTimeOffset.UtcNow,
            Persisted: true,
            Guid.NewGuid(),
            new OperatorConsoleAccessEvaluationPersistenceContext(
                reviewerUserId,
                HrIdentityMappingId: null,
                ReviewerDeviceBindingId,
                ReviewerShiftId,
                ShiftTakeoverId: null,
                siteGroupId,
                siteId,
                OperatorConsoleActionCodes.DecideStatutoryDiscount,
                OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
                TargetEntityType: "STATUTORY_DISCOUNT_DECISION",
                TargetEntityId: null));

    private sealed class FakeAccessEvaluationService : IOperatorConsoleAccessEvaluationService
    {
        private readonly OperatorConsoleAccessEvaluationResult _result;

        public FakeAccessEvaluationService(OperatorConsoleAccessEvaluationResult result)
        {
            _result = result;
        }

        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result with
            {
                SiteContext = new OperatorConsoleSiteContextResult(
                    command.SiteId ?? _result.SiteContext.SiteId,
                    command.SiteGroupId ?? _result.SiteContext.SiteGroupId,
                    Assigned: true),
                CorrelationId = command.CorrelationId,
                PersistenceContext = _result.PersistenceContext with
                {
                    OperatorUserId = command.UserId,
                    OperatorDeviceBindingId = command.OperatorDeviceBindingId,
                    OperatorShiftId = command.OperatorShiftId,
                    SiteGroupId = command.SiteGroupId ?? _result.PersistenceContext.SiteGroupId,
                    SiteId = command.SiteId ?? _result.PersistenceContext.SiteId,
                    RequestedAction = command.ControlledActionCode,
                    TargetEntityId = command.ParkingSessionId
                }
            });
    }

    private sealed class FakeAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        private readonly OperatorConsoleAccessEvaluationResult _result;

        public FakeAccessEvaluationWriter(OperatorConsoleAccessEvaluationResult result)
        {
            _result = result;
        }

        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with { EvaluationId = _result.EvaluationId, Persisted = true });
    }

    private sealed record ApplicationAttribution(
        Guid ValidatedByUserId,
        Guid? ValidationUpdatedByServiceIdentityId,
        string ApplicationChannel,
        Guid? AppliedByUserId,
        Guid? AppliedByServiceIdentityId,
        Guid? CreatedByUserId,
        Guid? CreatedByServiceIdentityId,
        Guid? UpdatedByUserId,
        Guid? UpdatedByServiceIdentityId,
        Guid? AppliedTariffCreatedByServiceIdentityId);
}

internal static class StatutoryDiscountDecisionResponseAssertions
{
    public static long FinalPayableAmountMinorUnits(this StatutoryDiscountDecisionResponse response) =>
        response.NetPayableAmountMinorUnits
        ?? throw new InvalidOperationException("Expected final payable amount to be present.");
}
