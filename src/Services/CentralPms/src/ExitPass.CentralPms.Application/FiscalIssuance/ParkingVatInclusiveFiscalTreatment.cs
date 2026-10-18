namespace ExitPass.CentralPms.Application.FiscalIssuance;

public static class ParkingVatInclusiveFiscalTreatment
{
    internal const decimal VatRate = 0.12m;
    public const string VatTreatment = "VAT_INCLUSIVE_NO_EXEMPTION";

    internal static ParkingVatInclusiveFiscalFacts Calculate(
        long originalAmountMinorUnits,
        long vatExclusiveBasisAmountMinorUnits,
        long statutoryDiscountAmountMinorUnits,
        long finalPayableAmountMinorUnits)
    {
        if (originalAmountMinorUnits <= 0 ||
            vatExclusiveBasisAmountMinorUnits < 0 ||
            statutoryDiscountAmountMinorUnits < 0 ||
            statutoryDiscountAmountMinorUnits > vatExclusiveBasisAmountMinorUnits ||
            finalPayableAmountMinorUnits < 0)
        {
            throw new InvalidOperationException("parking_vat_fiscal_facts_do_not_reconcile");
        }

        var originalVatAmountMinorUnits = originalAmountMinorUnits - vatExclusiveBasisAmountMinorUnits;
        var taxableAmountMinorUnits = vatExclusiveBasisAmountMinorUnits - statutoryDiscountAmountMinorUnits;
        var vatAmountMinorUnits = decimal.ToInt64(decimal.Round(
            taxableAmountMinorUnits * VatRate,
            0,
            MidpointRounding.AwayFromZero));

        if (originalVatAmountMinorUnits < 0 ||
            taxableAmountMinorUnits + vatAmountMinorUnits != finalPayableAmountMinorUnits)
        {
            throw new InvalidOperationException("parking_vat_fiscal_facts_do_not_reconcile");
        }

        return new ParkingVatInclusiveFiscalFacts(
            vatExclusiveBasisAmountMinorUnits,
            originalVatAmountMinorUnits,
            taxableAmountMinorUnits,
            vatAmountMinorUnits,
            statutoryDiscountAmountMinorUnits,
            finalPayableAmountMinorUnits);
    }
}

internal sealed record ParkingVatInclusiveFiscalFacts(
    long OriginalVatExclusiveAmountMinorUnits,
    long OriginalVatAmountMinorUnits,
    long TaxableAmountMinorUnits,
    long VatAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long FinalPayableAmountMinorUnits);
