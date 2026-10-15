using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class CustomerDigitalSalesInvoiceApiIntegrationTests
{
    private const string Endpoint = "/v1/webpay/digital-sales-invoices/presentation";

    [Fact]
    public async Task Presentation_WithValidCapability_IsAnonymousAndReturnsCanonicalDocument()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { accessToken = "valid-capability" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("fiscalDocumentNumber").GetString().Should().Be("SI-00000105");
        body.RootElement.GetProperty("canonicalText").GetString().Should().Be("SALES INVOICE\n===== NOTHING FOLLOWS =====");
    }

    [Theory]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("expired-capability")]
    [InlineData("tampered-capability")]
    public async Task Presentation_WithoutValidCapability_FailsClosed(string accessToken)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { accessToken });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    private static CustomWebApplicationFactory CreateFactory() =>
        new CustomWebApplicationFactory().WithServiceOverrides(services =>
        {
            services.RemoveAll<ICustomerDigitalSalesInvoiceCapabilityService>();
            services.AddSingleton<ICustomerDigitalSalesInvoiceCapabilityService>(new FakeCapabilityService());
        });

    private sealed class FakeCapabilityService : ICustomerDigitalSalesInvoiceCapabilityService
    {
        public CustomerDigitalSalesInvoiceCapability Create(Guid fiscalIssuanceReferenceId, Guid siteId) =>
            throw new NotSupportedException();

        public Task<CustomerDigitalSalesInvoicePresentation?> ResolveAsync(
            string token,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(token, "valid-capability", StringComparison.Ordinal))
            {
                return Task.FromResult<CustomerDigitalSalesInvoicePresentation?>(null);
            }

            var presentation = JsonSerializer.SerializeToElement(new { documentTitle = "Sales Invoice" });
            return Task.FromResult<CustomerDigitalSalesInvoicePresentation?>(new(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                "SI-00000105",
                presentation,
                "SALES INVOICE\n===== NOTHING FOLLOWS ====="));
        }

        public Task<CustomerDigitalSalesInvoicePresentation?> ReadAsync(
            Guid fiscalIssuanceReferenceId,
            Guid expectedSiteId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomerDigitalSalesInvoicePresentation?>(null);
    }
}
