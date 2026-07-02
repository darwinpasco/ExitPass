using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PosServerFiscalDocumentRequestMapperTests
{
    private readonly PosServerFiscalDocumentRequestMapper _sut = new();

    [Fact]
    public void Map_WhenContextIsValid_MapsRequiredPosServerFields()
    {
        var context = ValidContext();

        var result = _sut.Map(context);

        result.SitePosServerId.Should().Be(context.SitePosServerId);
        result.SitePosServerRef.Should().Be(context.SitePosServerRef);
        result.FiscalDocumentTypeCodeId.Should().Be(context.FiscalDocumentTypeCodeId);
        result.FiscalDocumentTypeCodeKey.Should().Be(context.FiscalDocumentTypeCodeKey);
        result.FiscalDocumentStatusCodeId.Should().Be(context.FiscalDocumentStatusCodeId);
        result.BusinessDayDate.Should().Be(context.BusinessDayDate);
        result.CentralPmsParkingSessionRef.Should().Be(context.CentralPmsParkingSessionRef);
        result.CentralPmsPaymentAttemptRef.Should().Be(context.CentralPmsPaymentAttemptRef);
        result.CentralPmsPaymentConfirmationRef.Should().Be(context.CentralPmsPaymentConfirmationRef);
    }

    [Fact]
    public void Map_MapsUpstreamFinalityReferenceToPayableBasisAndTopLevelFallback()
    {
        var context = ValidContext();

        var result = _sut.Map(context);

        result.UpstreamFinalityRef.Should().Be(context.PayableBasis.UpstreamFinalityRef);
        result.PayableBasis.UpstreamFinalityRef.Should().Be(context.PayableBasis.UpstreamFinalityRef);
        result.PayableBasis.PayableBasisRef.Should().Be(context.PayableBasis.PayableBasisRef);
    }

    [Fact]
    public void Map_MapsMinimalLineTenderTaxAndTotalFacts()
    {
        var result = _sut.Map(ValidContext());

        result.DocumentLines.Should().ContainSingle();
        result.Lines.Should().BeEquivalentTo(result.DocumentLines);
        result.Tenders.Should().ContainSingle();
        result.TaxDetails.Should().ContainSingle();
        result.Totals.Should().ContainSingle();

        result.DocumentLines[0].CurrencyCode.Should().Be("PHP");
        result.Tenders[0].AmountMinorUnits.Should().Be(12500);
        result.TaxDetails[0].TaxAmountMinorUnits.Should().Be(1200);
        result.Totals[0].AmountMinorUnits.Should().Be(12500);
    }

    [Fact]
    public void Map_MapsApprovedDiscountReferencesAsReferencesOnly()
    {
        var result = _sut.Map(ValidContext());

        result.PayableBasis.DiscountReferences.Should().ContainSingle();
        result.PayableBasis.DiscountReferences[0].DiscountValidationRef.Should().Be("discount-validation-ref");
        result.DiscountPrivilegeDetails.Should().ContainSingle();
        result.DiscountPrivilegeDetails[0].EvidenceRef.Should().Be("evidence-ref");
        result.DiscountPrivilegeDetails[0].ApprovalRef.Should().Be("approval-ref");
    }

    [Fact]
    public void RequestModels_DoNotExposeRawSensitivePayloadProperties()
    {
        var sensitiveTerms = new[] { "Pan", "Cvv", "Secret", "Token", "Credential", "Raw", "CallbackPayload" };
        var modelTypes = typeof(PosServerFiscalDocumentCreateRequest).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(PosServerFiscalDocumentCreateRequest).Namespace)
            .Where(type => type.Name.StartsWith("PosServer", StringComparison.Ordinal));

        var sensitivePropertyNames = modelTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Where(name => sensitiveTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        sensitivePropertyNames.Should().BeEmpty();
    }

    [Fact]
    public void Map_WhenSensitiveReferenceTermIsPresent_RejectsRequest()
    {
        var context = ValidContext() with
        {
            ReferenceContext = new Dictionary<string, string> { ["raw_payload"] = "not-allowed" }
        };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("sensitive_payload_reference_rejected");
    }

    [Fact]
    public void Map_WhenUpstreamFinalityReferenceIsMissing_RejectsRequest()
    {
        var context = ValidContext() with
        {
            PayableBasis = ValidContext().PayableBasis with { UpstreamFinalityRef = "" }
        };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("upstream_finality_reference_required");
    }

    [Fact]
    public void Map_WhenPayableBasisRefIsMissing_RejectsRequest()
    {
        var context = ValidContext() with
        {
            PayableBasis = ValidContext().PayableBasis with { PayableBasisRef = "" }
        };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("payable_basis_ref_required");
    }

    [Fact]
    public void Map_WhenDocumentLinesAreMissing_RejectsRequest()
    {
        var context = ValidContext() with { DocumentLines = Array.Empty<CentralPmsFiscalDocumentLineContext>() };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("document_line_required");
    }

    [Fact]
    public void Map_WhenTendersAreMissing_RejectsRequest()
    {
        var context = ValidContext() with { Tenders = Array.Empty<CentralPmsFiscalTenderContext>() };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("tender_required");
    }

    internal static CentralPmsFiscalDocumentMappingContext ValidContext() =>
        new(
            SitePosServerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SitePosServerRef: "site-pos-server-main",
            FiscalDocumentTypeCodeId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            FiscalDocumentStatusCodeId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            BusinessDayDate: new DateOnly(2026, 7, 2),
            CentralPmsParkingSessionRef: "parking-session-ref",
            CentralPmsPaymentAttemptRef: "payment-attempt-ref",
            CentralPmsPaymentConfirmationRef: "payment-confirmation-ref",
            PayableBasis: new CentralPmsPayableBasisContext(
                PayableBasisRef: "payable-basis-ref",
                UpstreamFinalityRef: "upstream-finality-ref",
                CurrencyCode: "php",
                PayableAmountMinorUnits: 12500,
                DiscountReferences:
                [
                    new CentralPmsFiscalDiscountReferenceContext(
                        DiscountValidationRef: "discount-validation-ref",
                        Status: "approved",
                        AppliesStatutoryDiscountTreatment: true,
                        ReferenceContext: new Dictionary<string, string> { ["source"] = "discount-workflow" })
                ],
                ReferenceContext: new Dictionary<string, string> { ["tariffSnapshotRef"] = "tariff-snapshot-ref" }),
            DocumentLines:
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Description: "Parking fee",
                    Quantity: 1,
                    UnitAmountMinorUnits: 12500,
                    GrossAmountMinorUnits: 12500,
                    DiscountAmountMinorUnits: 0,
                    TaxAmountMinorUnits: 1200,
                    NetAmountMinorUnits: 11300,
                    CurrencyCode: "php",
                    LineStatusCodeId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    SourceRef: "line-source-ref",
                    LineContext: new Dictionary<string, string> { ["category"] = "parking" })
            ],
            Tenders:
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    AmountMinorUnits: 12500,
                    CurrencyCode: "php",
                    CentralPmsPaymentAttemptRef: "payment-attempt-ref",
                    CentralPmsPaymentConfirmationRef: "payment-confirmation-ref",
                    PaymentFinalityRef: "payment-finality-ref",
                    ProviderRef: "provider-ref",
                    TenderContext: new Dictionary<string, string> { ["channel"] = "webpay" })
            ],
            TaxDetails:
            [
                new CentralPmsFiscalTaxDetailContext(
                    TaxTypeCodeId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    TaxClassificationCodeId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    TaxableAmountMinorUnits: 11300,
                    TaxAmountMinorUnits: 1200,
                    CurrencyCode: "php",
                    LineSequence: 1,
                    TaxRate: 12m,
                    TaxContext: new Dictionary<string, string> { ["taxClass"] = "vatable" })
            ],
            DiscountPrivilegeDetails:
            [
                new CentralPmsFiscalDiscountPrivilegeDetailContext(
                    DiscountPrivilegeTypeCodeId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    BasisAmountMinorUnits: 12500,
                    DiscountAmountMinorUnits: 0,
                    VatPrivilegeAmountMinorUnits: 0,
                    CurrencyCode: "php",
                    LineSequence: 1,
                    BeneficiaryRef: "beneficiary-ref",
                    EvidenceRef: "evidence-ref",
                    ApprovalRef: "approval-ref",
                    DiscountPrivilegeContext: new Dictionary<string, string> { ["classification"] = "statutory" })
            ],
            Totals:
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    AmountMinorUnits: 12500,
                    CurrencyCode: "php",
                    TotalContext: new Dictionary<string, string> { ["kind"] = "grand_total" })
            ],
            ReferenceContext: new Dictionary<string, string> { ["correlation"] = "correlation-ref" },
            PaymentFinalityRef: "payment-finality-ref",
            VendorAckRef: null);
}
