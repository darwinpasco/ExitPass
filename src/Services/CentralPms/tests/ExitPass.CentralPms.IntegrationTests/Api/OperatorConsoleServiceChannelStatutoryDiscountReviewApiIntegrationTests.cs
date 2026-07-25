using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests
{
    private const string QueueEndpoint = "/v1/ops/operator-console/statutory-discounts/reviews/pending";
    private const string DetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/reviews/{0}/decision";
    private const string SharedReadbackEndpointTemplate = "/v1/statutory-discounts/decisions/{0}";
    private static readonly Guid ReviewerUserId = Guid.Parse("9a000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewerDeviceBindingId = Guid.Parse("9a000000-0000-0000-0000-000000000002");
    private static readonly Guid ReviewerShiftId = Guid.Parse("9a000000-0000-0000-0000-000000000003");
    private static readonly Guid AccessEvaluationId = Guid.Parse("9a000000-0000-0000-0000-000000000004");
    private static readonly Guid OtherSiteId = Guid.Parse("9a000000-0000-0000-0000-000000000005");
    private static readonly Guid OtherSiteGroupId = Guid.Parse("9a000000-0000-0000-0000-000000000006");

    [Fact]
    public async Task ListAndDetail_ReturnServiceChannelPendingReviews_WithBoundedFiltersAndSafeFacts()
    {
        var webPay = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ListAndDetail_ReturnServiceChannelPendingReviews_WithBoundedFiltersAndSafeFacts),
            StatutoryDiscountSourceChannels.WebPay);
        var apt = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ListAndDetail_ReturnServiceChannelPendingReviews_WithBoundedFiltersAndSafeFacts) + "Apt",
            StatutoryDiscountSourceChannels.AssistedPaymentTerminal);

        try
        {
            using var factory = CreateFactory(AllowedResult(webPay.Context.SiteId, webPay.Context.SiteGroupId));
            using var client = factory.CreateClient();
            AddOperatorHeaders(client, webPay.Context.SiteId, webPay.Context.SiteGroupId);

            var webPayQueue = await GetQueueAsync(
                client,
                $"sourceChannel=WEBPAY&siteId={webPay.Context.SiteId}&parkingSessionId={webPay.Context.ParkingSessionId}");
            webPayQueue.Items.Should().ContainSingle(item =>
                item.StatutoryDiscountDecisionCommandId == webPay.Decision.StatutoryDiscountDecisionCommandId &&
                item.SourceChannel == StatutoryDiscountSourceChannels.WebPay &&
                item.ReviewStatus == StatutoryDiscountServiceChannelReviewStatuses.PendingReview);
            webPayQueue.Items.Should().NotContain(item => item.StatutoryDiscountDecisionCommandId == apt.Decision.StatutoryDiscountDecisionCommandId);

            using var aptFactory = CreateFactory(AllowedResult(apt.Context.SiteId, apt.Context.SiteGroupId));
            using var aptClient = aptFactory.CreateClient();
            AddOperatorHeaders(aptClient, apt.Context.SiteId, apt.Context.SiteGroupId);
            var aptQueue = await GetQueueAsync(aptClient, "sourceChannel=ASSISTED_PAYMENT_TERMINAL");
            aptQueue.Items.Should().ContainSingle(item =>
                item.StatutoryDiscountDecisionCommandId == apt.Decision.StatutoryDiscountDecisionCommandId &&
                item.SourceChannel == StatutoryDiscountSourceChannels.AssistedPaymentTerminal);

            var detail = await GetDetailAsync(client, webPay.Decision.StatutoryDiscountDecisionCommandId);
            detail.StatutoryDiscountDecisionCommandId.Should().Be(webPay.Decision.StatutoryDiscountDecisionCommandId);
            detail.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            detail.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            detail.MaskedIdReference.Should().Be("SC-****-1234");
            detail.MaskedIdReference.Should().NotContain("123456789");
            detail.EvidenceReferences.Should().ContainSingle();
            detail.EvidenceReferences[0].ReferenceNumberMasked.Should().Be("SC-****-1234");
            detail.EvidenceReferences[0].StorageReference.Should().Be("evidence-ref-001");
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(webPay.Context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(apt.Context);
        }
    }

    [Theory]
    [InlineData("WEBPAY", "APPROVE", "APPROVED")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL", "REJECT", "REJECTED")]
    public async Task DecisionCompletion_CompletesSameCanonicalDecision_AndSharedReadbackReflectsTerminalResult(
        string sourceChannel,
        string decision,
        string expectedResult)
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(DecisionCompletion_CompletesSameCanonicalDecision_AndSharedReadbackReflectsTerminalResult) + sourceChannel + decision,
            sourceChannel);

        try
        {
            using var factory = CreateFactory(AllowedResult(seeded.Context.SiteId, seeded.Context.SiteGroupId));
            using var client = factory.CreateClient();
            AddOperatorHeaders(client, seeded.Context.SiteId, seeded.Context.SiteGroupId);

            using var response = await client.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, decision));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
            body.Should().NotBeNull();
            body!.DecisionAccepted.Should().BeTrue();
            body.DecisionPersisted.Should().BeTrue();
            body.StatutoryDiscountDecisionCommandId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            body.CurrentValidationStatus.Should().Be(expectedResult);

            var shared = await GetSharedReadbackAsync(client, seeded.Decision.StatutoryDiscountDecisionCommandId);
            shared.StatutoryDiscountDecisionCommandId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            shared.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
            shared.DecisionResultStatus.Should().Be(expectedResult);
            shared.ApplicationRequested.Should().BeFalse();
            shared.ApplicationCommandStatus.Should().Be("NOT_REQUESTED");
            shared.StatutoryDiscountPayableBasisApplicationCommandId.Should().BeNull();
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(seeded.Context.ParkingSessionId)).Should().Be(0);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Theory]
    [InlineData("APPROVE", "APPROVE", "APPROVED", 2, 0)]
    [InlineData("REJECT", "REJECT", "REJECTED", 2, 0)]
    [InlineData("APPROVE", "REJECT", null, 1, 1)]
    public async Task ConcurrentReviewCompletion_ProducesOneTerminalCanonicalDecision_NoApplicationAndDeterministicConflict(
        string firstDecision,
        string secondDecision,
        string? expectedResult,
        int expectedOk,
        int expectedConflict)
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(ConcurrentReviewCompletion_ProducesOneTerminalCanonicalDecision_NoApplicationAndDeterministicConflict) + firstDecision + secondDecision,
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            using var factory = CreateFactory(AllowedResult(seeded.Context.SiteId, seeded.Context.SiteGroupId));
            using var firstClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();
            AddOperatorHeaders(firstClient, seeded.Context.SiteId, seeded.Context.SiteGroupId);
            AddOperatorHeaders(secondClient, seeded.Context.SiteId, seeded.Context.SiteGroupId);

            var first = firstClient.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, firstDecision, "first-concurrent-key"));
            var second = secondClient.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, secondDecision, "second-concurrent-key"));

            var responses = await Task.WhenAll(first, second);
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(expectedOk);
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(expectedConflict);

            var shared = await GetSharedReadbackAsync(firstClient, seeded.Decision.StatutoryDiscountDecisionCommandId);
            shared.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
            if (expectedResult is not null)
            {
                shared.DecisionResultStatus.Should().Be(expectedResult);
            }
            else
            {
                shared.DecisionResultStatus.Should().BeOneOf(
                    StatutoryDiscountDecisionV2ResultStates.Approved,
                    StatutoryDiscountDecisionV2ResultStates.Rejected);
            }

            (await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(seeded.Context.ParkingSessionId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.ReviewRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(1);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
            (await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(seeded.Context.ParkingSessionId)).Should().Be(0);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task Recovery_WhenCanonicalCompletionExistsButReviewLinkagePending_RetryRepairsLinkage()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Recovery_WhenCanonicalCompletionExistsButReviewLinkagePending_RetryRepairsLinkage),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CreateStagedService()
                .CompleteDecisionApprovedAsync(
                    seeded.Decision.StatutoryDiscountDecisionCommandId,
                    statutoryDiscountValidationId: null,
                    seeded.Decision.OriginalTariffSnapshotId,
                    seeded.Decision.AppliedPolicyReferenceId,
                    seeded.Decision.FallbackPolicyReferenceId,
                    seeded.Decision.PolicyResolutionBasis,
                    seeded.Decision.LocalOrdinanceApplied,
                    new StatutoryDiscountDecisionV2TariffFacts(
                        seeded.Decision.GrossAmountMinorUnits,
                        seeded.Decision.VatExclusiveAmountMinorUnits,
                        seeded.Decision.VatAmountMinorUnits,
                        seeded.Decision.StatutoryDiscountAmountMinorUnits,
                        seeded.Decision.NetPayableAmountMinorUnits,
                        seeded.Decision.Currency),
                    "ELIGIBLE",
                    seeded.Context.CorrelationId,
                    CancellationToken.None);

            using var factory = CreateFactory(AllowedResult(seeded.Context.SiteId, seeded.Context.SiteGroupId));
            using var client = factory.CreateClient();
            AddOperatorHeaders(client, seeded.Context.SiteId, seeded.Context.SiteGroupId);

            using var response = await client.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, "APPROVE"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
            body.Should().NotBeNull();
            body!.AlreadyDecided.Should().BeTrue();
            body.DecisionAccepted.Should().BeTrue();
            body.DecisionPersisted.Should().BeTrue();

            var detail = await GetDetailAsync(client, seeded.Decision.StatutoryDiscountDecisionCommandId);
            detail.ReviewStatus.Should().Be(StatutoryDiscountServiceChannelReviewStatuses.Approved);
            detail.ReviewerUserId.Should().Be(ReviewerUserId);
            (await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task AccessPolicyAndScopeFailures_AreRejectedThroughHttp()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(AccessPolicyAndScopeFailures_AreRejectedThroughHttp),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            using var allowedFactory = CreateFactory(AllowedResult(seeded.Context.SiteId, seeded.Context.SiteGroupId));
            using var unauthenticatedClient = allowedFactory.CreateClient();
            (await unauthenticatedClient.GetAsync(QueueEndpoint)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var deniedFactory = CreateFactory(DeniedResult(seeded.Context.SiteId, seeded.Context.SiteGroupId, "MISSING_REVIEWER_PERMISSION"));
            using var deniedClient = deniedFactory.CreateClient();
            AddOperatorHeaders(deniedClient, seeded.Context.SiteId, seeded.Context.SiteGroupId);
            (await deniedClient.GetAsync(QueueEndpoint)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var wrongSiteFactory = CreateFactory(AllowedResult(OtherSiteId, OtherSiteGroupId));
            using var wrongSiteClient = wrongSiteFactory.CreateClient();
            AddOperatorHeaders(wrongSiteClient, OtherSiteId, OtherSiteGroupId);
            (await wrongSiteClient.GetAsync(DetailEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var serviceClient = allowedFactory.CreateClient();
            serviceClient.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, Guid.NewGuid().ToString());
            serviceClient.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "statutory-discounts.decision.submit.webpay");
            (await serviceClient.GetAsync($"{QueueEndpoint}?sourceChannel=WEBPAY")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var missingShiftClient = deniedFactory.CreateClient();
            AddOperatorHeaders(missingShiftClient, seeded.Context.SiteId, seeded.Context.SiteGroupId, shiftId: Guid.Empty);
            using var missingShiftResponse = await missingShiftClient.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, "APPROVE") with { OperatorShiftId = null });
            missingShiftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var missingShift = await missingShiftResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
            missingShift!.AccessAllowed.Should().BeFalse();
            missingShift.DecisionAccepted.Should().BeFalse();

            using var invalidDeviceClient = deniedFactory.CreateClient();
            AddOperatorHeaders(invalidDeviceClient, seeded.Context.SiteId, seeded.Context.SiteGroupId, deviceBindingId: Guid.Empty);
            using var invalidDeviceResponse = await invalidDeviceClient.PostAsJsonAsync(
                DecisionEndpoint(seeded.Decision.StatutoryDiscountDecisionCommandId),
                DecisionRequest(seeded.Context, "APPROVE") with { OperatorDeviceBindingId = null });
            invalidDeviceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var invalidDevice = await invalidDeviceResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
            invalidDevice!.AccessAllowed.Should().BeFalse();
            invalidDevice.DecisionAccepted.Should().BeFalse();
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    private static async Task<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse> GetQueueAsync(
        HttpClient client,
        string query)
    {
        using var response = await client.GetAsync($"{QueueEndpoint}?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse>())!;
    }

    private static async Task<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse> GetDetailAsync(
        HttpClient client,
        Guid decisionCommandId)
    {
        using var response = await client.GetAsync(DetailEndpoint(decisionCommandId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse>())!;
    }

    private static async Task<StatutoryDiscountDecisionResponse> GetSharedReadbackAsync(HttpClient client, Guid decisionCommandId)
    {
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString());
        using var response = await client.GetAsync(string.Format(SharedReadbackEndpointTemplate, decisionCommandId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>())!;
    }

    private static string DetailEndpoint(Guid decisionCommandId) =>
        string.Format(DetailEndpointTemplate, decisionCommandId);

    private static string DecisionEndpoint(Guid decisionCommandId) =>
        string.Format(DecisionEndpointTemplate, decisionCommandId);

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest(
        PaymentTestContext context,
        string decision,
        string idempotencyKey = "service-channel-review-decision-key") =>
        new(
            ReviewerUserId,
            ReviewerDeviceBindingId,
            context.SiteId,
            context.SiteGroupId,
            ReviewerShiftId,
            decision,
            decision == "APPROVE" ? "ELIGIBLE" : "DOCUMENT_INVALID",
            DecisionNotes: null,
            ReviewerAttestation: true,
            idempotencyKey,
            Guid.NewGuid());

    private static void AddOperatorHeaders(
        HttpClient client,
        Guid siteId,
        Guid siteGroupId,
        Guid? deviceBindingId = null,
        Guid? shiftId = null)
    {
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", ReviewerUserId.ToString());
        if (deviceBindingId.GetValueOrDefault(ReviewerDeviceBindingId) != Guid.Empty)
        {
            client.DefaultRequestHeaders.Add("X-Operator-Device-Binding-Id", deviceBindingId.GetValueOrDefault(ReviewerDeviceBindingId).ToString());
        }

        if (shiftId.GetValueOrDefault(ReviewerShiftId) != Guid.Empty)
        {
        client.DefaultRequestHeaders.Add("X-Operator-Shift-Id", shiftId.GetValueOrDefault(ReviewerShiftId).ToString());
        }

        client.DefaultRequestHeaders.Add("X-Site-Id", siteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", siteGroupId.ToString());
    }

    private static CustomWebApplicationFactory CreateFactory(OperatorConsoleAccessEvaluationResult accessResult) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAccessEvaluationService(accessResult));
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAccessEvaluationWriter(accessResult));
            });

    private static OperatorConsoleAccessEvaluationResult AllowedResult(Guid siteId, Guid siteGroupId) =>
        AccessResult(allowed: true, siteId, siteGroupId, []);

    private static OperatorConsoleAccessEvaluationResult DeniedResult(Guid siteId, Guid siteGroupId, string reason) =>
        AccessResult(allowed: false, siteId, siteGroupId, [reason]);

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        bool allowed,
        Guid siteId,
        Guid siteGroupId,
        IReadOnlyList<string> reasons) =>
        new(
            AccessEvaluationId,
            allowed,
            allowed ? "ALLOW" : "DENY",
            reasons,
            allowed ? "STATUTORY_DISCOUNT_REVIEWER" : null,
            new OperatorConsoleDeviceTrustResult(ReviewerDeviceBindingId, allowed ? "TRUSTED" : "UNTRUSTED", "BOUND_DEVICE", allowed),
            new OperatorConsoleShiftContextResult(ReviewerShiftId, allowed ? "ACTIVE" : "MISSING", allowed),
            new OperatorConsoleSiteContextResult(siteId, siteGroupId, Assigned: true),
            DateTimeOffset.UtcNow,
            Persisted: true,
            Guid.NewGuid(),
            new OperatorConsoleAccessEvaluationPersistenceContext(
                ReviewerUserId,
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
            CancellationToken cancellationToken)
        {
            if (!_result.Allowed)
            {
                return Task.FromResult(_result with
                {
                    CorrelationId = command.CorrelationId,
                    PersistenceContext = _result.PersistenceContext with
                    {
                        OperatorUserId = command.UserId,
                        OperatorDeviceBindingId = command.OperatorDeviceBindingId,
                        OperatorShiftId = command.OperatorShiftId,
                        RequestedAction = command.ControlledActionCode,
                        TargetEntityId = command.ParkingSessionId
                    }
                });
            }

            return Task.FromResult(_result with
            {
                CorrelationId = command.CorrelationId,
                PersistenceContext = _result.PersistenceContext with
                {
                    OperatorUserId = command.UserId,
                    OperatorDeviceBindingId = command.OperatorDeviceBindingId,
                    OperatorShiftId = command.OperatorShiftId,
                    RequestedAction = command.ControlledActionCode,
                    TargetEntityId = command.ParkingSessionId
                }
            });
        }
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
