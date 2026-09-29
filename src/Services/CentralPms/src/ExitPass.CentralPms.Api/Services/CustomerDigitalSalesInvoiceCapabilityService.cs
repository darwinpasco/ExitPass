using System.Security.Cryptography;
using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Services;

public sealed class CustomerDigitalSalesInvoiceCapabilityOptions
{
    public const string SectionName = "CustomerDigitalSalesInvoiceCapability";

    public int LifetimeHours { get; set; } = 24;

    public string PublicPagePath { get; set; } = "/webpay/sales-invoice";
}

public sealed record CustomerDigitalSalesInvoiceCapability(
    string Token,
    string PublicPagePath,
    DateTimeOffset ExpiresAt);

public sealed record CustomerDigitalSalesInvoicePresentation(
    Guid FiscalIssuanceReferenceId,
    Guid SiteId,
    JsonElement Presentation,
    string CanonicalText);

public interface ICustomerDigitalSalesInvoiceCapabilityService
{
    CustomerDigitalSalesInvoiceCapability Create(Guid fiscalIssuanceReferenceId, Guid siteId);

    Task<CustomerDigitalSalesInvoicePresentation?> ResolveAsync(
        string token,
        CancellationToken cancellationToken);

    Task<CustomerDigitalSalesInvoicePresentation?> ReadAsync(
        Guid fiscalIssuanceReferenceId,
        Guid expectedSiteId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Issues a time-limited bearer capability for exactly one POS-owned Digital Sales Invoice.
/// The protected payload contains only internal routing identifiers and never customer data.
/// </summary>
public sealed class CustomerDigitalSalesInvoiceCapabilityService : ICustomerDigitalSalesInvoiceCapabilityService
{
    public const string ProtectorPurpose = "ExitPass.CustomerDigitalSalesInvoiceCapability.v1";
    private const int PayloadVersion = 1;

    private readonly ITimeLimitedDataProtector _protector;
    private readonly CustomerDigitalSalesInvoiceCapabilityOptions _options;
    private readonly IFiscalIssuanceReferenceRepository _references;
    private readonly IPosServerFiscalDocumentClient _posServer;
    private readonly TimeProvider _timeProvider;

    public CustomerDigitalSalesInvoiceCapabilityService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<CustomerDigitalSalesInvoiceCapabilityOptions> options,
        IFiscalIssuanceReferenceRepository references,
        IPosServerFiscalDocumentClient posServer,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        _options = options.Value;
        _references = references;
        _posServer = posServer;
        _timeProvider = timeProvider;
    }

    public CustomerDigitalSalesInvoiceCapability Create(Guid fiscalIssuanceReferenceId, Guid siteId)
    {
        if (fiscalIssuanceReferenceId == Guid.Empty || siteId == Guid.Empty)
        {
            throw new ArgumentException("A fiscal issuance reference and Site are required.");
        }

        var lifetime = TimeSpan.FromHours(_options.LifetimeHours);
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);
        var payload = JsonSerializer.Serialize(new CapabilityPayload(
            PayloadVersion,
            fiscalIssuanceReferenceId,
            siteId));
        var token = _protector.Protect(payload, lifetime);

        return new CustomerDigitalSalesInvoiceCapability(
            token,
            NormalizePublicPagePath(_options.PublicPagePath),
            expiresAt);
    }

    public async Task<CustomerDigitalSalesInvoicePresentation?> ResolveAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        CapabilityPayload? payload;
        try
        {
            var serialized = _protector.Unprotect(token.Trim(), out var expiresAt);
            if (expiresAt <= _timeProvider.GetUtcNow())
            {
                return null;
            }

            payload = JsonSerializer.Deserialize<CapabilityPayload>(serialized);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }

        if (payload is null || payload.Version != PayloadVersion ||
            payload.FiscalIssuanceReferenceId == Guid.Empty || payload.SiteId == Guid.Empty)
        {
            return null;
        }

        return await ReadAsync(
                payload.FiscalIssuanceReferenceId,
                payload.SiteId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CustomerDigitalSalesInvoicePresentation?> ReadAsync(
        Guid fiscalIssuanceReferenceId,
        Guid expectedSiteId,
        CancellationToken cancellationToken)
    {
        var reference = await _references.FindByFiscalIssuanceReferenceIdAsync(
                fiscalIssuanceReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (reference is null || reference.SiteId != expectedSiteId ||
            reference.PosServerFiscalDocumentId is null ||
            reference.PosServerFiscalDocumentId == Guid.Empty)
        {
            return null;
        }

        PosServerRoutingContext routing;
        try
        {
            routing = PosServerRoutingContext.Create(
                reference.SitePosServerId,
                reference.SitePosServerRef);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var read = await _posServer.GetFiscalDocumentPresentationAsync(
                reference.PosServerFiscalDocumentId.Value,
                reference.CorrelationId,
                routing,
                cancellationToken)
            .ConfigureAwait(false);

        if (!read.Succeeded || read.FiscalDocumentId != reference.PosServerFiscalDocumentId ||
            read.AuthoritativeResponse is null ||
            !read.AuthoritativeResponse.Value.TryGetProperty("presentation", out var presentation) ||
            presentation.ValueKind != JsonValueKind.Object ||
            !read.AuthoritativeResponse.Value.TryGetProperty("canonicalText", out var canonicalTextElement) ||
            canonicalTextElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(canonicalTextElement.GetString()))
        {
            return null;
        }

        return new CustomerDigitalSalesInvoicePresentation(
            reference.FiscalIssuanceReferenceId,
            expectedSiteId,
            presentation.Clone(),
            canonicalTextElement.GetString()!);
    }

    private static string NormalizePublicPagePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/webpay/sales-invoice" : value.Trim();
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private sealed record CapabilityPayload(
        int Version,
        Guid FiscalIssuanceReferenceId,
        Guid SiteId);
}
