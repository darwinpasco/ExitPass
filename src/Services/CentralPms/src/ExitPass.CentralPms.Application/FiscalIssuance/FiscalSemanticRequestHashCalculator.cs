using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalSemanticRequestHashCalculator : IFiscalSemanticRequestHashCalculator
{
    public const string CurrentHashAlgorithm = "SHA-256";
    public const string CurrentHashSourceVersion = "sha256:v1";

    private static readonly JsonWriterOptions CanonicalJsonOptions = new()
    {
        Indented = false
    };

    public FiscalSemanticRequestHashResult Calculate(PosServerFiscalDocumentCreateRequest request)
    {
        var inspection = InspectCanonicalSource(request);

        return new FiscalSemanticRequestHashResult(
            Status: inspection.Status,
            HashValue: inspection.HashValue,
            HashAlgorithm: inspection.HashAlgorithm,
            HashSourceVersion: inspection.HashSourceVersion,
            SourceFactCount: inspection.SourceFactCount,
            SafeSourceSummary: inspection.SafeSourceSummary,
            BlockReasonCode: inspection.BlockReasonCode);
    }

    public FiscalSemanticRequestHashCanonicalInspectionResult InspectCanonicalSource(
        PosServerFiscalDocumentCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missingFacts = MissingRequiredFacts(request);
        if (missingFacts.Count > 0)
        {
            return new FiscalSemanticRequestHashCanonicalInspectionResult(
                Status: FiscalSemanticRequestHashSourceStatus.Incomplete,
                HashValue: null,
                HashAlgorithm: CurrentHashAlgorithm,
                HashSourceVersion: CurrentHashSourceVersion,
                SourceFactCount: 0,
                SafeSourceSummary: $"semantic_request_hash_source_incomplete:{string.Join(",", missingFacts)}",
                BlockReasonCode: missingFacts[0],
                NormalizedFacts: Array.Empty<string>(),
                CanonicalSourceText: null);
        }

        var canonicalSource = CanonicalSource(request);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSource))).ToLowerInvariant();
        var topLevelFacts = TopLevelFactNames();

        return new FiscalSemanticRequestHashCanonicalInspectionResult(
            Status: FiscalSemanticRequestHashSourceStatus.Available,
            HashValue: hash,
            HashAlgorithm: CurrentHashAlgorithm,
            HashSourceVersion: CurrentHashSourceVersion,
            SourceFactCount: topLevelFacts.Length,
            SafeSourceSummary: $"semantic_request_hash_source_available:facts={topLevelFacts.Length}",
            BlockReasonCode: null,
            NormalizedFacts: topLevelFacts,
            CanonicalSourceText: canonicalSource);
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

    private static string CanonicalSource(PosServerFiscalDocumentCreateRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CanonicalJsonOptions))
        {
            writer.WriteStartObject();
            WriteString(writer, "business_day_date", request.BusinessDayDate);
            WriteString(writer, "central_pms_parking_session_ref", request.CentralPmsParkingSessionRef);
            WriteString(writer, "central_pms_payment_attempt_ref", request.CentralPmsPaymentAttemptRef);
            WriteString(writer, "central_pms_payment_confirmation_ref", request.CentralPmsPaymentConfirmationRef);
            WriteString(writer, "channel_terminal_id", request.ChannelTerminalId);
            WriteDiscountPrivileges(writer, request.DiscountPrivilegeDetails);
            WriteDocumentLines(writer, request.DocumentLines);
            writer.WritePropertyName("document_links");
            writer.WriteStartArray();
            writer.WriteEndArray();
            WriteString(writer, "fiscal_document_status_code_id", request.FiscalDocumentStatusCodeId);
            WriteString(writer, "fiscal_document_type_code_id", request.FiscalDocumentTypeCodeId);
            WriteString(writer, "fiscal_document_type_code_key", request.FiscalDocumentTypeCodeKey);
            WritePayableBasis(writer, request.PayableBasis);
            WriteString(writer, "payment_finality_ref", request.PaymentFinalityRef);
            WriteDictionary(writer, "reference_context", request.ReferenceContext);
            WriteString(writer, "site_pos_server_id", request.SitePosServerId);
            WriteString(writer, "site_pos_server_ref", request.SitePosServerRef);
            WriteTaxDetails(writer, request.TaxDetails);
            WriteTenders(writer, request.Tenders);
            WriteTotals(writer, request.Totals);
            WriteString(writer, "vendor_ack_ref", request.VendorAckRef);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePayableBasis(Utf8JsonWriter writer, PosServerPayableBasisRequest payableBasis)
    {
        writer.WritePropertyName("payable_basis");
        writer.WriteStartObject();
        WriteString(writer, "currency_code", NormalizeCurrency(payableBasis.CurrencyCode));
        writer.WritePropertyName("discount_references");
        writer.WriteStartArray();
        foreach (var discount in payableBasis.DiscountReferences.OrderBy(discount => Normalize(discount.DiscountValidationRef), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("applies_statutory_discount_treatment", discount.AppliesStatutoryDiscountTreatment);
            WriteString(writer, "applied_policy_reference_ref", discount.AppliedPolicyReferenceRef);
            WriteString(writer, "applied_tariff_snapshot_ref", discount.AppliedTariffSnapshotRef);
            WriteNullableNumber(writer, "discount_amount_minor_units", discount.DiscountAmountMinorUnits);
            WriteString(writer, "discount_validation_ref", discount.DiscountValidationRef);
            WriteString(writer, "entitlement_type", discount.EntitlementType);
            WriteNullableNumber(writer, "final_payable_amount_minor_units", discount.FinalPayableAmountMinorUnits);
            WriteNullableNumber(writer, "original_amount_minor_units", discount.OriginalAmountMinorUnits);
            WriteString(writer, "original_tariff_snapshot_ref", discount.OriginalTariffSnapshotRef);
            WriteDictionary(writer, "reference_context", discount.ReferenceContext);
            WriteString(writer, "source_channel", discount.SourceChannel);
            WriteString(writer, "status", NormalizeDiscountStatus(discount.Status));
            WriteString(writer, "statutory_discount_decision_command_ref", discount.StatutoryDiscountDecisionCommandRef);
            WriteNullableNumber(writer, "vat_exclusive_basis_amount_minor_units", discount.VatExclusiveBasisAmountMinorUnits);
            WriteString(writer, "vat_treatment", discount.VatTreatment);
            WriteString(writer, "decision_timestamp", discount.DecisionTimestamp);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("payable_amount_minor_units", payableBasis.PayableAmountMinorUnits);
        WriteString(writer, "payable_basis_ref", payableBasis.PayableBasisRef);
        WriteDictionary(writer, "reference_context", payableBasis.ReferenceContext);
        WriteString(writer, "upstream_finality_ref", payableBasis.UpstreamFinalityRef);
        writer.WriteEndObject();
    }

    private static void WriteDocumentLines(Utf8JsonWriter writer, IReadOnlyList<PosServerFiscalDocumentLineRequest> lines)
    {
        writer.WritePropertyName("document_lines");
        writer.WriteStartArray();
        foreach (var line in lines.OrderBy(line => line.LineSequence).ThenBy(line => Normalize(line.SourceRef), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            WriteString(writer, "currency_code", NormalizeCurrency(line.CurrencyCode));
            WriteString(writer, "description", line.Description);
            writer.WriteNumber("discount_amount_minor_units", line.DiscountAmountMinorUnits);
            writer.WriteNumber("gross_amount_minor_units", line.GrossAmountMinorUnits);
            WriteDictionary(writer, "line_context", line.LineContext);
            writer.WriteNumber("line_sequence", line.LineSequence);
            WriteString(writer, "line_status_code_id", line.LineStatusCodeId);
            WriteString(writer, "line_type_code_id", line.LineTypeCodeId);
            writer.WriteNumber("net_amount_minor_units", line.NetAmountMinorUnits);
            WriteNumber(writer, "quantity", line.Quantity);
            WriteString(writer, "source_ref", line.SourceRef);
            writer.WriteNumber("tax_amount_minor_units", line.TaxAmountMinorUnits);
            writer.WriteNumber("unit_amount_minor_units", line.UnitAmountMinorUnits);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTenders(Utf8JsonWriter writer, IReadOnlyList<PosServerFiscalTenderRequest> tenders)
    {
        writer.WritePropertyName("tenders");
        writer.WriteStartArray();
        foreach (var tender in tenders.OrderBy(tender => tender.TenderTypeCodeId).ThenBy(tender => Normalize(tender.ProviderRef), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteNumber("amount_minor_units", tender.AmountMinorUnits);
            WriteString(writer, "central_pms_payment_attempt_ref", tender.CentralPmsPaymentAttemptRef);
            WriteString(writer, "central_pms_payment_confirmation_ref", tender.CentralPmsPaymentConfirmationRef);
            WriteString(writer, "currency_code", NormalizeCurrency(tender.CurrencyCode));
            WriteString(writer, "payment_finality_ref", tender.PaymentFinalityRef);
            WriteString(writer, "provider_ref", tender.ProviderRef);
            WriteDictionary(writer, "tender_context", tender.TenderContext);
            WriteString(writer, "tender_type_code_id", tender.TenderTypeCodeId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTaxDetails(Utf8JsonWriter writer, IReadOnlyList<PosServerFiscalTaxDetailRequest> taxes)
    {
        writer.WritePropertyName("tax_details");
        writer.WriteStartArray();
        foreach (var tax in taxes.OrderBy(tax => tax.LineSequence).ThenBy(tax => tax.TaxTypeCodeId))
        {
            writer.WriteStartObject();
            WriteString(writer, "currency_code", NormalizeCurrency(tax.CurrencyCode));
            WriteNullableNumber(writer, "line_sequence", tax.LineSequence);
            writer.WriteNumber("tax_amount_minor_units", tax.TaxAmountMinorUnits);
            WriteString(writer, "tax_classification_code_id", tax.TaxClassificationCodeId);
            WriteDictionary(writer, "tax_context", tax.TaxContext);
            WriteNullableNumber(writer, "tax_rate", tax.TaxRate);
            WriteString(writer, "tax_type_code_id", tax.TaxTypeCodeId);
            writer.WriteNumber("taxable_amount_minor_units", tax.TaxableAmountMinorUnits);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDiscountPrivileges(
        Utf8JsonWriter writer,
        IReadOnlyList<PosServerFiscalDiscountPrivilegeDetailRequest> discounts)
    {
        writer.WritePropertyName("discount_privilege_details");
        writer.WriteStartArray();
        foreach (var discount in discounts.OrderBy(discount => discount.LineSequence).ThenBy(discount => discount.DiscountPrivilegeTypeCodeId))
        {
            writer.WriteStartObject();
            WriteString(writer, "approval_ref", discount.ApprovalRef);
            writer.WriteNumber("basis_amount_minor_units", discount.BasisAmountMinorUnits);
            WriteString(writer, "beneficiary_ref", discount.BeneficiaryRef);
            WriteString(writer, "currency_code", NormalizeCurrency(discount.CurrencyCode));
            writer.WriteNumber("discount_amount_minor_units", discount.DiscountAmountMinorUnits);
            WriteDictionary(writer, "discount_privilege_context", discount.DiscountPrivilegeContext);
            WriteString(writer, "discount_privilege_type_code_id", discount.DiscountPrivilegeTypeCodeId);
            WriteString(writer, "evidence_ref", discount.EvidenceRef);
            WriteNullableNumber(writer, "line_sequence", discount.LineSequence);
            writer.WriteNumber("vat_privilege_amount_minor_units", discount.VatPrivilegeAmountMinorUnits);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTotals(Utf8JsonWriter writer, IReadOnlyList<PosServerFiscalTotalRequest> totals)
    {
        writer.WritePropertyName("totals");
        writer.WriteStartArray();
        foreach (var total in totals.OrderBy(total => total.TotalTypeCodeId))
        {
            writer.WriteStartObject();
            writer.WriteNumber("amount_minor_units", total.AmountMinorUnits);
            WriteString(writer, "currency_code", NormalizeCurrency(total.CurrencyCode));
            WriteDictionary(writer, "total_context", total.TotalContext);
            WriteString(writer, "total_type_code_id", total.TotalTypeCodeId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDictionary(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyDictionary<string, string> dictionary)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        foreach (var pair in dictionary
                     .Where(pair => !IsTransportOnlyDictionaryKey(pair.Key))
                     .OrderBy(pair => Normalize(pair.Key), StringComparer.Ordinal))
        {
            WriteString(writer, Normalize(pair.Key), pair.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, Normalize(value));
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, Guid? value)
    {
        if (value is null || value == Guid.Empty)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value.Value.ToString("D"));
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, DateOnly? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, DateTimeOffset? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
    }

    private static void WriteNumber(Utf8JsonWriter writer, string propertyName, decimal value) =>
        writer.WriteNumber(propertyName, value);

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, decimal? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    private static void AddIfMissing(List<string> missing, string? value, string reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(reason);
        }
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeCurrency(string? value) =>
        Normalize(value).ToUpperInvariant();

    private static bool IsTransportOnlyDictionaryKey(string? key)
    {
        var normalized = Normalize(key).Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
        return normalized is "CORRELATION" or "CORRELATION_ID" or "CORRELATIONID" or "X_CORRELATION_ID";
    }

    private static string NormalizeDiscountStatus(string? value)
    {
        var normalized = Normalize(value);
        return string.Equals(normalized, "approved", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : normalized;
    }

    private static string[] TopLevelFactNames() =>
    [
        "business_day_date",
        "central_pms_parking_session_ref",
        "central_pms_payment_attempt_ref",
        "central_pms_payment_confirmation_ref",
        "channel_terminal_id",
        "discount_privilege_details",
        "document_lines",
        "document_links",
        "fiscal_document_status_code_id",
        "fiscal_document_type_code_id",
        "fiscal_document_type_code_key",
        "payable_basis",
        "payment_finality_ref",
        "reference_context",
        "site_pos_server_id",
        "site_pos_server_ref",
        "tax_details",
        "tenders",
        "totals",
        "vendor_ack_ref"
    ];
}
