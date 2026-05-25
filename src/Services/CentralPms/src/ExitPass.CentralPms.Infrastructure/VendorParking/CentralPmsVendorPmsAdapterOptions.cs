namespace ExitPass.CentralPms.Infrastructure.VendorParking;

/// <summary>
/// Central PMS configuration for selecting the Vendor PMS Adapter implementation used by parking lookup.
/// </summary>
/// <remarks>
/// BRD v1.2: supports vendor PMS parking session lookup and tariff calculation.
/// SDD v1.2: keeps vendor-specific integration behind the Vendor PMS Adapter boundary.
/// Invariant: adapter selection cannot mutate payment, provider, exit, gate, settlement, or payout truth.
/// </remarks>
public sealed class CentralPmsVendorPmsAdapterOptions
{
    /// <summary>
    /// Configuration section name for Central PMS vendor PMS adapter options.
    /// </summary>
    public const string SectionName = "CentralPms:VendorPms";

    /// <summary>
    /// Mock adapter provider code used by local development and automated tests.
    /// </summary>
    public const string MockProvider = "MOCK";

    /// <summary>
    /// HikCentral adapter provider code.
    /// </summary>
    public const string HikCentralProvider = "HIKCENTRAL";

    /// <summary>
    /// Gets or sets the provider selected for Central PMS vendor parking resolution.
    /// </summary>
    public string Provider { get; set; } = MockProvider;

    /// <summary>
    /// Returns the normalized configured provider code.
    /// </summary>
    public string NormalizedProvider()
    {
        return string.IsNullOrWhiteSpace(Provider)
            ? MockProvider
            : Provider.Trim().ToUpperInvariant();
    }
}
