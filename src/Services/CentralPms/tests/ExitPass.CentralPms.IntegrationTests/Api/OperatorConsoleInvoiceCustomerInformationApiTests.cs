using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleInvoiceCustomerInformationApiTests
{
    private static readonly Guid UserId = Guid.Parse("b31ad0fb-3300-4c0a-9ae5-819f472fbe10");
    private static readonly Guid SiteId = Guid.Parse("b31ad0fb-3300-4c0a-9ae5-819f472fbe11");
    private static readonly Guid SiteGroupId = Guid.Parse("b31ad0fb-3300-4c0a-9ae5-819f472fbe12");
    private static readonly Guid ParkingSessionId = Guid.Parse("b31ad0fb-3300-4c0a-9ae5-819f472fbe13");

    [Fact]
    public async Task Get_NoRecord_ReturnsControlledNotSuppliedResponse()
    {
        var fake = new FakeService { ReadResult = new(InvoiceCustomerInformationReadStatus.NotSupplied, null) };
        using var factory = Factory(fake);
        using var client = Client(factory, "sales-invoice-customer-information.read");

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleInvoiceCustomerInformationResponse>();
        body.Should().NotBeNull();
        body!.HasCustomerInformation.Should().BeFalse();
        body.RowVersion.Should().BeNull();
    }

    [Fact]
    public async Task Put_DerivesActorScopeAndSourceFromServerBoundary()
    {
        var record = Record(1);
        var fake = new FakeService { SaveResult = new(InvoiceCustomerInformationSaveStatus.Created, record, []) };
        using var factory = Factory(fake);
        using var client = Client(factory, "sales-invoice-customer-information.manage");

        var response = await client.PutAsJsonAsync(Route, new SaveOperatorConsoleInvoiceCustomerInformationRequest(
            " ABC Corporation ", " Makati City ", " 123-456-789 ", " ABC Retail ", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.SaveCommand.Should().NotBeNull();
        fake.SaveCommand!.Actor.Should().Be(new InvoiceCustomerInformationActor(UserId, null, InvoiceCustomerInformationSourceChannels.OperatorConsole));
        fake.SaveCommand.Scope.Should().Be(new InvoiceCustomerInformationScope(SiteId, SiteGroupId));
        fake.SaveCommand.ParkingSessionId.Should().Be(ParkingSessionId);
    }

    [Fact]
    public async Task Put_VersionConflict_ReturnsSafeCurrentState()
    {
        var fake = new FakeService { SaveResult = new(InvoiceCustomerInformationSaveStatus.VersionConflict, Record(4), []) };
        using var factory = Factory(fake);
        using var client = Client(factory, "sales-invoice-customer-information.manage");

        var response = await client.PutAsJsonAsync(Route, new SaveOperatorConsoleInvoiceCustomerInformationRequest("Stale", null, null, null, 3));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CUSTOMER_INFORMATION_VERSION_CONFLICT");
        body.Details.Should().ContainKey("currentRowVersion").WhoseValue.ToString().Should().Be("4");
    }

    [Fact]
    public async Task Put_FiscalFinality_ReturnsControlledConflict()
    {
        var fake = new FakeService { SaveResult = new(InvoiceCustomerInformationSaveStatus.FiscalFinality, null, []) };
        using var factory = Factory(fake);
        using var client = Client(factory, "sales-invoice-customer-information.manage");

        var response = await client.PutAsJsonAsync(Route, new SaveOperatorConsoleInvoiceCustomerInformationRequest("Name", null, null, null, 1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CUSTOMER_INFORMATION_FISCAL_FINALITY");
    }

    [Theory]
    [InlineData("GET", "sales-invoice-customer-information.manage")]
    [InlineData("PUT", "sales-invoice-customer-information.read")]
    public async Task Route_MissingRequiredPermission_ReturnsForbidden(string method, string wrongPermission)
    {
        using var factory = Factory(new FakeService());
        using var client = Client(factory, wrongPermission);
        using var request = new HttpRequestMessage(new HttpMethod(method), Route);
        if (method == "PUT") request.Content = JsonContent.Create(new SaveOperatorConsoleInvoiceCustomerInformationRequest("Name", null, null, null, null));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task Route_MissingAuthentication_ReturnsUnauthorized()
    {
        using var factory = Factory(new FakeService());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_ClientAuthoredActorOrSourceProperties_AreRejected()
    {
        using var factory = Factory(new FakeService());
        using var client = Client(factory, "sales-invoice-customer-information.manage");
        using var content = JsonContent.Create(new
        {
            customerName = "Name",
            address = (string?)null,
            tin = (string?)null,
            businessStyle = (string?)null,
            expectedVersion = (long?)null,
            submittedByUserId = Guid.NewGuid(),
            sourceChannel = "WEBPAY"
        });

        var response = await client.PutAsync(Route, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Routes_ExposeDedicatedReadAndManagePolicies()
    {
        using var factory = Factory(new FakeService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("invoice-customer-information", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should().BeEquivalentTo([
                OperatorConsoleInvoiceCustomerInformationEndpoints.ReadPolicy,
                OperatorConsoleInvoiceCustomerInformationEndpoints.ManagePolicy]);
    }

    private static string Route => $"/v1/ops/operator-console/sessions/{ParkingSessionId}/invoice-customer-information";

    private static CustomWebApplicationFactory Factory(FakeService fake) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowFixtureIdentityHeaders"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IParkingSessionInvoiceCustomerInformationService>();
                services.AddSingleton<IParkingSessionInvoiceCustomerInformationService>(fake);
            });

    private static HttpClient Client(CustomWebApplicationFactory factory, string permission)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString());
        client.DefaultRequestHeaders.Add("X-Site-Group-Id", SiteGroupId.ToString());
        return client;
    }

    private static ParkingSessionInvoiceCustomerInformationRecord Record(long version) =>
        new(ParkingSessionId, "ABC Corporation", "Makati City", "123-456-789", "ABC Retail", version, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeService : IParkingSessionInvoiceCustomerInformationService
    {
        public InvoiceCustomerInformationReadResult ReadResult { get; init; } = new(InvoiceCustomerInformationReadStatus.NotSupplied, null);
        public InvoiceCustomerInformationSaveResult SaveResult { get; init; } = new(InvoiceCustomerInformationSaveStatus.Created, Record(1), []);
        public SaveParkingSessionInvoiceCustomerInformationCommand? SaveCommand { get; private set; }

        public Task<InvoiceCustomerInformationReadResult> ReadAsync(Guid parkingSessionId, InvoiceCustomerInformationScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult);

        public Task<InvoiceCustomerInformationSaveResult> SaveAsync(SaveParkingSessionInvoiceCustomerInformationCommand command, CancellationToken cancellationToken)
        {
            SaveCommand = command;
            return Task.FromResult(SaveResult);
        }
    }
}
