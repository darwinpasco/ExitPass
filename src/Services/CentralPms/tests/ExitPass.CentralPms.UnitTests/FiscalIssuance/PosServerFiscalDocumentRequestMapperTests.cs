using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PosServerFiscalDocumentRequestMapperTests
{
    private readonly PosServerFiscalDocumentRequestMapper _sut = new();

    [Fact]
    public void PosServerCreateRequest_ExposesTopLevelSiteIdForFiscalProfileResolution()
    {
        typeof(PosServerFiscalDocumentCreateRequest)
            .GetProperty("SiteId")
            .Should().NotBeNull();
    }

    [Fact]
    public void Map_WhenContextIsValid_MapsRequiredPosServerFields()
    {
        var context = ValidContext();

        var result = _sut.Map(context);

        result.SitePosServerId.Should().Be(context.SitePosServerId);
        result.SiteId.Should().Be(context.SiteId);
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
        result.DiscountPrivilegeDetails[0].BeneficiaryRef.Should().BeNull();
        result.DiscountPrivilegeDetails[0].EvidenceRef.Should().BeNull();
        result.DiscountPrivilegeDetails[0].ApprovalRef.Should().Be("approval-ref");
    }

    [Fact]
    public void Map_WhenCanonicalStatutoryDecisionFactsArePresent_MapsSafeTypedFiscalReferenceFields()
    {
        var decisionId = Guid.Parse("abababab-abab-4bab-8bab-abababababab");
        var policyId = Guid.Parse("bcbcbcbc-bcbc-4cbc-8cbc-bcbcbcbcbcbc");
        var originalSnapshotId = Guid.Parse("cdcdcdcd-cdcd-4dcd-8dcd-cdcdcdcdcdcd");
        var appliedSnapshotId = Guid.Parse("dededede-dede-4ede-8ede-dededededede");
        var decidedAt = DateTimeOffset.Parse("2026-07-21T08:02:00Z");
        var context = ValidContext() with
        {
            PayableBasis = ValidContext().PayableBasis with
            {
                DiscountReferences =
                [
                    new CentralPmsFiscalDiscountReferenceContext(
                        DiscountValidationRef: "discount-validation-ref",
                        Status: "approved",
                        AppliesStatutoryDiscountTreatment: true,
                        ReferenceContext: new Dictionary<string, string> { ["source"] = "discount-workflow" })
                    {
                        StatutoryDiscountDecisionCommandRef = decisionId.ToString("D"),
                        EntitlementType = "pwd",
                        AppliedPolicyReferenceRef = policyId.ToString("D"),
                        OriginalTariffSnapshotRef = originalSnapshotId.ToString("D"),
                        AppliedTariffSnapshotRef = appliedSnapshotId.ToString("D"),
                        OriginalAmountMinorUnits = 12500,
                        VatExclusiveBasisAmountMinorUnits = 11161,
                        VatTreatment = "vat_exclusive",
                        DiscountAmountMinorUnits = 2232,
                        FinalPayableAmountMinorUnits = 8929,
                        DecisionTimestamp = decidedAt,
                        SourceChannel = "webpay"
                    }
                ]
            }
        };

        var result = _sut.Map(context);

        var discount = result.PayableBasis.DiscountReferences.Should().ContainSingle().Subject;
        discount.StatutoryDiscountDecisionCommandRef.Should().Be(decisionId.ToString("D"));
        discount.EntitlementType.Should().Be("PWD");
        discount.AppliedPolicyReferenceRef.Should().Be(policyId.ToString("D"));
        discount.OriginalTariffSnapshotRef.Should().Be(originalSnapshotId.ToString("D"));
        discount.AppliedTariffSnapshotRef.Should().Be(appliedSnapshotId.ToString("D"));
        discount.OriginalAmountMinorUnits.Should().Be(12500);
        discount.VatExclusiveBasisAmountMinorUnits.Should().Be(11161);
        discount.VatTreatment.Should().Be("VAT_EXCLUSIVE");
        discount.DiscountAmountMinorUnits.Should().Be(2232);
        discount.FinalPayableAmountMinorUnits.Should().Be(8929);
        discount.DecisionTimestamp.Should().Be(decidedAt);
        discount.SourceChannel.Should().Be("WEBPAY");
    }

    [Fact]
    public void Map_WhenPwdStatutoryDiscountApplied_MapsDiscountedPayableAndPrivilegeMetadata()
    {
        var validationId = Guid.Parse("10101010-1010-4010-8010-101010101010");
        var applicationId = Guid.Parse("20202020-2020-4020-8020-202020202020");
        var appliedTariffSnapshotId = Guid.Parse("30303030-3030-4030-8030-303030303030");
        var context = ValidContext() with
        {
            PayableBasis = new CentralPmsPayableBasisContext(
                PayableBasisRef: appliedTariffSnapshotId.ToString("D"),
                UpstreamFinalityRef: "pwd-discount-upstream-finality-ref",
                CurrencyCode: "PHP",
                PayableAmountMinorUnits: 8929,
                DiscountReferences:
                [
                    new CentralPmsFiscalDiscountReferenceContext(
                        DiscountValidationRef: validationId.ToString("D"),
                        Status: "approved",
                        AppliesStatutoryDiscountTreatment: true,
                        ReferenceContext: new Dictionary<string, string>
                        {
                            ["payableBasisApplicationId"] = applicationId.ToString("D"),
                            ["entitlementType"] = "PWD"
                        })
                ],
                ReferenceContext: new Dictionary<string, string>
                {
                    ["appliedTariffSnapshotId"] = appliedTariffSnapshotId.ToString("D"),
                    ["payableBasisApplicationId"] = applicationId.ToString("D"),
                    ["entitlementType"] = "PWD"
                }),
            DocumentLines =
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: null,
                    Description: "Parking fee - statutory discount applied",
                    Quantity: 1m,
                    UnitAmountMinorUnits: 11161,
                    GrossAmountMinorUnits: 11161,
                    DiscountAmountMinorUnits: 2232,
                    TaxAmountMinorUnits: 0,
                    NetAmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    LineStatusCodeId: null,
                    SourceRef: appliedTariffSnapshotId.ToString("D"),
                    LineContext: new Dictionary<string, string>
                    {
                        ["entitlementType"] = "PWD",
                        ["originalGrossAmountMinorUnits"] = "12500",
                        ["vatAmountMinorUnits"] = "1339",
                        ["vatPrivilegeAmountMinorUnits"] = "1339"
                    })
            ],
            Tenders =
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: null,
                    AmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    CentralPmsPaymentAttemptRef: "payment-attempt-ref",
                    CentralPmsPaymentConfirmationRef: "payment-confirmation-ref",
                    PaymentFinalityRef: "payment-finality-ref",
                    ProviderRef: "provider-ref",
                    TenderContext: new Dictionary<string, string> { ["channel"] = "webpay" })
            ],
            TaxDetails =
            [
                new CentralPmsFiscalTaxDetailContext(
                    TaxTypeCodeId: null,
                    TaxClassificationCodeId: null,
                    TaxableAmountMinorUnits: 11161,
                    TaxAmountMinorUnits: 1339,
                    CurrencyCode: "PHP",
                    LineSequence: 1,
                    TaxRate: 12m,
                    TaxContext: new Dictionary<string, string> { ["basis"] = "VAT_EXCLUSIVE" })
            ],
            DiscountPrivilegeDetails =
            [
                new CentralPmsFiscalDiscountPrivilegeDetailContext(
                    DiscountPrivilegeTypeCodeId: null,
                    BasisAmountMinorUnits: 11161,
                    DiscountAmountMinorUnits: 2232,
                    VatPrivilegeAmountMinorUnits: 1339,
                    CurrencyCode: "PHP",
                    LineSequence: 1,
                    BeneficiaryRef: null,
                    EvidenceRef: null,
                    ApprovalRef: validationId.ToString("D"),
                    DiscountPrivilegeContext: new Dictionary<string, string>
                    {
                        ["entitlementType"] = "PWD",
                        ["payableBasisApplicationId"] = applicationId.ToString("D"),
                        ["roundingMode"] = "HALF_AWAY_FROM_ZERO"
                    })
            ],
            Totals =
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: null,
                    AmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    TotalContext: new Dictionary<string, string> { ["kind"] = "final_payable" })
            ]
        };

        var result = _sut.Map(context);

        result.PayableBasis.PayableAmountMinorUnits.Should().Be(8929);
        result.Tenders.Should().ContainSingle().Which.AmountMinorUnits.Should().Be(8929);
        result.DocumentLines.Should().ContainSingle().Which.Should().Match<PosServerFiscalDocumentLineRequest>(line =>
            line.GrossAmountMinorUnits == 11161 &&
            line.DiscountAmountMinorUnits == 2232 &&
            line.TaxAmountMinorUnits == 0 &&
            line.NetAmountMinorUnits == 8929);
        result.PayableBasis.DiscountReferences.Should().ContainSingle().Which.ReferenceContext
            .Should().Contain("entitlementType", "PWD");
        result.DiscountPrivilegeDetails.Should().ContainSingle().Which.Should()
            .Match<PosServerFiscalDiscountPrivilegeDetailRequest>(discount =>
                discount.BasisAmountMinorUnits == 11161 &&
                discount.DiscountAmountMinorUnits == 2232 &&
                discount.VatPrivilegeAmountMinorUnits == 1339 &&
                discount.ApprovalRef == validationId.ToString("D"));
    }

    [Fact]
    public void Map_WhenAppliedStatutoryFactsArePresent_MapsFirstClassPosServerPayload()
    {
        var context = StatutoryContext("WEBPAY");

        var result = _sut.Map(context);

        result.AppliedStatutoryFiscalFacts.Should().NotBeNull();
        var facts = result.AppliedStatutoryFiscalFacts!;
        facts.StatutoryDiscountDecisionCommandId.Should().Be(StatutoryDecisionCommandId);
        facts.StatutoryRequestReference.Should().Be(StatutoryValidationId);
        facts.StatutoryPayableBasisApplicationCommandId.Should().Be(StatutoryApplicationCommandId);
        facts.StatutoryValidationId.Should().Be(StatutoryValidationId);
        facts.ParkingSessionId.Should().Be(StatutoryParkingSessionId);
        facts.SiteId.Should().Be(StatutorySiteId);
        facts.SiteGroupId.Should().Be(StatutorySiteGroupId);
        facts.EntitlementType.Should().Be("SENIOR_CITIZEN");
        facts.BenefitClassification.Should().Be("VAT_EXEMPTION_AND_STATUTORY_DISCOUNT");
        facts.PolicyReference.ResolutionBasis.Should().Be("LOCAL_ORDINANCE");
        facts.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
        facts.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        facts.OriginalAmountMinorUnits.Should().Be(12_500);
        facts.VatExclusiveBasisAmountMinorUnits.Should().Be(11_161);
        facts.VatAmountMinorUnits.Should().Be(1_339);
        facts.VatTreatment.Should().Be("VAT_EXEMPT");
        facts.StatutoryDiscountAmountMinorUnits.Should().Be(2_232);
        facts.FinalPayableAmountMinorUnits.Should().Be(8_929);
        facts.Currency.Should().Be("PHP");
        facts.SourcePaymentChannel.Should().Be("WEBPAY");
        facts.TerminalCashTenderId.Should().BeNull();
    }

    [Fact]
    public void Map_WhenWebPayAndAptHaveEquivalentAppliedBenefit_ProducesParityExceptChannelContext()
    {
        var webPay = _sut.Map(StatutoryContext("WEBPAY")).AppliedStatutoryFiscalFacts!;
        var aptTenderId = Guid.Parse("12121212-1212-4121-8121-121212121212");
        var apt = _sut.Map(StatutoryContext("ASSISTED_PAYMENT_TERMINAL", aptTenderId)).AppliedStatutoryFiscalFacts!;

        apt.Should().BeEquivalentTo(
            webPay with
            {
                SourcePaymentChannel = "ASSISTED_PAYMENT_TERMINAL",
                TerminalCashTenderId = aptTenderId
            });
    }

    [Fact]
    public void Map_WhenAppliedStatutoryFactsDoNotMatchFinalPayableBasis_RejectsBeforePosServerCall()
    {
        var context = StatutoryContext("WEBPAY") with
        {
            AppliedStatutoryFiscalFacts = StatutoryFacts("WEBPAY") with
            {
                FinalPayableAmountMinorUnits = 8_930
            }
        };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("applied_statutory_fiscal_facts_must_match_payable_basis");
    }

    [Fact]
    public void Map_WhenAppliedStatutoryFactsUseOriginalSnapshotAsAppliedSnapshot_RejectsBeforePosServerCall()
    {
        var context = StatutoryContext("WEBPAY") with
        {
            AppliedStatutoryFiscalFacts = StatutoryFacts("WEBPAY") with
            {
                AppliedTariffSnapshotId = OriginalTariffSnapshotId
            }
        };

        var ex = Assert.Throws<ArgumentException>(() => _sut.Map(context));

        ex.Message.Should().Contain("applied_statutory_fiscal_facts_final_snapshot_required");
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
                    BeneficiaryRef: null,
                    EvidenceRef: null,
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
            VendorAckRef: null,
            SiteId: Guid.Parse("12121212-1212-4212-8212-121212121212"));

    private static readonly Guid StatutoryDecisionCommandId = Guid.Parse("01010101-0101-4101-8101-010101010101");
    private static readonly Guid StatutoryApplicationCommandId = Guid.Parse("02020202-0202-4202-8202-020202020202");
    private static readonly Guid StatutoryValidationId = Guid.Parse("03030303-0303-4303-8303-030303030303");
    private static readonly Guid StatutoryParkingSessionId = Guid.Parse("04040404-0404-4404-8404-040404040404");
    private static readonly Guid StatutorySiteId = Guid.Parse("05050505-0505-4505-8505-050505050505");
    private static readonly Guid StatutorySiteGroupId = Guid.Parse("06060606-0606-4606-8606-060606060606");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("07070707-0707-4707-8707-070707070707");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("08080808-0808-4808-8808-080808080808");
    private static readonly Guid AppliedPolicyReferenceId = Guid.Parse("09090909-0909-4909-8909-090909090909");

    internal static CentralPmsFiscalDocumentMappingContext StatutoryContext(
        string sourcePaymentChannel,
        Guid? terminalCashTenderId = null) =>
        ValidContext() with
        {
            CentralPmsParkingSessionRef = StatutoryParkingSessionId.ToString("D"),
            PayableBasis = ValidContext().PayableBasis with
            {
                PayableBasisRef = AppliedTariffSnapshotId.ToString("D"),
                PayableAmountMinorUnits = 8_929,
                CurrencyCode = "PHP"
            },
            Tenders =
            [
                ValidContext().Tenders[0] with
                {
                    AmountMinorUnits = 8_929,
                    CurrencyCode = "PHP"
                }
            ],
            Totals =
            [
                ValidContext().Totals[0] with
                {
                    AmountMinorUnits = 8_929,
                    CurrencyCode = "PHP"
                }
            ],
            AppliedStatutoryFiscalFacts = StatutoryFacts(sourcePaymentChannel, terminalCashTenderId)
        };

    internal static CentralPmsAppliedStatutoryFiscalFactsContext StatutoryFacts(
        string sourcePaymentChannel,
        Guid? terminalCashTenderId = null) =>
        new(
            StatutoryDecisionCommandId,
            StatutoryValidationId,
            StatutoryApplicationCommandId,
            StatutoryValidationId,
            StatutoryParkingSessionId,
            StatutorySiteId,
            StatutorySiteGroupId,
            "senior_citizen",
            "vat_exemption_and_statutory_discount",
            new CentralPmsAppliedStatutoryPolicyReferenceContext(
                "local_ordinance",
                AppliedPolicyReferenceId,
                PolicyCode: "PARANAQUE_SENIOR_PARKING",
                OrdinanceReference: "SOURCE_TEXT_UNAVAILABLE_OPERATIONAL"),
            OriginalTariffSnapshotId,
            AppliedTariffSnapshotId,
            OriginalAmountMinorUnits: 12_500,
            VatExclusiveBasisAmountMinorUnits: 11_161,
            VatAmountMinorUnits: 1_339,
            VatTreatment: "vat_exempt",
            StatutoryDiscountAmountMinorUnits: 2_232,
            FinalPayableAmountMinorUnits: 8_929,
            Currency: "php",
            AppliedAt: DateTimeOffset.Parse("2026-07-28T01:21:00Z"),
            SourcePaymentChannel: sourcePaymentChannel,
            TerminalCashTenderId: terminalCashTenderId);
}
