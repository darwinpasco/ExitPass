using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests
{
    private const string SharedDecisionEndpoint = "/v1/statutory-discounts/decisions";
    private const string SharedReadbackEndpointTemplate = "/v1/statutory-discounts/decisions/{0}";
    private const string ReviewDecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}/decision";
    private const string ReviewDetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}";
    private const string OperatorApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";
    private static readonly Guid ReviewerDeviceBindingId = Guid.Parse("9b000000-0000-0000-0000-000000000002");
    private static readonly Guid ReviewerShiftId = Guid.Parse("9b000000-0000-0000-0000-000000000003");
    private static readonly Guid AccessEvaluationId = Guid.Parse("9b000000-0000-0000-0000-000000000004");
    private static readonly Guid WebPayServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000005");
    private static readonly Guid AptServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000006");

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay)]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal)]
    public async Task ServiceChannel_RealReviewMediatedApplicationFlow_AppliesOnceReplaysAndPaymentInitiationUsesAppliedSnapshot(
        string sourceChannel)
    {
        var scenarioName = nameof(ServiceChannel_RealReviewMediatedApplicationFlow_AppliesOnceReplaysAndPaymentInitiationUsesAppliedSnapshot) + sourceChannel;
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(scenarioName);

        try
        {
            using var factory = CreateFactory(context);
            using var serviceClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, sourceChannel);
            using var operatorClient = factory.CreateClient();
            AddOperatorHeaders(operatorClient, context);

            var intake = await PostSharedDecisionAsync(
                serviceClient,
                Request(context, sourceChannel, applyPayableBasis: false),
                $"svc-intake-{sourceChannel}-{context.ParkingSessionId:N}",
                context.CorrelationId,
                expectedStatus: HttpStatusCode.Created);
            intake.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            intake.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            intake.ApplicationRequested.Should().BeFalse();

            var approved = await CompleteReviewAsync(operatorClient, context, intake.StatutoryDiscountDecisionCommandId, "APPROVE");
            approved.CurrentValidationStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
            approved.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);

            var reviewDetail = await GetReviewDetailAsync(operatorClient, intake.StatutoryDiscountDecisionCommandId);
            reviewDetail.StatutoryDiscountValidationId.Should().NotBeNull();
            reviewDetail.SourceChannel.Should().Be(sourceChannel);

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

            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);

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

            var paymentAttempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
                StatutoryDiscountReviewIntegrationTestSupport.ConnectionString,
                context,
                $"payment-after-statutory-application-{context.ParkingSessionId:N}",
                "service-channel-application-intent-test",
                application.AppliedTariffSnapshotId!.Value);
            paymentAttempt.TariffSnapshotId.Should().Be(application.AppliedTariffSnapshotId!.Value);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay, StatutoryDiscountSourceChannels.AssistedPaymentTerminal)]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal, StatutoryDiscountSourceChannels.WebPay)]
    public async Task ServiceChannel_CrossChannelApplicationIntent_ConvergesOnSameCanonicalApplication(
        string intakeChannel,
        string applyChannel)
    {
        var scenarioName = nameof(ServiceChannel_CrossChannelApplicationIntent_ConvergesOnSameCanonicalApplication) + intakeChannel + applyChannel;
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(scenarioName);

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

            var first = await PostSharedDecisionAsync(
                applyClient,
                Request(context, applyChannel, applyPayableBasis: true),
                $"cross-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);
            var replay = await PostSharedDecisionAsync(
                intakeClient,
                Request(context, intakeChannel, applyPayableBasis: true),
                $"cross-intake-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                HttpStatusCode.OK);

            replay.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            replay.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(first.StatutoryDiscountPayableBasisApplicationCommandId);
            replay.AppliedTariffSnapshotId.Should().Be(first.AppliedTariffSnapshotId);
            (await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperatorConsoleApplyAndServiceChannelApplicationIntent_ConvergeOnSameCanonicalApplication(
        bool serviceChannelAppliesFirst)
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(OperatorConsoleApplyAndServiceChannelApplicationIntent_ConvergeOnSameCanonicalApplication) + serviceChannelAppliesFirst);

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

            StatutoryDiscountDecisionResponse? serviceApplication = null;
            OperatorConsoleStatutoryDiscountApplyPayableBasisResponse? operatorApplication = null;
            if (serviceChannelAppliesFirst)
            {
                serviceApplication = await PostSharedDecisionAsync(
                    serviceClient,
                    Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                    $"oc-converge-service-apply-{context.ParkingSessionId:N}",
                    Guid.NewGuid(),
                    HttpStatusCode.OK);
                operatorApplication = await ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);
            }
            else
            {
                operatorApplication = await ApplyWithOperatorConsoleAsync(operatorClient, context, validationId);
                serviceApplication = await PostSharedDecisionAsync(
                    serviceClient,
                    Request(context, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: true),
                    $"oc-converge-service-apply-{context.ParkingSessionId:N}",
                    Guid.NewGuid(),
                    HttpStatusCode.OK);
            }

            operatorApplication.StatutoryDiscountDecisionCommandId.Should().Be(intake.StatutoryDiscountDecisionCommandId);
            operatorApplication.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(serviceApplication.StatutoryDiscountPayableBasisApplicationCommandId);
            operatorApplication.AppliedTariffSnapshotId.Should().Be(serviceApplication.AppliedTariffSnapshotId);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
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

        try
        {
            using var factory = CreateFactory(awaitingContext);
            using var serviceClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(serviceClient, StatutoryDiscountSourceChannels.WebPay);
            AddOperatorHeaders(operatorClient, awaitingContext);

            var awaiting = await PostSharedDecisionAsync(
                serviceClient,
                Request(awaitingContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"negative-awaiting-intake-{awaitingContext.ParkingSessionId:N}",
                awaitingContext.CorrelationId,
                HttpStatusCode.Created);
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
                Request(rejectedContext, StatutoryDiscountSourceChannels.WebPay, applyPayableBasis: false),
                $"negative-rejected-intake-{rejectedContext.ParkingSessionId:N}",
                rejectedContext.CorrelationId,
                HttpStatusCode.Created);
            AddOperatorHeaders(operatorClient, rejectedContext);
            await CompleteReviewAsync(operatorClient, rejectedContext, rejectedIntake.StatutoryDiscountDecisionCommandId, "REJECT");
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

        try
        {
            using var factory = CreateFactory(context);
            using var webPayClient = factory.CreateClient();
            using var aptClient = factory.CreateClient();
            using var operatorClient = factory.CreateClient();
            AddServiceHeaders(webPayClient, StatutoryDiscountSourceChannels.WebPay);
            AddServiceHeaders(aptClient, StatutoryDiscountSourceChannels.AssistedPaymentTerminal);
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
            var aptApply = PostSharedDecisionAsync(
                aptClient,
                Request(context, StatutoryDiscountSourceChannels.AssistedPaymentTerminal, applyPayableBasis: true),
                $"concurrent-apt-apply-{context.ParkingSessionId:N}",
                Guid.NewGuid(),
                expectedStatus: null);
            var operatorApply = ApplyWithOperatorConsoleAsync(operatorClient, context, validationId, expectedStatus: null);

            var serviceResult = await serviceApply;
            var aptResult = await aptApply;
            var operatorResult = await operatorApply;

            var readback = await GetSharedReadbackAsync(webPayClient, intake.StatutoryDiscountDecisionCommandId);
            readback.ApplicationCommandStatus.Should().Be(
                StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied,
                "concurrent service-channel and Operator Console application intent must converge instead of leaving status {0}; service result {1}, APT result {2}, operator accepted {3} error {4}",
                readback.ApplicationCommandStatus,
                serviceResult.ApplicationCommandStatus,
                aptResult.ApplicationCommandStatus,
                operatorResult.ApplicationAccepted,
                operatorResult.ErrorCode);

            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(intake.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(context.ParkingSessionId)).Should().Be(1);

            readback.ApplicationCommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
            readback.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
        }
        finally
        {
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
            new OperatorConsoleStatutoryDiscountDecisionRequest(
                context.RequestedByUserId,
                ReviewerDeviceBindingId,
                context.SiteId,
                context.SiteGroupId,
                ReviewerShiftId,
                decision,
                decision == "APPROVE" ? "ELIGIBLE" : "DOCUMENT_INVALID",
                DecisionNotes: null,
                ReviewerAttestation: true,
                $"review-{decision}-{statutoryDiscountDecisionCommandId:N}",
                Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>())!;
    }

    private static async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse> ApplyWithOperatorConsoleAsync(
        HttpClient client,
        PaymentTestContext context,
        Guid validationId,
        HttpStatusCode? expectedStatus = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(
            string.Format(OperatorApplyEndpointTemplate, validationId),
            new OperatorConsoleStatutoryDiscountApplyPayableBasisRequest(
                context.RequestedByUserId,
                ReviewerDeviceBindingId,
                context.SiteId,
                context.SiteGroupId,
                ReviewerShiftId,
                context.TariffSnapshotId,
                $"operator-apply-{validationId:N}",
                Guid.NewGuid()));
        if (expectedStatus is not null)
        {
            response.StatusCode.Should().Be(expectedStatus.Value, await response.Content.ReadAsStringAsync());
        }
        else
        {
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new OperatorConsoleStatutoryDiscountApplyPayableBasisResponse(
                AccessEvaluationId,
                AccessAllowed: true,
                AccessDecision: "ALLOW",
                AccessDenialReasons: [],
                AccessPersisted: true,
                ApplicationAccepted: false,
                ApplicationPersisted: false,
                PayableBasisApplicationId: null,
                validationId,
                context.ParkingSessionId,
                context.TariffSnapshotId,
                AppliedTariffSnapshotId: null,
                ApplicationStatus: "CONFLICT",
                AlreadyApplied: false,
                GrossAmountMinorUnits: null,
                VatAmountMinorUnits: null,
                VatExclusiveAmountMinorUnits: null,
                StatutoryDiscountAmountMinorUnits: null,
                FinalPayableAmountMinorUnits: null,
                CurrencyCode: null,
                StatutoryDiscountPolicyId: null,
                ResolvedJurisdictionId: null,
                PolicyResolutionBasis: null,
                PolicyCode: null,
                BenefitType: null,
                NationalLawReference: null,
                OrdinanceReference: null,
                PolicySnapshotUsed: false,
                IneligibilityReason: null,
                ErrorCode: "CONFLICT",
                Guid.NewGuid());
        }

        return (await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>())!;
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
        bool applyPayableBasis) =>
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
            context.TariffSnapshotId);

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

    private static CustomWebApplicationFactory CreateFactory(PaymentTestContext context) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                var access = AllowedResult(context.SiteId, context.SiteGroupId, context.RequestedByUserId);
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAccessEvaluationService(access));
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAccessEvaluationWriter(access));
            });

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
}

internal static class StatutoryDiscountDecisionResponseAssertions
{
    public static long FinalPayableAmountMinorUnits(this StatutoryDiscountDecisionResponse response) =>
        response.NetPayableAmountMinorUnits
        ?? throw new InvalidOperationException("Expected final payable amount to be present.");
}
