using RestaurantOrder.Api.Domain.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests;

/// <summary>
/// Unit tests for production domain PricingService.
/// Covers decimal currency precision, rounding modes, zero/negative validation,
/// indivisible bill splitting with penny remainder distribution, tax, and commissions.
/// Target: 100% branch and statement coverage for critical calculation paths.
/// </summary>
public class PricingCalculationTests
{
    private readonly IPricingService _pricingService = new PricingService();

    #region Line Item Subtotal Tests

    [Fact]
    public void CalculateLineItemSubtotal_WithModifiersAndQuantity_ReturnsCorrectSubtotal()
    {
        // Arrange
        const decimal basePrice = 120.00m;
        var modifiers = new[] { 15.00m, 25.00m };
        const int quantity = 2;

        // Act
        var subtotal = _pricingService.CalculateLineItemSubtotal(basePrice, modifiers, quantity);

        // Assert
        Assert.Equal(320.00m, subtotal);
    }

    [Fact]
    public void CalculateLineItemSubtotal_WithoutModifiers_CalculatesBaseTimesQuantity()
    {
        // Act
        var subtotalNullModifiers = _pricingService.CalculateLineItemSubtotal(50.00m, null, 3);
        var subtotalEmptyModifiers = _pricingService.CalculateLineItemSubtotal(50.00m, [], 3);

        // Assert
        Assert.Equal(150.00m, subtotalNullModifiers);
        Assert.Equal(150.00m, subtotalEmptyModifiers);
    }

