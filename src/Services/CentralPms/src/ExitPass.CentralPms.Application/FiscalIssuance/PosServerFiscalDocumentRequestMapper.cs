namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IPosServerFiscalDocumentRequestMapper
{
    PosServerFiscalDocumentCreateRequest Map(CentralPmsFiscalDocumentMappingContext context);
}

public sealed class PosServerFiscalDocumentRequestMapper : IPosServerFiscalDocumentRequestMapper
{
    private static readonly string[] SensitiveTerms =
    [
        "pan",
        "cvv",
        "secret",
        "token",
        "credential",
        "raw_payload",
        "callback_payload",
        "provider_callback",
        "entitlement_evidence_image"
    ];

    public PosServerFiscalDocumentCreateRequest Map(CentralPmsFiscalDocumentMappingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var errors = Validate(context);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"POS Server fiscal document request context is invalid: {string.Join(", ", errors)}",
                nameof(context));
        }

        var lines = context.DocumentLines
            .Select(line => new PosServerFiscalDocumentLineRequest(
                LineSequence: line.LineSequence,
                LineTypeCodeId: line.LineTypeCodeId,
                Description: line.Description.Trim(),
                Quantity: line.Quantity,
                UnitAmountMinorUnits: line.UnitAmountMinorUnits,
                GrossAmountMinorUnits: line.GrossAmountMinorUnits,
                DiscountAmountMinorUnits: line.DiscountAmountMinorUnits,
                TaxAmountMinorUnits: line.TaxAmountMinorUnits,
                NetAmountMinorUnits: line.NetAmountMinorUnits,
                CurrencyCode: line.CurrencyCode.Trim().ToUpperInvariant(),
                LineStatusCodeId: line.LineStatusCodeId,
                SourceRef: TrimToNull(line.SourceRef),
                LineContext: NormalizeDictionary(line.LineContext)))
            .ToArray();

        return new PosServerFiscalDocumentCreateRequest(
            SitePosServerRef: TrimToNull(context.SitePosServerRef),
            FiscalDocumentTypeCodeKey: TrimToNull(context.FiscalDocumentTypeCodeKey),
            PayableBasis: new PosServerPayableBasisRequest(
                PayableBasisRef: context.PayableBasis.PayableBasisRef.Trim(),
                UpstreamFinalityRef: context.PayableBasis.UpstreamFinalityRef.Trim(),
                CurrencyCode: context.PayableBasis.CurrencyCode.Trim().ToUpperInvariant(),
                PayableAmountMinorUnits: context.PayableBasis.PayableAmountMinorUnits,
                DiscountReferences: context.PayableBasis.DiscountReferences
                    .Select(discount => new PosServerFiscalDiscountReferenceRequest(
                        DiscountValidationRef: discount.DiscountValidationRef.Trim(),
                        Status: discount.Status.Trim(),
                        AppliesStatutoryDiscountTreatment: discount.AppliesStatutoryDiscountTreatment,
                        ReferenceContext: NormalizeDictionary(discount.ReferenceContext))
                    {
                        StatutoryDiscountDecisionCommandRef = TrimToNull(discount.StatutoryDiscountDecisionCommandRef),
                        EntitlementType = TrimToNull(discount.EntitlementType)?.ToUpperInvariant(),
                        AppliedPolicyReferenceRef = TrimToNull(discount.AppliedPolicyReferenceRef),
                        OriginalTariffSnapshotRef = TrimToNull(discount.OriginalTariffSnapshotRef),
                        AppliedTariffSnapshotRef = TrimToNull(discount.AppliedTariffSnapshotRef),
                        OriginalAmountMinorUnits = discount.OriginalAmountMinorUnits,
                        VatExclusiveBasisAmountMinorUnits = discount.VatExclusiveBasisAmountMinorUnits,
                        VatTreatment = TrimToNull(discount.VatTreatment)?.ToUpperInvariant(),
                        DiscountAmountMinorUnits = discount.DiscountAmountMinorUnits,
                        FinalPayableAmountMinorUnits = discount.FinalPayableAmountMinorUnits,
                        DecisionTimestamp = discount.DecisionTimestamp,
                        SourceChannel = TrimToNull(discount.SourceChannel)?.ToUpperInvariant()
                    })
                    .ToArray(),
                ReferenceContext: NormalizeDictionary(context.PayableBasis.ReferenceContext)),
            SitePosServerId: context.SitePosServerId,
            ChannelTerminalId: null,
            FiscalDocumentTypeCodeId: context.FiscalDocumentTypeCodeId,
            FiscalDocumentStatusCodeId: context.FiscalDocumentStatusCodeId,
            BusinessDayDate: context.BusinessDayDate,
            CentralPmsParkingSessionRef: context.CentralPmsParkingSessionRef.Trim(),
            CentralPmsPaymentAttemptRef: context.CentralPmsPaymentAttemptRef.Trim(),
            CentralPmsPaymentConfirmationRef: context.CentralPmsPaymentConfirmationRef.Trim(),
            UpstreamFinalityRef: context.PayableBasis.UpstreamFinalityRef.Trim(),
            PaymentFinalityRef: TrimToNull(context.PaymentFinalityRef),
            VendorAckRef: TrimToNull(context.VendorAckRef),
            DocumentLines: lines,
            Lines: lines,
            Tenders: context.Tenders
                .Select(tender => new PosServerFiscalTenderRequest(
                    TenderTypeCodeId: tender.TenderTypeCodeId,
                    AmountMinorUnits: tender.AmountMinorUnits,
                    CurrencyCode: tender.CurrencyCode.Trim().ToUpperInvariant(),
                    CentralPmsPaymentAttemptRef: TrimToNull(tender.CentralPmsPaymentAttemptRef),
                    CentralPmsPaymentConfirmationRef: TrimToNull(tender.CentralPmsPaymentConfirmationRef),
                    PaymentFinalityRef: TrimToNull(tender.PaymentFinalityRef),
                    ProviderRef: TrimToNull(tender.ProviderRef),
                    TenderContext: NormalizeDictionary(tender.TenderContext)))
                .ToArray(),
            TaxDetails: context.TaxDetails
                .Select(tax => new PosServerFiscalTaxDetailRequest(
                    TaxTypeCodeId: tax.TaxTypeCodeId,
                    TaxClassificationCodeId: tax.TaxClassificationCodeId,
                    TaxableAmountMinorUnits: tax.TaxableAmountMinorUnits,
                    TaxAmountMinorUnits: tax.TaxAmountMinorUnits,
                    CurrencyCode: tax.CurrencyCode.Trim().ToUpperInvariant(),
                    LineSequence: tax.LineSequence,
                    TaxRate: tax.TaxRate,
                    TaxContext: NormalizeDictionary(tax.TaxContext)))
                .ToArray(),
            DiscountPrivilegeDetails: context.DiscountPrivilegeDetails
                .Select(discount => new PosServerFiscalDiscountPrivilegeDetailRequest(
                    DiscountPrivilegeTypeCodeId: discount.DiscountPrivilegeTypeCodeId,
                    BasisAmountMinorUnits: discount.BasisAmountMinorUnits,
                    DiscountAmountMinorUnits: discount.DiscountAmountMinorUnits,
                    VatPrivilegeAmountMinorUnits: discount.VatPrivilegeAmountMinorUnits,
                    CurrencyCode: discount.CurrencyCode.Trim().ToUpperInvariant(),
                    LineSequence: discount.LineSequence,
                    BeneficiaryRef: TrimToNull(discount.BeneficiaryRef),
                    EvidenceRef: TrimToNull(discount.EvidenceRef),
                    ApprovalRef: TrimToNull(discount.ApprovalRef),
                    DiscountPrivilegeContext: NormalizeDictionary(discount.DiscountPrivilegeContext)))
                .ToArray(),
            Totals: context.Totals
                .Select(total => new PosServerFiscalTotalRequest(
                    TotalTypeCodeId: total.TotalTypeCodeId,
                    AmountMinorUnits: total.AmountMinorUnits,
                    CurrencyCode: total.CurrencyCode.Trim().ToUpperInvariant(),
                    TotalContext: NormalizeDictionary(total.TotalContext)))
                .ToArray(),
            ReferenceContext: NormalizeDictionary(context.ReferenceContext));
    }

    private static IReadOnlyList<string> Validate(CentralPmsFiscalDocumentMappingContext context)
    {
        var errors = new List<string>();

        if (context.SitePosServerId is null && string.IsNullOrWhiteSpace(context.SitePosServerRef))
        {
            errors.Add("site_pos_server_context_required");
        }

        if (context.FiscalDocumentTypeCodeId is null && string.IsNullOrWhiteSpace(context.FiscalDocumentTypeCodeKey))
        {
            errors.Add("fiscal_document_type_required");
        }

        if (string.IsNullOrWhiteSpace(context.CentralPmsParkingSessionRef))
        {
            errors.Add("central_pms_parking_session_ref_required");
        }

        if (string.IsNullOrWhiteSpace(context.CentralPmsPaymentAttemptRef))
        {
            errors.Add("central_pms_payment_attempt_ref_required");
        }

        if (string.IsNullOrWhiteSpace(context.CentralPmsPaymentConfirmationRef))
        {
            errors.Add("central_pms_payment_confirmation_ref_required");
        }

        if (context.PayableBasis is null)
        {
            errors.Add("payable_basis_required");
        }
        else
        {
            ValidatePayableBasis(context.PayableBasis, errors);
        }

        if (context.DocumentLines.Count == 0)
        {
            errors.Add("document_line_required");
        }

        if (context.Tenders.Count == 0)
        {
            errors.Add("tender_required");
        }

        ValidateNoSensitivePayloadTerms(context, errors);
        return errors;
    }

    private static void ValidatePayableBasis(CentralPmsPayableBasisContext payableBasis, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(payableBasis.PayableBasisRef))
        {
            errors.Add("payable_basis_ref_required");
        }

        if (string.IsNullOrWhiteSpace(payableBasis.UpstreamFinalityRef))
        {
            errors.Add("upstream_finality_reference_required");
        }

        if (string.IsNullOrWhiteSpace(payableBasis.CurrencyCode))
        {
            errors.Add("currency_code_required");
        }

        if (payableBasis.PayableAmountMinorUnits <= 0)
        {
            errors.Add("payable_amount_minor_units_required");
        }
    }

    private static void ValidateNoSensitivePayloadTerms(
        CentralPmsFiscalDocumentMappingContext context,
        List<string> errors)
    {
        var values = new List<string?>
        {
            context.PaymentFinalityRef,
            context.VendorAckRef,
            context.CentralPmsParkingSessionRef,
            context.CentralPmsPaymentAttemptRef,
            context.CentralPmsPaymentConfirmationRef,
            context.PayableBasis?.PayableBasisRef,
            context.PayableBasis?.UpstreamFinalityRef
        };

        values.AddRange(context.ReferenceContext.SelectMany(pair => new[] { pair.Key, pair.Value }));
        if (context.PayableBasis is not null)
        {
            values.AddRange(context.PayableBasis.ReferenceContext.SelectMany(pair => new[] { pair.Key, pair.Value }));
            values.AddRange(context.PayableBasis.DiscountReferences.SelectMany(discount =>
                new[]
                {
                    discount.DiscountValidationRef,
                    discount.Status,
                    discount.StatutoryDiscountDecisionCommandRef,
                    discount.EntitlementType,
                    discount.AppliedPolicyReferenceRef,
                    discount.OriginalTariffSnapshotRef,
                    discount.AppliedTariffSnapshotRef,
                    discount.VatTreatment,
                    discount.SourceChannel
                }
                    .Concat(discount.ReferenceContext.SelectMany(pair => new[] { pair.Key, pair.Value }))));
        }
        values.AddRange(context.DocumentLines.SelectMany(line =>
            new[] { line.Description, line.SourceRef, line.CurrencyCode }
                .Concat(line.LineContext.SelectMany(pair => new[] { pair.Key, pair.Value }))));
        values.AddRange(context.Tenders.SelectMany(tender =>
            new[] { tender.CentralPmsPaymentAttemptRef, tender.CentralPmsPaymentConfirmationRef, tender.PaymentFinalityRef, tender.ProviderRef, tender.CurrencyCode }
                .Concat(tender.TenderContext.SelectMany(pair => new[] { pair.Key, pair.Value }))));
        values.AddRange(context.DiscountPrivilegeDetails.SelectMany(discount =>
            new[] { discount.BeneficiaryRef, discount.EvidenceRef, discount.ApprovalRef, discount.CurrencyCode }
                .Concat(discount.DiscountPrivilegeContext.SelectMany(pair => new[] { pair.Key, pair.Value }))));

        if (values.Where(value => !string.IsNullOrWhiteSpace(value)).Any(ContainsSensitiveTerm))
        {
            errors.Add("sensitive_payload_reference_rejected");
        }
    }

    private static bool ContainsSensitiveTerm(string? value) =>
        value is not null &&
        SensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IReadOnlyDictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string> value) =>
        value
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal);
}
