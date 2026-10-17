using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderPricingCalculatorTests
{
    [Fact]
    public void CalculateLineItem_TaxIncluded_ExtractsTaxWithHalfUpRounding()
    {
        // 100.00 TRY brüt, %10 KDV (1000 bps)
        // Tax = (10000 * 1000 + 5500) / 11000 = 10005500 / 11000 = 909.59 -> 910 kuruş (9.10 TRY)
        // Net = 10000 - 909 = 9091 kuruş (veya 909 kuruş vergi, net 9091)
        // 10000000 / 11000 = 909.0909 -> RoundHalfUp(10000000, 11000) = (10000000 + 5500) / 11000 = 10005500 / 11000 = 909.
        var (lineTotal, netSubtotal, tax) = OrderPricingCalculator.CalculateLineItemFinancials(
            unitPriceMinorUnits: 10000L,
            modifierTotalMinorUnits: 0L,
            quantity: 1,
            taxRateBps: 1000,
            pricesIncludeTax: true);

        Assert.Equal(10000L, lineTotal);
        Assert.Equal(909L, tax);
        Assert.Equal(9091L, netSubtotal);
        Assert.Equal(lineTotal, netSubtotal + tax);
    }

    [Fact]
    public void CalculateLineItem_TaxExcluded_AddsTaxOnTop()
    {
        // 100.00 TRY net, %10 KDV (1000 bps)
        // Tax = (10000 * 1000 + 5000) / 10000 = 10005000 / 10000 = 1000 kuruş (10.00 TRY)
        // Net = 10000
        var (lineTotal, netSubtotal, tax) = OrderPricingCalculator.CalculateLineItemFinancials(
            unitPriceMinorUnits: 10000L,
            modifierTotalMinorUnits: 0L,
            quantity: 1,
            taxRateBps: 1000,
            pricesIncludeTax: false);

        Assert.Equal(10000L, lineTotal);
        Assert.Equal(10000L, netSubtotal);
        Assert.Equal(1000L, tax);
    }

    [Fact]
    public void CalculateLineItem_ZeroTax_TaxIsZero()
    {
        var (lineTotal, netSubtotal, tax) = OrderPricingCalculator.CalculateLineItemFinancials(
            unitPriceMinorUnits: 5000L,
            modifierTotalMinorUnits: 500L,
            quantity: 2,
            taxRateBps: 0,
            pricesIncludeTax: true);

        // (5000 + 500) * 2 = 11000
        Assert.Equal(11000L, lineTotal);
        Assert.Equal(11000L, netSubtotal);
        Assert.Equal(0L, tax);
    }

    [Fact]
    public void CalculateLineItem_WithModifiersAndQuantity()
    {
        // Base: 40.00 (4000), Modifiers: 10.00 (1000), Quantity: 3 -> LineTotal = 15000
        // %20 KDV (2000 bps), Tax Included
        // Tax = (15000 * 2000 + 6000) / 12000 = 30006000 / 12000 = 2500.5 -> 2500 kuruş
        var (lineTotal, netSubtotal, tax) = OrderPricingCalculator.CalculateLineItemFinancials(
            unitPriceMinorUnits: 4000L,
            modifierTotalMinorUnits: 1000L,
            quantity: 3,
            taxRateBps: 2000,
            pricesIncludeTax: true);

        Assert.Equal(15000L, lineTotal);
        Assert.Equal(2500L, tax);
        Assert.Equal(12500L, netSubtotal);
        Assert.Equal(lineTotal, netSubtotal + tax);
    }

    [Theory]
    [InlineData(10001L, 10000L, 1L)]   // 1.0001 -> 1
    [InlineData(14999L, 10000L, 1L)]   // 1.4999 -> 1
    [InlineData(15000L, 10000L, 2L)]   // 1.5000 -> 2 (Half-Up)
    [InlineData(15001L, 10000L, 2L)]   // 1.5001 -> 2
    [InlineData(19999L, 10000L, 2L)]   // 1.9999 -> 2
    public void RoundHalfUp_ExactBoundaryTests(long numerator, long denominator, long expected)
    {
        var actual = OrderPricingCalculator.RoundHalfUp(numerator, denominator);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CalculateOrderTotals_ServiceChargeEnabled_CalculatesCorrectly()
    {
        // Net: 10000, Tax: 1000 -> Gross: 11000
        // Service charge: %10 (1000 bps)
        // SC = (11000 * 1000 + 5000) / 10000 = 11005000 / 10000 = 1100 kuruş (11.00 TRY)
        // Grand total: 11000 + 1100 = 12100
        var (serviceCharge, grandTotal) = OrderPricingCalculator.CalculateOrderTotals(
            netSubtotalMinorUnits: 10000L,
            taxTotalMinorUnits: 1000L,
            isServiceChargeEnabled: true,
            serviceChargeRateBps: 1000);

        Assert.Equal(1100L, serviceCharge);
        Assert.Equal(12100L, grandTotal);
    }

    [Fact]
    public void CalculateOrderTotals_ServiceChargeDisabled_ServiceChargeIsZero()
    {
        var (serviceCharge, grandTotal) = OrderPricingCalculator.CalculateOrderTotals(
            netSubtotalMinorUnits: 10000L,
            taxTotalMinorUnits: 1000L,
            isServiceChargeEnabled: false,
            serviceChargeRateBps: 1000);

        Assert.Equal(0L, serviceCharge);
        Assert.Equal(11000L, grandTotal);
    }

    [Fact]
    public void OverflowProtection_VeryLargeAmounts_ThrowsOverflowException()
    {
        // Max long value checked multiplication should trigger OverflowException
        Assert.Throws<OverflowException>(() =>
            OrderPricingCalculator.CalculateLineItemFinancials(
                unitPriceMinorUnits: long.MaxValue / 2,
                modifierTotalMinorUnits: long.MaxValue / 2,
                quantity: 2,
                taxRateBps: 1000,
                pricesIncludeTax: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void CalculateLineItem_InvalidQuantity_ThrowsDomainException(int invalidQuantity)
    {
        Assert.Throws<DomainException>(() =>
            OrderPricingCalculator.CalculateLineItemFinancials(
                unitPriceMinorUnits: 1000L,
                modifierTotalMinorUnits: 0L,
                quantity: invalidQuantity,
                taxRateBps: 1000,
                pricesIncludeTax: true));
    }
}
