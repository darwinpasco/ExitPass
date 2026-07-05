using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalSemanticRequestHashCalculator : IFiscalSemanticRequestHashCalculator
{
    public const string CurrentHashAlgorithm = "SHA-256";
    public const string CurrentHashSourceVersion = "central-pms-pos-server-fiscal-request-v1";

    public FiscalSemanticRequestHashResult Calculate(PosServerFiscalDocumentCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missingFacts = MissingRequiredFacts(request);
        if (missingFacts.Count > 0)
        {
            return new FiscalSemanticRequestHashResult(
                Status: FiscalSemanticRequestHashSourceStatus.Incomplete,
                HashValue: null,
                HashAlgorithm: CurrentHashAlgorithm,
                HashSourceVersion: CurrentHashSourceVersion,
                SourceFactCount: 0,
                SafeSourceSummary: $"semantic_request_hash_source_incomplete:{string.Join(",", missingFacts)}",
                BlockReasonCode: missingFacts[0]);
        }

        var facts = CanonicalFacts(request);
        var canonicalSource = string.Join('\n', facts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSource));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();

        return new FiscalSemanticRequestHashResult(
            Status: FiscalSemanticRequestHashSourceStatus.Available,
            HashValue: hash,
            HashAlgorithm: CurrentHashAlgorithm,
            HashSourceVersion: CurrentHashSourceVersion,
            SourceFactCount: facts.Count,
            SafeSourceSummary: $"semantic_request_hash_source_available:facts={facts.Count}",
            BlockReasonCode: null);
    }

    private static IReadOnlyList<string> MissingRequiredFacts(PosServerFiscalDocumentCreateRequest request)
    {
        var missing = new List<string>();

        if (request.PayableBasis is null)
        {
            missing.Add("payable_basis_required");
        }
        else
        {
            AddIfMissing(missing, request.PayableBasis.PayableBasisRef, "payable_basis_ref_required");
            AddIfMissing(missing, request.PayableBasis.UpstreamFinalityRef, "upstream_finality_reference_required");
            AddIfMissing(missing, request.PayableBasis.CurrencyCode, "payable_basis_currency_required");
            if (request.PayableBasis.PayableAmountMinorUnits <= 0)
            {
                missing.Add("payable_amount_minor_units_required");
            }
        }

        if (request.SitePosServerId is null && string.IsNullOrWhiteSpace(request.SitePosServerRef))
        {
            missing.Add("site_pos_server_context_required");
        }

        if (request.FiscalDocumentTypeCodeId is null && string.IsNullOrWhiteSpace(request.FiscalDocumentTypeCodeKey))
        {
            missing.Add("fiscal_document_type_required");
        }

        AddIfMissing(missing, request.CentralPmsParkingSessionRef, "parking_session_ref_required");
        AddIfMissing(missing, request.CentralPmsPaymentAttemptRef, "payment_attempt_ref_required");
        AddIfMissing(missing, request.CentralPmsPaymentConfirmationRef, "payment_confirmation_ref_required");
        AddIfMissing(missing, request.UpstreamFinalityRef, "upstream_finality_reference_required");

        if (request.DocumentLines.Count == 0)
        {
            missing.Add("document_line_required");
        }

        if (request.Tenders.Count == 0)
        {
            missing.Add("tender_required");
        }

        return missing;
    }

    private static List<string> CanonicalFacts(PosServerFiscalDocumentCreateRequest request)
    {
        var facts = new List<string>
        {
            Fact("hash_source_version", CurrentHashSourceVersion),
            Fact("site_pos_server_id", request.SitePosServerId),
            Fact("site_pos_server_ref", request.SitePosServerRef),
            Fact("fiscal_document_type_code_id", request.FiscalDocumentTypeCodeId),
            Fact("fiscal_document_type_code_key", request.FiscalDocumentTypeCodeKey),
            Fact("fiscal_document_status_code_id", request.FiscalDocumentStatusCodeId),
            Fact("business_day_date", request.BusinessDayDate),
            Fact("central_pms_parking_session_ref", request.CentralPmsParkingSessionRef),
            Fact("central_pms_payment_attempt_ref", request.CentralPmsPaymentAttemptRef),
            Fact("central_pms_payment_confirmation_ref", request.CentralPmsPaymentConfirmationRef),
            Fact("upstream_finality_ref", request.UpstreamFinalityRef),
            Fact("payment_finality_ref", request.PaymentFinalityRef),
            Fact("vendor_ack_ref", request.VendorAckRef)
        };

        AddPayableBasisFacts(facts, request.PayableBasis);
        AddDictionaryFacts(facts, "reference_context", request.ReferenceContext);
        AddLineFacts(facts, request.DocumentLines);
        AddTenderFacts(facts, request.Tenders);
        AddTaxFacts(facts, request.TaxDetails);
        AddDiscountPrivilegeFacts(facts, request.DiscountPrivilegeDetails);
        AddTotalFacts(facts, request.Totals);

        return facts;
    }

    private static void AddPayableBasisFacts(List<string> facts, PosServerPayableBasisRequest payableBasis)
    {
        facts.Add(Fact("payable_basis.ref", payableBasis.PayableBasisRef));
        facts.Add(Fact("payable_basis.upstream_finality_ref", payableBasis.UpstreamFinalityRef));
        facts.Add(Fact("payable_basis.currency_code", payableBasis.CurrencyCode));
        facts.Add(Fact("payable_basis.amount_minor_units", payableBasis.PayableAmountMinorUnits));
        AddDictionaryFacts(facts, "payable_basis.reference_context", payableBasis.ReferenceContext);

        var discounts = payableBasis.DiscountReferences
            .OrderBy(discount => Normalize(discount.DiscountValidationRef), StringComparer.Ordinal)
            .ThenBy(discount => Normalize(discount.Status), StringComparer.Ordinal)
            .ThenBy(discount => discount.AppliesStatutoryDiscountTreatment)
            .ThenBy(discount => DictionaryFingerprint(discount.ReferenceContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("payable_basis.discount_references.count", discounts.Length));
        for (var index = 0; index < discounts.Length; index++)
        {
            var discount = discounts[index];
            var prefix = $"payable_basis.discount_references[{index}]";
            facts.Add(Fact($"{prefix}.discount_validation_ref", discount.DiscountValidationRef));
            facts.Add(Fact($"{prefix}.status", discount.Status));
            facts.Add(Fact($"{prefix}.applies_statutory_discount_treatment", discount.AppliesStatutoryDiscountTreatment));
            AddDictionaryFacts(facts, $"{prefix}.reference_context", discount.ReferenceContext);
        }
    }

    private static void AddLineFacts(List<string> facts, IReadOnlyList<PosServerFiscalDocumentLineRequest> lines)
    {
        var ordered = lines
            .OrderBy(line => line.LineSequence)
            .ThenBy(line => Normalize(line.SourceRef), StringComparer.Ordinal)
            .ThenBy(line => Normalize(line.Description), StringComparer.Ordinal)
            .ThenBy(line => line.LineTypeCodeId)
            .ThenBy(line => line.LineStatusCodeId)
            .ThenBy(line => DictionaryFingerprint(line.LineContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("document_lines.count", ordered.Length));
        for (var index = 0; index < ordered.Length; index++)
        {
            var line = ordered[index];
            var prefix = $"document_lines[{index}]";
            facts.Add(Fact($"{prefix}.line_sequence", line.LineSequence));
            facts.Add(Fact($"{prefix}.line_type_code_id", line.LineTypeCodeId));
            facts.Add(Fact($"{prefix}.description", line.Description));
            facts.Add(Fact($"{prefix}.quantity", line.Quantity));
            facts.Add(Fact($"{prefix}.unit_amount_minor_units", line.UnitAmountMinorUnits));
            facts.Add(Fact($"{prefix}.gross_amount_minor_units", line.GrossAmountMinorUnits));
            facts.Add(Fact($"{prefix}.discount_amount_minor_units", line.DiscountAmountMinorUnits));
            facts.Add(Fact($"{prefix}.tax_amount_minor_units", line.TaxAmountMinorUnits));
            facts.Add(Fact($"{prefix}.net_amount_minor_units", line.NetAmountMinorUnits));
            facts.Add(Fact($"{prefix}.currency_code", line.CurrencyCode));
            facts.Add(Fact($"{prefix}.line_status_code_id", line.LineStatusCodeId));
            facts.Add(Fact($"{prefix}.source_ref", line.SourceRef));
            AddDictionaryFacts(facts, $"{prefix}.line_context", line.LineContext);
        }
    }

    private static void AddTenderFacts(List<string> facts, IReadOnlyList<PosServerFiscalTenderRequest> tenders)
    {
        var ordered = tenders
            .OrderBy(tender => tender.TenderTypeCodeId)
            .ThenBy(tender => tender.AmountMinorUnits)
            .ThenBy(tender => Normalize(tender.CurrencyCode), StringComparer.Ordinal)
            .ThenBy(tender => Normalize(tender.ProviderRef), StringComparer.Ordinal)
            .ThenBy(tender => DictionaryFingerprint(tender.TenderContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("tenders.count", ordered.Length));
        for (var index = 0; index < ordered.Length; index++)
        {
            var tender = ordered[index];
            var prefix = $"tenders[{index}]";
            facts.Add(Fact($"{prefix}.tender_type_code_id", tender.TenderTypeCodeId));
            facts.Add(Fact($"{prefix}.amount_minor_units", tender.AmountMinorUnits));
            facts.Add(Fact($"{prefix}.currency_code", tender.CurrencyCode));
            facts.Add(Fact($"{prefix}.central_pms_payment_attempt_ref", tender.CentralPmsPaymentAttemptRef));
            facts.Add(Fact($"{prefix}.central_pms_payment_confirmation_ref", tender.CentralPmsPaymentConfirmationRef));
            facts.Add(Fact($"{prefix}.payment_finality_ref", tender.PaymentFinalityRef));
            facts.Add(Fact($"{prefix}.provider_ref", tender.ProviderRef));
            AddDictionaryFacts(facts, $"{prefix}.tender_context", tender.TenderContext);
        }
    }

    private static void AddTaxFacts(List<string> facts, IReadOnlyList<PosServerFiscalTaxDetailRequest> taxes)
    {
        var ordered = taxes
            .OrderBy(tax => tax.LineSequence)
            .ThenBy(tax => tax.TaxTypeCodeId)
            .ThenBy(tax => tax.TaxClassificationCodeId)
            .ThenBy(tax => tax.TaxableAmountMinorUnits)
            .ThenBy(tax => tax.TaxAmountMinorUnits)
            .ThenBy(tax => DictionaryFingerprint(tax.TaxContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("tax_details.count", ordered.Length));
        for (var index = 0; index < ordered.Length; index++)
        {
            var tax = ordered[index];
            var prefix = $"tax_details[{index}]";
            facts.Add(Fact($"{prefix}.tax_type_code_id", tax.TaxTypeCodeId));
            facts.Add(Fact($"{prefix}.tax_classification_code_id", tax.TaxClassificationCodeId));
            facts.Add(Fact($"{prefix}.taxable_amount_minor_units", tax.TaxableAmountMinorUnits));
            facts.Add(Fact($"{prefix}.tax_amount_minor_units", tax.TaxAmountMinorUnits));
            facts.Add(Fact($"{prefix}.currency_code", tax.CurrencyCode));
            facts.Add(Fact($"{prefix}.line_sequence", tax.LineSequence));
            facts.Add(Fact($"{prefix}.tax_rate", tax.TaxRate));
            AddDictionaryFacts(facts, $"{prefix}.tax_context", tax.TaxContext);
        }
    }

    private static void AddDiscountPrivilegeFacts(
        List<string> facts,
        IReadOnlyList<PosServerFiscalDiscountPrivilegeDetailRequest> discounts)
    {
        var ordered = discounts
            .OrderBy(discount => discount.LineSequence)
            .ThenBy(discount => discount.DiscountPrivilegeTypeCodeId)
            .ThenBy(discount => Normalize(discount.ApprovalRef), StringComparer.Ordinal)
            .ThenBy(discount => Normalize(discount.EvidenceRef), StringComparer.Ordinal)
            .ThenBy(discount => DictionaryFingerprint(discount.DiscountPrivilegeContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("discount_privilege_details.count", ordered.Length));
        for (var index = 0; index < ordered.Length; index++)
        {
            var discount = ordered[index];
            var prefix = $"discount_privilege_details[{index}]";
            facts.Add(Fact($"{prefix}.discount_privilege_type_code_id", discount.DiscountPrivilegeTypeCodeId));
            facts.Add(Fact($"{prefix}.basis_amount_minor_units", discount.BasisAmountMinorUnits));
            facts.Add(Fact($"{prefix}.discount_amount_minor_units", discount.DiscountAmountMinorUnits));
            facts.Add(Fact($"{prefix}.vat_privilege_amount_minor_units", discount.VatPrivilegeAmountMinorUnits));
            facts.Add(Fact($"{prefix}.currency_code", discount.CurrencyCode));
            facts.Add(Fact($"{prefix}.line_sequence", discount.LineSequence));
            facts.Add(Fact($"{prefix}.beneficiary_ref", discount.BeneficiaryRef));
            facts.Add(Fact($"{prefix}.evidence_ref", discount.EvidenceRef));
            facts.Add(Fact($"{prefix}.approval_ref", discount.ApprovalRef));
            AddDictionaryFacts(facts, $"{prefix}.discount_privilege_context", discount.DiscountPrivilegeContext);
        }
    }

    private static void AddTotalFacts(List<string> facts, IReadOnlyList<PosServerFiscalTotalRequest> totals)
    {
        var ordered = totals
            .OrderBy(total => total.TotalTypeCodeId)
            .ThenBy(total => total.AmountMinorUnits)
            .ThenBy(total => Normalize(total.CurrencyCode), StringComparer.Ordinal)
            .ThenBy(total => DictionaryFingerprint(total.TotalContext), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact("totals.count", ordered.Length));
        for (var index = 0; index < ordered.Length; index++)
        {
            var total = ordered[index];
            var prefix = $"totals[{index}]";
            facts.Add(Fact($"{prefix}.total_type_code_id", total.TotalTypeCodeId));
            facts.Add(Fact($"{prefix}.amount_minor_units", total.AmountMinorUnits));
            facts.Add(Fact($"{prefix}.currency_code", total.CurrencyCode));
            AddDictionaryFacts(facts, $"{prefix}.total_context", total.TotalContext);
        }
    }

    private static void AddDictionaryFacts(
        List<string> facts,
        string prefix,
        IReadOnlyDictionary<string, string> dictionary)
    {
        var pairs = dictionary
            .OrderBy(pair => Normalize(pair.Key), StringComparer.Ordinal)
            .ThenBy(pair => Normalize(pair.Value), StringComparer.Ordinal)
            .ToArray();

        facts.Add(Fact($"{prefix}.count", pairs.Length));
        foreach (var pair in pairs)
        {
            facts.Add(Fact($"{prefix}.{Normalize(pair.Key)}", pair.Value));
        }
    }

    private static string DictionaryFingerprint(IReadOnlyDictionary<string, string> dictionary) =>
        string.Join(
            "|",
            dictionary
                .OrderBy(pair => Normalize(pair.Key), StringComparer.Ordinal)
                .ThenBy(pair => Normalize(pair.Value), StringComparer.Ordinal)
                .Select(pair => $"{Normalize(pair.Key)}={Normalize(pair.Value)}"));

    private static void AddIfMissing(List<string> missing, string? value, string reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(reason);
        }
    }

    private static string Fact(string key, object? value) =>
        $"{key}={CanonicalValue(value)}";

    private static string CanonicalValue(object? value) =>
        value switch
        {
            null => "<null>",
            string text => Normalize(text),
            Guid guid => guid == Guid.Empty ? "<empty-guid>" : guid.ToString("D"),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.#############################", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>",
            _ => value.ToString() ?? "<null>"
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
}
