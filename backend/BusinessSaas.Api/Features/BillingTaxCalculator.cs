namespace BusinessSaas.Api.Features;

public readonly record struct TaxBreakdown(decimal Gross, decimal Taxable,
    decimal Cgst, decimal Sgst, decimal Igst)
{
    public decimal Tax => Cgst + Sgst + Igst;
}

public static class BillingTaxCalculator
{
    public static TaxBreakdown Inclusive(decimal gross, decimal rate, bool sameState)
    {
        if (gross <= 0 || gross != decimal.Round(gross, 2) || rate is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(gross));

        var taxable = decimal.Round(gross * 100m / (100m + rate), 2,
            MidpointRounding.AwayFromZero);
        var tax = gross - taxable;
        if (!sameState)
            return new TaxBreakdown(gross, taxable, 0m, 0m, tax);

        var cgst = decimal.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
        return new TaxBreakdown(gross, taxable, cgst, tax - cgst, 0m);
    }

    public static TaxBreakdown Refund(decimal amount, decimal originalGross,
        decimal originalTaxable, decimal creditedGross, decimal creditedTaxable,
        bool sameState)
    {
        if (amount <= 0 || originalGross <= 0 ||
            amount > originalGross - creditedGross ||
            creditedTaxable > originalTaxable)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var taxable = amount == originalGross - creditedGross
            ? originalTaxable - creditedTaxable
            : decimal.Round(originalTaxable * amount / originalGross, 2,
                MidpointRounding.AwayFromZero);
        if (taxable < 0 || taxable > amount ||
            taxable > originalTaxable - creditedTaxable)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var tax = amount - taxable;
        if (!sameState)
            return new TaxBreakdown(amount, taxable, 0m, 0m, tax);

        var cgst = decimal.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
        return new TaxBreakdown(amount, taxable, cgst, tax - cgst, 0m);
    }
}
