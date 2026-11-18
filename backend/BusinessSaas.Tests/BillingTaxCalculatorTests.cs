using BusinessSaas.Api.Features;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class BillingTaxCalculatorTests
{
    [Fact]
    public void Inclusive_intrastate_tax_keeps_the_captured_total()
    {
        var result = BillingTaxCalculator.Inclusive(1180m, 18m, true);
        Assert.Equal(1000m, result.Taxable);
        Assert.Equal(90m, result.Cgst);
        Assert.Equal(90m, result.Sgst);
        Assert.Equal(0m, result.Igst);
        Assert.Equal(result.Gross, result.Taxable + result.Tax);
    }

    [Fact]
    public void Different_state_uses_configured_rate_and_igst()
    {
        var result = BillingTaxCalculator.Inclusive(1050m, 5m, false);
        Assert.Equal(1000m, result.Taxable);
        Assert.Equal(50m, result.Igst);
        Assert.Equal(0m, result.Cgst);
    }

    [Fact]
    public void Two_refunds_allocate_the_full_taxable_amount_without_excess()
    {
        var first = BillingTaxCalculator.Refund(500m, 1180m, 1000m, 0m, 0m, true);
        var second = BillingTaxCalculator.Refund(680m, 1180m, 1000m,
            first.Gross, first.Taxable, true);
        Assert.Equal(1180m, first.Gross + second.Gross);
        Assert.Equal(1000m, first.Taxable + second.Taxable);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillingTaxCalculator.Refund(1m, 1180m, 1000m, 1180m, 1000m, true));
    }

    [Fact]
    public void Invalid_negative_and_fractional_amounts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillingTaxCalculator.Inclusive(-1m, 18m, true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillingTaxCalculator.Inclusive(20.001m, 18m, true));
    }
}