    [Fact]
    public void CalculateLineItemSubtotal_WhenNegativeBasePrice_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculateLineItemSubtotal(-10.00m, null, 1));
        Assert.Contains("Base price cannot be negative", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void CalculateLineItemSubtotal_WhenZeroOrNegativeQuantity_ThrowsArgumentOutOfRangeException(int invalidQuantity)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculateLineItemSubtotal(10.00m, null, invalidQuantity));
        Assert.Contains("Quantity must be greater than zero", ex.Message);
    }

    [Fact]
    public void CalculateLineItemSubtotal_WhenNegativeModifierPrice_ThrowsArgumentOutOfRangeException()
    {
        var modifiers = new[] { 10.00m, -5.00m };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculateLineItemSubtotal(20.00m, modifiers, 1));
        Assert.Contains("Modifier price cannot be negative", ex.Message);
    }

    #endregion

    #region Split Bill Tests

    [Fact]
    public void SplitBillEqually_SingleGuest_ReturnsSingleShareMatchingTotal()
    {
        var shares = _pricingService.SplitBillEqually(75.50m, 1);

        Assert.Single(shares);
        Assert.Equal(75.50m, shares[0]);
    }

    [Fact]
    public void SplitBillEqually_EvenlyDivisible_ReturnsIdenticalShares()
    {
        var shares = _pricingService.SplitBillEqually(100.00m, 4);

        Assert.Equal(4, shares.Count);
        Assert.All(shares, share => Assert.Equal(25.00m, share));
        Assert.Equal(100.00m, shares.Sum());
    }

    [Fact]
    public void SplitBillEqually_IndivisibleBill_AllocatesPennyDiscrepancyToFinalGuest()
    {
        // 100.00 / 3 = 33.3333...
        // Guest 1: 33.33
        // Guest 2: 33.33
        // Guest 3 (Final): 33.34
        var shares = _pricingService.SplitBillEqually(100.00m, 3);

        Assert.Equal(3, shares.Count);
        Assert.Equal(33.33m, shares[0]);
        Assert.Equal(33.33m, shares[1]);
        Assert.Equal(33.34m, shares[2]);
        Assert.Equal(100.00m, shares.Sum());
    }

    [Theory]
    [InlineData(10.00, 3)]
    [InlineData(100.01, 3)]
    [InlineData(250.75, 6)]
    [InlineData(1000.00, 7)]
    [InlineData(0.05, 4)]
    public void SplitBillEqually_ArbitraryIndivisibleAmounts_InvariantTotalSumAlwaysEqualsBill(
        decimal total,
        int guests)
    {
        var shares = _pricingService.SplitBillEqually(total, guests);

        Assert.Equal(guests, shares.Count);
        Assert.Equal(total, shares.Sum());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void SplitBillEqually_WhenTotalZeroOrNegative_ThrowsArgumentOutOfRangeException(decimal invalidTotal)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.SplitBillEqually(invalidTotal, 2));
        Assert.Contains("Total amount must be greater than zero", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SplitBillEqually_WhenGuestCountZeroOrNegative_ThrowsArgumentOutOfRangeException(int invalidGuests)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.SplitBillEqually(100.00m, invalidGuests));
        Assert.Contains("Guest count must be greater than zero", ex.Message);
    }

    #endregion

    #region Tax Calculation Tests

    [Fact]
    public void CalculateTax_ValidAmountAndRate_ReturnsAccuratelyRoundedTax()
    {
        // 12.35 * 0.08 = 0.988 -> 0.99
        var tax = _pricingService.CalculateTax(12.35m, 8.00m);
        Assert.Equal(0.99m, tax);
    }

    [Fact]
    public void CalculateTax_ZeroTaxRateOrAmount_ReturnsZero()
    {
        Assert.Equal(0.00m, _pricingService.CalculateTax(100.00m, 0m));
        Assert.Equal(0.00m, _pricingService.CalculateTax(0.00m, 18m));
    }

    [Fact]
    public void CalculateTax_WhenNegativeTaxableAmount_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculateTax(-50m, 18m));
        Assert.Contains("Taxable amount cannot be negative", ex.Message);
    }

    [Fact]
    public void CalculateTax_WhenNegativeTaxRate_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculateTax(100m, -5m));
        Assert.Contains("Tax rate percentage cannot be negative", ex.Message);
    }

    #endregion

    #region Platform Commission Tests

    [Fact]
    public void CalculatePlatformCommission_WithVariableAndFixedFee_ComputesDualEntryLedgerCorrectly()
    {
        // Gross: 200.00, Commission: 2.5% (= 5.00) + fixed 1.50 = 6.50
        // Net: 200.00 - 6.50 = 193.50
        var result = _pricingService.CalculatePlatformCommission(200.00m, 2.5m, 1.50m);

        Assert.Equal(200.00m, result.GrossAmount);
        Assert.Equal(6.50m, result.CommissionFee);
        Assert.Equal(193.50m, result.NetAmount);
        Assert.Equal(result.GrossAmount, result.CommissionFee + result.NetAmount);
    }

    [Fact]
    public void CalculatePlatformCommission_WithoutFixedFee_DefaultsToZeroFixed()
    {
        var result = _pricingService.CalculatePlatformCommission(100.00m, 3.0m);

        Assert.Equal(100.00m, result.GrossAmount);
        Assert.Equal(3.00m, result.CommissionFee);
        Assert.Equal(97.00m, result.NetAmount);
    }

    [Fact]
    public void CalculatePlatformCommission_ZeroTransaction_ReturnsZeroResult()
    {
        var result = _pricingService.CalculatePlatformCommission(0m, 5.0m, 0m);

        Assert.Equal(0m, result.GrossAmount);
        Assert.Equal(0m, result.CommissionFee);
        Assert.Equal(0m, result.NetAmount);
    }

    [Fact]
    public void CalculatePlatformCommission_NegativeTransaction_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculatePlatformCommission(-10m, 5.0m));
        Assert.Contains("Transaction amount cannot be negative", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CalculatePlatformCommission_InvalidCommissionRate_ThrowsArgumentOutOfRangeException(decimal invalidRate)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculatePlatformCommission(100m, invalidRate));
        Assert.Contains("Commission rate percentage must be between 0 and 100", ex.Message);
    }

    [Fact]
    public void CalculatePlatformCommission_NegativeFixedFee_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricingService.CalculatePlatformCommission(100m, 5.0m, -1m));
        Assert.Contains("Fixed fee cannot be negative", ex.Message);
    }

    [Fact]
    public void CalculatePlatformCommission_TotalCommissionExceedsGross_ThrowsInvalidOperationException()
    {
        // Transaction 5.00, Fixed fee 10.00 -> Fee 10.00 > Gross 5.00
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _pricingService.CalculatePlatformCommission(5.00m, 0m, 10.00m));
        Assert.Contains("Total platform commission cannot exceed total transaction amount", ex.Message);
    }

    #endregion
}
