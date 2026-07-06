using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

internal sealed record PosServerSemanticHashSha256V1Fixture(
    string CanonicalSourceVersion,
    string HashAlgorithm,
    string ExpectedSha256Hash,
    string CanonicalSourceText,
    PosServerFiscalDocumentCreateRequest RepresentativeCreateRequest)
{
    public const string FileName = "pos_server_semantic_hash_sha256_v1_representative_fixture.json";
    public const string RelativePath =
        "src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/Fixtures/" + FileName;

    public FiscalSemanticRequestHashParityFixture ToParityFixture() =>
        new(
            PosServerHashSourceVersion: CanonicalSourceVersion,
            PosServerCanonicalSourceText: CanonicalSourceText,
            PosServerSemanticRequestHash: ExpectedSha256Hash);

    public string ComputeCanonicalSourceSha256LowerHex() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalSourceText))).ToLowerInvariant();

    public static PosServerSemanticHashSha256V1Fixture Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ResolvePath()));
        var root = document.RootElement;
        var facts = root.GetProperty("representative_create_request_facts");

        return new PosServerSemanticHashSha256V1Fixture(
            CanonicalSourceVersion: root.GetProperty("canonical_source_version").GetString()
                ?? throw new InvalidDataException("Fixture canonical_source_version is missing."),
            HashAlgorithm: root.GetProperty("hash_algorithm").GetString()
                ?? throw new InvalidDataException("Fixture hash_algorithm is missing."),
            ExpectedSha256Hash: root.GetProperty("expected_sha256_hash").GetString()
                ?? throw new InvalidDataException("Fixture expected_sha256_hash is missing."),
            CanonicalSourceText: root.GetProperty("canonical_source_text").GetString()
                ?? throw new InvalidDataException("Fixture canonical_source_text is missing."),
            RepresentativeCreateRequest: MapCreateRequest(facts));
    }

    private static PosServerFiscalDocumentCreateRequest MapCreateRequest(JsonElement facts)
    {
        var payableBasis = MapPayableBasis(facts.GetProperty("payableBasis"));

        return new PosServerFiscalDocumentCreateRequest(
            SitePosServerRef: GetString(facts, "sitePosServerRef"),
            FiscalDocumentTypeCodeKey: GetString(facts, "fiscalDocumentTypeCodeKey"),
            PayableBasis: payableBasis,
            SitePosServerId: GetGuid(facts, "sitePosServerId"),
            ChannelTerminalId: GetGuid(facts, "channelTerminalId"),
            FiscalDocumentTypeCodeId: GetGuid(facts, "fiscalDocumentTypeCodeId"),
            FiscalDocumentStatusCodeId: GetGuid(facts, "fiscalDocumentStatusCodeId"),
            BusinessDayDate: GetDateOnly(facts, "businessDayDate"),
            CentralPmsParkingSessionRef: GetRequiredString(facts, "centralPmsParkingSessionRef"),
            CentralPmsPaymentAttemptRef: GetRequiredString(facts, "centralPmsPaymentAttemptRef"),
            CentralPmsPaymentConfirmationRef: GetRequiredString(facts, "centralPmsPaymentConfirmationRef"),
            UpstreamFinalityRef: payableBasis.UpstreamFinalityRef,
            PaymentFinalityRef: GetString(facts, "paymentFinalityRef"),
            VendorAckRef: GetString(facts, "vendorAckRef"),
            DocumentLines: facts.GetProperty("documentLines").EnumerateArray().Select(MapLine).ToArray(),
            Lines: facts.GetProperty("documentLines").EnumerateArray().Select(MapLine).ToArray(),
            Tenders: facts.GetProperty("tenders").EnumerateArray().Select(MapTender).ToArray(),
            TaxDetails: facts.GetProperty("taxDetails").EnumerateArray().Select(MapTaxDetail).ToArray(),
            DiscountPrivilegeDetails: facts.GetProperty("discountPrivilegeDetails").EnumerateArray()
                .Select(MapDiscountPrivilege).ToArray(),
            Totals: facts.GetProperty("totals").EnumerateArray().Select(MapTotal).ToArray(),
            ReferenceContext: MapDictionary(facts.GetProperty("referenceContext")));
    }

    private static PosServerPayableBasisRequest MapPayableBasis(JsonElement element) =>
        new(
            PayableBasisRef: GetRequiredString(element, "payableBasisRef"),
            UpstreamFinalityRef: GetRequiredString(element, "upstreamFinalityRef"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            PayableAmountMinorUnits: GetInt64(element, "payableAmountMinorUnits"),
            DiscountReferences: element.GetProperty("discountReferences").EnumerateArray()
                .Select(MapDiscountReference).ToArray(),
            ReferenceContext: MapDictionary(element.GetProperty("referenceContext")));

    private static PosServerFiscalDiscountReferenceRequest MapDiscountReference(JsonElement element) =>
        new(
            DiscountValidationRef: GetRequiredString(element, "discountValidationRef"),
            Status: GetRequiredString(element, "status"),
            AppliesStatutoryDiscountTreatment: element.GetProperty("appliesStatutoryDiscountTreatment").GetBoolean(),
            ReferenceContext: MapDictionary(element.GetProperty("referenceContext")));

    private static PosServerFiscalDocumentLineRequest MapLine(JsonElement element) =>
        new(
            LineSequence: GetInt32(element, "lineSequence"),
            LineTypeCodeId: GetGuid(element, "lineTypeCodeId"),
            Description: GetRequiredString(element, "description"),
            Quantity: GetDecimal(element, "quantity"),
            UnitAmountMinorUnits: GetInt64(element, "unitAmountMinorUnits"),
            GrossAmountMinorUnits: GetInt64(element, "grossAmountMinorUnits"),
            DiscountAmountMinorUnits: GetInt64(element, "discountAmountMinorUnits"),
            TaxAmountMinorUnits: GetInt64(element, "taxAmountMinorUnits"),
            NetAmountMinorUnits: GetInt64(element, "netAmountMinorUnits"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            LineStatusCodeId: GetGuid(element, "lineStatusCodeId"),
            SourceRef: GetString(element, "sourceRef"),
            LineContext: MapDictionary(element.GetProperty("lineContext")));

    private static PosServerFiscalTenderRequest MapTender(JsonElement element) =>
        new(
            TenderTypeCodeId: GetGuid(element, "tenderTypeCodeId"),
            AmountMinorUnits: GetInt64(element, "amountMinorUnits"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            CentralPmsPaymentAttemptRef: GetString(element, "centralPmsPaymentAttemptRef"),
            CentralPmsPaymentConfirmationRef: GetString(element, "centralPmsPaymentConfirmationRef"),
            PaymentFinalityRef: GetString(element, "paymentFinalityRef"),
            ProviderRef: GetString(element, "providerRef"),
            TenderContext: MapDictionary(element.GetProperty("tenderContext")));

    private static PosServerFiscalTaxDetailRequest MapTaxDetail(JsonElement element) =>
        new(
            TaxTypeCodeId: GetGuid(element, "taxTypeCodeId"),
            TaxClassificationCodeId: GetGuid(element, "taxClassificationCodeId"),
            TaxableAmountMinorUnits: GetInt64(element, "taxableAmountMinorUnits"),
            TaxAmountMinorUnits: GetInt64(element, "taxAmountMinorUnits"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            LineSequence: GetNullableInt32(element, "lineSequence"),
            TaxRate: GetNullableDecimal(element, "taxRate"),
            TaxContext: MapDictionary(element.GetProperty("taxContext")));

    private static PosServerFiscalDiscountPrivilegeDetailRequest MapDiscountPrivilege(JsonElement element) =>
        new(
            DiscountPrivilegeTypeCodeId: GetGuid(element, "discountPrivilegeTypeCodeId"),
            BasisAmountMinorUnits: GetInt64(element, "basisAmountMinorUnits"),
            DiscountAmountMinorUnits: GetInt64(element, "discountAmountMinorUnits"),
            VatPrivilegeAmountMinorUnits: GetInt64(element, "vatPrivilegeAmountMinorUnits"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            LineSequence: GetNullableInt32(element, "lineSequence"),
            BeneficiaryRef: GetString(element, "beneficiaryRef"),
            EvidenceRef: GetString(element, "evidenceRef"),
            ApprovalRef: GetString(element, "approvalRef"),
            DiscountPrivilegeContext: MapDictionary(element.GetProperty("discountPrivilegeContext")));

    private static PosServerFiscalTotalRequest MapTotal(JsonElement element) =>
        new(
            TotalTypeCodeId: GetGuid(element, "totalTypeCodeId"),
            AmountMinorUnits: GetInt64(element, "amountMinorUnits"),
            CurrencyCode: GetRequiredString(element, "currencyCode"),
            TotalContext: MapDictionary(element.GetProperty("totalContext")));

    private static IReadOnlyDictionary<string, string> MapDictionary(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty,
            StringComparer.Ordinal);

    private static string ResolvePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "Services",
                "CentralPms",
                "tests",
                "ExitPass.CentralPms.UnitTests",
                "FiscalIssuance",
                "Fixtures",
                FileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate fixture {RelativePath}.");
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetString(element, propertyName) ?? throw new InvalidDataException($"Fixture property {propertyName} is missing.");

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static Guid? GetGuid(JsonElement element, string propertyName)
    {
        var text = GetString(element, propertyName);
        return string.IsNullOrWhiteSpace(text) ? null : Guid.Parse(text);
    }

    private static DateOnly? GetDateOnly(JsonElement element, string propertyName)
    {
        var text = GetString(element, propertyName);
        return string.IsNullOrWhiteSpace(text) ? null : DateOnly.Parse(text);
    }

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetInt32();

    private static int? GetNullableInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt32()
            : null;

    private static long GetInt64(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetInt64();

    private static decimal GetDecimal(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetDecimal();

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDecimal()
            : null;
}
