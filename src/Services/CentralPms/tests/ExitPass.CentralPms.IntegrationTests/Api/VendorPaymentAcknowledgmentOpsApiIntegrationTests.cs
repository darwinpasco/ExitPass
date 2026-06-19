using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Operations;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the read-only ops API for Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentOpsApiIntegrationTests
{
    private static readonly Guid AcknowledgmentId = Guid.Parse("279d0000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("279d0000-0000-0000-0000-000000000002");
    private static readonly Guid PaymentConfirmationId = Guid.Parse("279d0000-0000-0000-0000-000000000003");
    private static readonly Guid ParkingSessionId = Guid.Parse("279d0000-0000-0000-0000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("279d0000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-06-19T01:00:00Z");

    [Fact]
    public async Task Search_WhenCalled_ReturnsPaginatedVendorAcknowledgments()
    {
        var fake = new FakeVendorPaymentAcknowledgmentOpsService
        {
            SearchResult = new VendorPaymentAcknowledgmentSearchResult(
                [Record(VendorPaymentAcknowledgmentStatuses.RetryPending)],
                new VendorPaymentAcknowledgmentStatusBucketCounts(0, 1, 0, 0, 0, 0),
                PageIndex: 1,
                PageSize: 1,
                HasMore: true)
        };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/vendor-payment-acknowledgments/search",
            new VendorPaymentAcknowledgmentSearchRequest
            {
                AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.RetryPending,
                VendorSystemCode = "HIKCENTRAL",
                PageIndex = 1,
                PageSize = 1
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VendorPaymentAcknowledgmentSearchResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].VendorPaymentAcknowledgmentId.Should().Be(AcknowledgmentId);
        body.Items[0].StatusBucket.Should().Be("retry_pending");
        body.Items[0].TicketNumber.Should().Be("TICKET-279D");
        body.Items[0].CardNum.Should().Be("CARD-279D");
        body.StatusBuckets.RetryPending.Should().Be(1);
        body.PageIndex.Should().Be(1);
        body.PageSize.Should().Be(1);
        body.HasMore.Should().BeTrue();
        fake.LastSearchQuery!.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.RetryPending);
        fake.LastSearchQuery.VendorSystemCode.Should().Be("HIKCENTRAL");
    }

    [Fact]
    public async Task Detail_WhenKnown_ReturnsSafeDiagnostics()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/vendor-payment-acknowledgments/{AcknowledgmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VendorPaymentAcknowledgmentDetailResponse>();
        body.Should().NotBeNull();
        body!.VendorPaymentAcknowledgmentId.Should().Be(AcknowledgmentId);
        body.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.RetryPending);
        body.VendorCode.Should().Be("128");
        body.VendorMessage.Should().Be("Vendor retry pending.");
        body.Diagnostics.Should().NotBeEmpty();
        body.Diagnostics.Should().Contain(item => item.Code == "VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE");
    }

    [Fact]
    public async Task Detail_WhenUnknown_ReturnsNotFound()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/vendor-payment-acknowledgments/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("VENDOR_PAYMENT_ACKNOWLEDGMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Search_ResponseDoesNotExposeSecretBearingFields()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/vendor-payment-acknowledgments/search",
            new VendorPaymentAcknowledgmentSearchRequest { PageSize = 10 });
        var json = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();

        json.Should().NotContain("appsecret");
        json.Should().NotContain("signature");
        json.Should().NotContain("authorization");
        json.Should().NotContain("authheader");
        json.Should().NotContain("idempotencykey");
    }

    [Fact]
    public async Task VendorPaymentAcknowledgmentOps_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService())
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/vendor-payment-acknowledgments/search",
            new VendorPaymentAcknowledgmentSearchRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VendorPaymentAcknowledgmentOps_WhenRbacEnabledAndViewerPermissionPresent_AllowsRead()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService())
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.view");

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/vendor-payment-acknowledgments/search",
            new VendorPaymentAcknowledgmentSearchRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void VendorPaymentAcknowledgmentOpsEndpoints_ExposeViewerPolicyMetadata()
    {
        using var factory = CreateFactory(new FakeVendorPaymentAcknowledgmentOpsService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/ops/vendor-payment-acknowledgments", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .OnlyContain(policy => policy == "VendorPaymentAcknowledgmentViewer");
    }

    private static CustomWebApplicationFactory CreateFactory(FakeVendorPaymentAcknowledgmentOpsService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IVendorPaymentAcknowledgmentOpsService>();
                services.AddSingleton<IVendorPaymentAcknowledgmentOpsService>(fake);
            });
    }

    private static VendorPaymentAcknowledgmentRecord Record(string status) =>
        new(
            AcknowledgmentId,
            PaymentAttemptId,
            PaymentConfirmationId,
            ParkingSessionId,
            "HIKCENTRAL",
            "HIK:CARD-279D",
            "TICKET-279D",
            "CARD-279D",
            status,
            "128",
            "Vendor retry pending.",
            5000,
            "PHP",
            null,
            null,
            2,
            CreatedAt.AddMinutes(5),
            CreatedAt.AddMinutes(-1),
            "internal-idempotency-key-not-exposed",
            CorrelationId,
            CreatedAt,
            CreatedAt.AddMinutes(5));

    private sealed class FakeVendorPaymentAcknowledgmentOpsService : IVendorPaymentAcknowledgmentOpsService
    {
        public bool ReturnMissing { get; init; }

        public SearchVendorPaymentAcknowledgmentsQuery? LastSearchQuery { get; private set; }

        public VendorPaymentAcknowledgmentSearchResult SearchResult { get; init; } =
            new(
                [Record(VendorPaymentAcknowledgmentStatuses.RetryPending)],
                new VendorPaymentAcknowledgmentStatusBucketCounts(0, 1, 0, 0, 0, 0),
                PageIndex: 0,
                PageSize: 25,
                HasMore: false);

        public Task<VendorPaymentAcknowledgmentSearchResult> SearchAsync(
            SearchVendorPaymentAcknowledgmentsQuery query,
            CancellationToken cancellationToken)
        {
            LastSearchQuery = query;
            return Task.FromResult(SearchResult);
        }

        public Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
            Guid vendorPaymentAcknowledgmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnMissing ? null : Record(VendorPaymentAcknowledgmentStatuses.RetryPending));
    }
}
