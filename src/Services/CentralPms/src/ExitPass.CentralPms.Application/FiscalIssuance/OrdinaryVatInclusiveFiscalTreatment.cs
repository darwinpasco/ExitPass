namespace ExitPass.CentralPms.Application.FiscalIssuance;

internal static class OrdinaryVatInclusiveFiscalTreatment
{
    private const decimal VatRate = 0.12m;

    internal static readonly Guid VatTaxTypeCodeId =
        Guid.Parse("328dcb64-584a-5f59-a304-2e5189a2aa83");

    internal static readonly Guid VatableTaxClassificationCodeId =
        Guid.Parse("ab180f41-e181-5579-b9f1-5ae7a840a946");

    internal static OrdinaryVatInclusiveFiscalFacts Calculate(long grossAmountMinorUnits)
    {
        if (grossAmountMinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossAmountMinorUnits),
                grossAmountMinorUnits,
                "An ordinary VAT-inclusive fiscal amount must be positive.");
        }

        var vatableSalesMinorUnits = decimal.ToInt64(decimal.Round(
            grossAmountMinorUnits / (1m + VatRate),
            0,
            MidpointRounding.AwayFromZero));
        var vatAmountMinorUnits = grossAmountMinorUnits - vatableSalesMinorUnits;

        if (vatableSalesMinorUnits < 0 ||
            vatAmountMinorUnits < 0 ||
            vatableSalesMinorUnits + vatAmountMinorUnits != grossAmountMinorUnits)
        {
            throw new InvalidOperationException("ordinary_vat_fiscal_facts_do_not_reconcile");
        }

        return new OrdinaryVatInclusiveFiscalFacts(
            grossAmountMinorUnits,
            vatableSalesMinorUnits,
            vatAmountMinorUnits);
    }

    internal static CentralPmsFiscalTaxDetailContext CreateTaxDetail(
        OrdinaryVatInclusiveFiscalFacts facts,
        string currency,
        int lineSequence = 1)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.VatableSalesMinorUnits < 0 ||
            facts.VatAmountMinorUnits < 0 ||
            facts.VatableSalesMinorUnits + facts.VatAmountMinorUnits != facts.GrossAmountMinorUnits)
        {
            throw new InvalidOperationException("ordinary_vat_fiscal_facts_do_not_reconcile");
        }

        return new CentralPmsFiscalTaxDetailContext(
            VatTaxTypeCodeId,
            VatableTaxClassificationCodeId,
            facts.VatableSalesMinorUnits,
            facts.VatAmountMinorUnits,
            currency,
            lineSequence,
            12m,
            new Dictionary<string, string> { ["basis"] = "VAT_INCLUSIVE_NO_EXEMPTION" });
    }
}

internal sealed record OrdinaryVatInclusiveFiscalFacts(
    long GrossAmountMinorUnits,
    long VatableSalesMinorUnits,
    long VatAmountMinorUnits);
