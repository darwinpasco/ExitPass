using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.WebPay;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryApiIntegrationTests
{
    private const string Route = "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover";
    private static readonly Guid WebPayServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000005");
    private static readonly Guid UserId = Guid.Parse("9b000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("9b000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("9b000000-0000-0000-0000-000000000008");
    private static readonly Guid SiteId = Guid.Parse("9b000000-0000-0000-0000-000000000009");
    private static readonly Guid SiteGroupId = Guid.Parse("9b000000-0000-0000-0000-00000000000a");
    private static readonly Guid DecisionId = Guid.Parse("9b000000-0000-0000-0000-00000000000b");
    private static readonly Guid RequestReference = Guid.Parse("9b000000-0000-0000-0000-00000000000c");

    [Fact]
    public async Task Rediscover_WhenWebPayServiceHasPermission_ReturnsSafeResponse()
    {
        var fake = new FakeRediscoveryService(LifecycleResult());
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddWebPayHeaders(client);

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
        body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found);
        body.StatutoryDecisionId.Should().Be(DecisionId);
        body.OpaqueContinuationReference.Should().Be(RequestReference.ToString("D"));
        body.CorrelationId.Should().Be(CorrelationId);
        fake.LastQuery!.LookupMode.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId);
    }

    [Fact]
    public async Task Rediscover_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task Rediscover_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, WebPayServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "statutory-discounts.decision.submit.webpay");

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task Rediscover_WhenHumanPrincipalUsesWebPayRoute_ReturnsAccessDenied()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Permission);

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied);
    }

    [Fact]
    public async Task Rediscover_WhenRequestMalformed_ReturnsSafeBadRequest()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()));
        using var client = factory.CreateClient();
        AddWebPayHeaders(client);

        using var response = await client.PostAsJsonAsync(
            Route,
            ParkingSessionRequest() with
            {
                TicketReference = "TICKET-1"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("INVALID_REQUEST");
        body.Message.Should().NotContain("SQL");
        body.Message.Should().NotContain("Exception");
    }

    [Theory]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AmbiguousSession)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure)]
    public async Task Rediscover_WhenSafeFailureClassificationOccurs_ReturnsNoInternalDetails(string classification)
    {
        using var factory = CreateFactory(new FakeRediscoveryService(
            WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                classification,
                CorrelationId,
                "The parking privilege request could not be checked right now. Please try again.",
                retryable: classification is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable)));
        using var client = factory.CreateClient();
        AddWebPayHeaders(client);

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var normalizedRaw = raw.ToUpperInvariant();
        raw.Should().Contain(classification);
        normalizedRaw.Should().NotContain("NPGSQL");
        normalizedRaw.Should().NotContain("STACK");
        normalizedRaw.Should().NotContain("CONNECTION");
        normalizedRaw.Should().NotContain("PASSWORD");
    }

    [Fact]
    public async Task Rediscover_WhenUnexpectedEndpointFailureOccurs_ReturnsSafeErrorEnvelope()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()) { ThrowsUnexpected = true });
        using var client = factory.CreateClient();
        AddWebPayHeaders(client);

        using var response = await client.PostAsJsonAsync(Route, ParkingSessionRequest());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure);
        body.Message.Should().NotContain("Simulated");
        body.Message.Should().NotContain("Exception");
    }

    [Fact]
    public void RediscoverEndpoint_HasDedicatedWebPayRediscoveryPolicy()
    {
        using var factory = CreateFactory(new FakeRediscoveryService(LifecycleResult()));
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover",
                StringComparison.Ordinal))
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.PolicyName);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.PolicyName)
            .Should()
            .Contain(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Permission);
    }

    [Theory]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber)]
    public async Task Rediscover_WithExistingWebPayPendingReview_ReturnsSameDecisionAndContinuation(string lookupMode)
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            $"{nameof(Rediscover_WithExistingWebPayPendingReview_ReturnsSameDecisionAndContinuation)}_{lookupMode}",
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);

            var before = await CountWriteSensitiveRowsAsync(seeded);
            using var response = await client.PostAsJsonAsync(Route, RequestFor(seeded, lookupMode));
            var after = await CountWriteSensitiveRowsAsync(seeded);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found);
            body.StatutoryDecisionId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            body.StatutoryDecisionCommandId.Should().Be(seeded.Decision.StatutoryDiscountDecisionCommandId);
            body.RequestReference.Should().Be(seeded.Decision.RequestReference);
            body.OpaqueContinuationReference.Should().Be(seeded.Decision.RequestReference.ToString("D"));
            body.ParkingSessionId.Should().Be(seeded.Context.ParkingSessionId);
            body.SiteId.Should().Be(seeded.Context.SiteId);
            body.SiteGroupId.Should().Be(seeded.Context.SiteGroupId);
            body.EntitlementType.Should().Be("SENIOR_CITIZEN");
            body.DecisionStatus.Should().Be("AWAITING_REVIEW");
            body.PayableBasisStatus.Should().Be("AWAITING_REVIEW");
            body.LifecycleState.Should().Be("PENDING_REVIEW");
            after.Should().Be(before);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task Rediscover_WhenNoActiveLifecycle_ReturnsNoActiveLifecycleWithoutWrites()
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            nameof(Rediscover_WhenNoActiveLifecycle_ReturnsNoActiveLifecycleWithoutWrites));

        try
        {
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);
            var beforeDecisionRows = await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(context.ParkingSessionId);

            using var response = await client.PostAsJsonAsync(
                Route,
                new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId,
                    context.ParkingSessionId,
                    context.SiteId,
                    context.SiteGroupId,
                    TicketReference: null,
                    PlateNumber: null,
                    VendorSystemId: null,
                    "SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle);
            body.StatutoryDecisionId.Should().BeNull();
            (await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(context.ParkingSessionId))
                .Should()
                .Be(beforeDecisionRows);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(context);
        }
    }

    [Fact]
    public async Task Rediscover_WhenParkingSessionDoesNotExist_ReturnsNotFoundWithoutWrites()
    {
        using var factory = CreateRealFactoryWithRbac();
        using var client = factory.CreateClient();
        AddWebPayHeaders(client);
        var unknownParkingSessionId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            Route,
            new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId,
                unknownParkingSessionId,
                SiteId,
                SiteGroupId,
                TicketReference: null,
                PlateNumber: null,
                VendorSystemId: null,
                "SENIOR_CITIZEN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
        body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NotFound);
        (await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(unknownParkingSessionId))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task Rediscover_WhenScopeDenied_ReturnsAccessDeniedWithoutReviewOrApplicationWrites()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Rediscover_WhenScopeDenied_ReturnsAccessDeniedWithoutReviewOrApplicationWrites),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);
            var before = await CountWriteSensitiveRowsAsync(seeded);

            using var response = await client.PostAsJsonAsync(
                Route,
                RequestFor(seeded, WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId) with
                {
                    SiteId = Guid.NewGuid()
                });
            var after = await CountWriteSensitiveRowsAsync(seeded);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied);
            after.Should().Be(before);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task Rediscover_WhenSiteGroupScopeDenied_ReturnsAccessDeniedWithoutWrites()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Rediscover_WhenSiteGroupScopeDenied_ReturnsAccessDeniedWithoutWrites),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);
            var before = await CountWriteSensitiveRowsAsync(seeded);

            using var response = await client.PostAsJsonAsync(
                Route,
                RequestFor(seeded, WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId) with
                {
                    SiteGroupId = Guid.NewGuid()
                });
            var after = await CountWriteSensitiveRowsAsync(seeded);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied);
            after.Should().Be(before);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    [Fact]
    public async Task Rediscover_WhenPlateLookupIsAmbiguous_ReturnsAmbiguousSessionWithoutWrites()
    {
        var siteId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var first = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Rediscover_WhenPlateLookupIsAmbiguous_ReturnsAmbiguousSessionWithoutWrites) + "_1",
            StatutoryDiscountSourceChannels.WebPay,
            siteId,
            siteGroupId);
        var second = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Rediscover_WhenPlateLookupIsAmbiguous_ReturnsAmbiguousSessionWithoutWrites) + "_2",
            StatutoryDiscountSourceChannels.WebPay);
        await AlignLifecycleScopeAndPlateAsync(second, siteId, siteGroupId, "ABC1234");

        try
        {
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);
            var beforeFirst = await CountWriteSensitiveRowsAsync(first);
            var beforeSecond = await CountWriteSensitiveRowsAsync(second);

            using var response = await client.PostAsJsonAsync(
                Route,
                new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber,
                    ParkingSessionId: null,
                    siteId,
                    siteGroupId,
                    TicketReference: null,
                    "ABC1234",
                    VendorSystemId: null,
                    "SENIOR_CITIZEN"));
            var afterFirst = await CountWriteSensitiveRowsAsync(first);
            var afterSecond = await CountWriteSensitiveRowsAsync(second);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AmbiguousSession);
            afterFirst.Should().Be(beforeFirst);
            afterSecond.Should().Be(beforeSecond);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(second.Context);
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(first.Context);
        }
    }

    [Fact]
    public async Task Rediscover_WhenStoredLifecycleIsMalformed_ReturnsMalformedStateWithoutApiWrites()
    {
        var seeded = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Rediscover_WhenStoredLifecycleIsMalformed_ReturnsMalformedStateWithoutApiWrites),
            StatutoryDiscountSourceChannels.WebPay);

        try
        {
            await ForceMalformedRequestReferenceAsync(seeded.Decision.StatutoryDiscountDecisionCommandId);
            using var factory = CreateRealFactoryWithRbac();
            using var client = factory.CreateClient();
            AddWebPayHeaders(client);
            var before = await CountWriteSensitiveRowsAsync(seeded);

            using var response = await client.PostAsJsonAsync(
                Route,
                RequestFor(seeded, WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId));
            var after = await CountWriteSensitiveRowsAsync(seeded);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState);
            after.Should().Be(before);
        }
        finally
        {
            await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(seeded.Context);
        }
    }

    private static CustomWebApplicationFactory CreateFactory(FakeRediscoveryService fake) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IWebPayStatutoryDiscountPendingLifecycleRediscoveryService>();
                services.AddSingleton<IWebPayStatutoryDiscountPendingLifecycleRediscoveryService>(fake);
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static CustomWebApplicationFactory CreateRealFactoryWithRbac() =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            });

    private static void AddWebPayHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, WebPayServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Permission);
    }

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest ParkingSessionRequest() =>
        new(
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            TicketReference: null,
            PlateNumber: null,
            VendorSystemId: null,
            "SENIOR_CITIZEN");

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest RequestFor(
        SeededServiceChannelReview seeded,
        string lookupMode) =>
        lookupMode switch
        {
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference =>
                new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                    lookupMode,
                    ParkingSessionId: null,
                    seeded.Context.SiteId,
                    seeded.Context.SiteGroupId,
                    seeded.Review.TicketReference,
                    PlateNumber: null,
                    VendorSystemId: null,
                    seeded.Decision.EntitlementType),
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber =>
                new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                    lookupMode,
                    ParkingSessionId: null,
                    seeded.Context.SiteId,
                    seeded.Context.SiteGroupId,
                    TicketReference: null,
                    seeded.Review.PlateNumber,
                    VendorSystemId: null,
                    seeded.Decision.EntitlementType),
            _ =>
                new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                    lookupMode,
                    seeded.Context.ParkingSessionId,
                    seeded.Context.SiteId,
                    seeded.Context.SiteGroupId,
                    TicketReference: null,
                    PlateNumber: null,
                    VendorSystemId: null,
                    seeded.Decision.EntitlementType)
        };

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryResult LifecycleResult() =>
        WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.Found(
            new WebPayStatutoryDiscountPendingLifecycleRecord(
                DecisionId,
                DecisionId,
                RequestReference,
                "SENIOR_CITIZEN",
                "AWAITING_REVIEW",
                "AWAITING_REVIEW",
                ParkingSessionId,
                SiteId,
                SiteGroupId,
                RequestReference.ToString("D"),
                OpaqueContinuationUrl: null,
                "PENDING_REVIEW",
                Retryable: true,
                DateTimeOffset.Parse("2026-07-31T08:00:00+08:00"),
                DateTimeOffset.Parse("2026-07-31T08:01:00+08:00"),
                DateTimeOffset.Parse("2026-07-31T08:00:30+08:00"),
                DecidedAt: null,
                ReviewedAt: null),
            CorrelationId);

    private static async Task<WriteSensitiveCounts> CountWriteSensitiveRowsAsync(SeededServiceChannelReview seeded)
    {
        var mutationFacts = await MutationFactsAsync(
            seeded.Context.ParkingSessionId,
            seeded.Decision.StatutoryDiscountDecisionCommandId);
        return new WriteSensitiveCounts(
            await StatutoryDiscountReviewIntegrationTestSupport.DecisionRowCountAsync(seeded.Context.ParkingSessionId),
            await StatutoryDiscountReviewIntegrationTestSupport.ReviewRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId),
            await StatutoryDiscountReviewIntegrationTestSupport.ApplicationCommandRowCountAsync(seeded.Decision.StatutoryDiscountDecisionCommandId),
            await StatutoryDiscountReviewIntegrationTestSupport.PayableBasisApplicationRowCountAsync(seeded.Context.ParkingSessionId),
            await StatutoryDiscountReviewIntegrationTestSupport.AppliedTariffSnapshotRowCountAsync(seeded.Context.ParkingSessionId),
            await StatutoryDiscountReviewIntegrationTestSupport.PaymentBoundaryRowCountAsync(seeded.Context.ParkingSessionId),
            mutationFacts.ParkingSessionRowVersion,
            mutationFacts.DecisionUpdatedAt,
            mutationFacts.ReviewUpdatedAt,
            mutationFacts.TariffRowVersionSum);
    }

    private static async Task<MutationFacts> MutationFactsAsync(
        Guid parkingSessionId,
        Guid decisionCommandId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                COALESCE((SELECT row_version FROM core.parking_sessions WHERE parking_session_id = @parking_session_id), 0)::bigint AS parking_session_row_version,
                (SELECT updated_at FROM discounts.statutory_discount_decision_commands WHERE statutory_discount_decision_command_id = @decision_command_id) AS decision_updated_at,
                (SELECT updated_at FROM operator_console.statutory_discount_service_channel_reviews WHERE statutory_discount_decision_command_id = @decision_command_id) AS review_updated_at,
                COALESCE((SELECT SUM(row_version)::bigint FROM core.tariff_snapshots WHERE parking_session_id = @parking_session_id), 0)::bigint AS tariff_row_version_sum;
            """,
            connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = decisionCommandId;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new MutationFacts(
            reader.GetInt64(reader.GetOrdinal("parking_session_row_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("decision_updated_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("review_updated_at")),
            reader.GetInt64(reader.GetOrdinal("tariff_row_version_sum")));
    }

    private static async Task ForceMalformedRequestReferenceAsync(Guid decisionCommandId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_discount_decision_commands
            SET request_reference = '00000000-0000-0000-0000-000000000000'
            WHERE statutory_discount_decision_command_id = @decision_command_id;
            """,
            connection);
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = decisionCommandId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AlignLifecycleScopeAndPlateAsync(
        SeededServiceChannelReview seeded,
        Guid siteId,
        Guid siteGroupId,
        string plateNumber)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE core.parking_sessions
            SET
                site_id = @site_id,
                site_group_id = @site_group_id,
                plate_number_masked = @plate_number,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            UPDATE operator_console.statutory_discount_service_channel_reviews
            SET
                site_id = @site_id,
                site_group_id = @site_group_id,
                plate_number = @plate_number,
                updated_at = NOW()
            WHERE statutory_discount_decision_command_id = @decision_command_id;
            """,
            connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = seeded.Context.ParkingSessionId;
        command.Parameters.Add("decision_command_id", NpgsqlDbType.Uuid).Value = seeded.Decision.StatutoryDiscountDecisionCommandId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.AddWithValue("plate_number", plateNumber);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record WriteSensitiveCounts(
        int Decisions,
        int Reviews,
        int ApplicationCommands,
        int PayableBasisApplications,
        int AppliedTariffSnapshots,
        int PaymentBoundaryRows,
        long ParkingSessionRowVersion,
        DateTimeOffset DecisionUpdatedAt,
        DateTimeOffset ReviewUpdatedAt,
        long TariffRowVersionSum);

    private sealed record MutationFacts(
        long ParkingSessionRowVersion,
        DateTimeOffset DecisionUpdatedAt,
        DateTimeOffset ReviewUpdatedAt,
        long TariffRowVersionSum);

    private sealed class FakeRediscoveryService : IWebPayStatutoryDiscountPendingLifecycleRediscoveryService
    {
        private readonly WebPayStatutoryDiscountPendingLifecycleRediscoveryResult _result;

        public FakeRediscoveryService(WebPayStatutoryDiscountPendingLifecycleRediscoveryResult result)
        {
            _result = result;
        }

        public WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery? LastQuery { get; private set; }
        public bool ThrowsUnexpected { get; set; }

        public Task<WebPayStatutoryDiscountPendingLifecycleRediscoveryResult> RediscoverAsync(
            WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            if (ThrowsUnexpected)
            {
                throw new InvalidOperationException("Simulated unexpected rediscovery failure.");
            }

            if (query.LookupMode is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId
                && query.TicketReference is not null)
            {
                throw new WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException(
                    "INVALID_REQUEST",
                    "Exactly one lookup context is required.",
                    query.CorrelationId);
            }

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeRbacRepository : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(
            Guid userId,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ServiceIdentityIsActiveAsync(
            Guid serviceIdentityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(serviceIdentityId == WebPayServiceIdentityId);

        public Task RecordDeniedAsync(
            string policyName,
            Guid? userId,
            Guid? serviceIdentityId,
            Guid? correlationId,
            string requestPath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordAuditEventAsync(
            string eventType,
            string eventResult,
            string eventReasonCode,
            string targetEntityType,
            Guid? targetEntityId,
            Guid? actorUserId,
            Guid? actorServiceIdentityId,
            Guid? correlationId,
            string summary,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
