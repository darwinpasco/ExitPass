using System.Text.Json;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class CustomerDigitalSalesInvoiceCapabilityServiceTests : IDisposable
{
    private static readonly Guid ReferenceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PosDocumentId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private readonly DirectoryInfo _keyDirectory = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"exitpass-si-capability-{Guid.NewGuid():N}"));

    [Fact]
    public async Task Capability_ResolvesOnlyItsSiteBoundCanonicalPresentation()
    {
        var fixture = CreateFixture();
        var capability = fixture.Service.Create(ReferenceId, SiteId);

        var result = await fixture.Service.ResolveAsync(capability.Token, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FiscalIssuanceReferenceId.Should().Be(ReferenceId);
        result.SiteId.Should().Be(SiteId);
        result.Presentation.GetProperty("documentTitle").GetString().Should().Be("Sales Invoice");
        capability.PublicPagePath.Should().Be("/webpay/sales-invoice");
        capability.Token.Should().NotContain(ReferenceId.ToString("D"));
        capability.Token.Should().NotContain(SiteId.ToString("D"));
    }

    [Fact]
    public async Task Capability_WhenReferenceSiteDiffers_FailsClosed()
    {
        var fixture = CreateFixture(referenceSiteId: Guid.Parse("44444444-4444-4444-8444-444444444444"));
        var token = fixture.Service.Create(ReferenceId, SiteId).Token;

        (await fixture.Service.ResolveAsync(token, CancellationToken.None)).Should().BeNull();
        await fixture.PosClient.DidNotReceiveWithAnyArgs()
            .GetFiscalDocumentPresentationAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Capability_WhenMalformedOrTampered_FailsClosed()
    {
        var fixture = CreateFixture();
        var validToken = fixture.Service.Create(ReferenceId, SiteId).Token;

        (await fixture.Service.ResolveAsync("not-a-protected-token", CancellationToken.None)).Should().BeNull();
        (await fixture.Service.ResolveAsync(validToken + "tampered", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Capability_WhenExpired_FailsClosed()
    {
        var fixture = CreateFixture();
        var payload = JsonSerializer.Serialize(new { version = 1, fiscalIssuanceReferenceId = ReferenceId, siteId = SiteId });
        var token = fixture.Provider.CreateProtector(CustomerDigitalSalesInvoiceCapabilityService.ProtectorPurpose)
            .ToTimeLimitedDataProtector()
            .Protect(payload, TimeSpan.FromSeconds(-1));

        (await fixture.Service.ResolveAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Capability_WhenPurposeOrVersionIsWrong_FailsClosed()
    {
        var fixture = CreateFixture();
        var wrongPurpose = fixture.Provider.CreateProtector("ExitPass.UnrelatedPurpose")
            .ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new { version = 1, fiscalIssuanceReferenceId = ReferenceId, siteId = SiteId }), TimeSpan.FromHours(1));
        var wrongVersion = fixture.Provider.CreateProtector(CustomerDigitalSalesInvoiceCapabilityService.ProtectorPurpose)
            .ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new { version = 2, fiscalIssuanceReferenceId = ReferenceId, siteId = SiteId }), TimeSpan.FromHours(1));

        (await fixture.Service.ResolveAsync(wrongPurpose, CancellationToken.None)).Should().BeNull();
        (await fixture.Service.ResolveAsync(wrongVersion, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Capability_WhenReferenceIsMissing_FailsClosed()
    {
        var fixture = CreateFixture(referenceExists: false);
        var token = fixture.Service.Create(ReferenceId, SiteId).Token;

        (await fixture.Service.ResolveAsync(token, CancellationToken.None)).Should().BeNull();
    }

    public void Dispose() => _keyDirectory.Delete(recursive: true);

    private Fixture CreateFixture(Guid? referenceSiteId = null, bool referenceExists = true)
    {
        var provider = DataProtectionProvider.Create(_keyDirectory, configuration =>
            configuration.SetApplicationName("ExitPass.CentralPms.Tests"));
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        if (referenceExists)
        {
            repository.FindByFiscalIssuanceReferenceIdAsync(ReferenceId, Arg.Any<CancellationToken>())
                .Returns(Reference(referenceSiteId ?? SiteId));
        }

        var posClient = Substitute.For<IPosServerFiscalDocumentClient>();
        posClient.GetFiscalDocumentPresentationAsync(
                PosDocumentId,
                Arg.Any<Guid?>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Presentation());

        var service = new CustomerDigitalSalesInvoiceCapabilityService(
            provider,
            Options.Create(new CustomerDigitalSalesInvoiceCapabilityOptions()),
            repository,
            posClient,
            TimeProvider.System);
        return new Fixture(provider, posClient, service);
    }

    private static FiscalIssuanceReferenceRecord Reference(Guid siteId) => new(
        ReferenceId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        siteId,
        Guid.Parse("55555555-5555-4555-8555-555555555555"),
        "PITX-L3-WEBPAY-01",
        "payable-basis",
        "payment-finality",
        PosDocumentId,
        null,
        null,
        49,
        "SI-00000049",
        null,
        null,
        null,
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        null,
        Guid.NewGuid(),
        FiscalIssuanceResultClassification.NewlyCreated,
        FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
        FiscalNumberAssignmentState.Assigned,
        FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
        null,
        null,
        null,
        Guid.NewGuid(),
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        null);

    private static PosServerFiscalDocumentPresentationReadResult Presentation() => new(
        PosServerFiscalDocumentOutcome.Accepted,
        true,
        200,
        "OK",
        "Canonical presentation returned.",
        PosDocumentId,
        "SI-00000049",
        "RECORDED",
        "ASSIGNED",
        Guid.NewGuid(),
        "SALES_INVOICE",
        Guid.NewGuid(),
        null,
        null,
        null,
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
        null,
        null,
        null,
        "digital-sales-invoice-presentation-json-v1",
        "digital-sales-invoice-json-v1",
        "application/json",
        JsonSerializer.SerializeToElement(new
        {
            presentation = new
            {
                documentTitle = "Sales Invoice",
                sections = Array.Empty<object>()
            },
            canonicalText = "SALES INVOICE\nSI No: SI-00000049",
            fiscalDocumentId = PosDocumentId
        }));

    private sealed record Fixture(
        IDataProtectionProvider Provider,
        IPosServerFiscalDocumentClient PosClient,
        CustomerDigitalSalesInvoiceCapabilityService Service);
}
