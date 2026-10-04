using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class PriceAmountUnitTests
{
    [Fact]
    public void FromMinorUnits_Zero_ReturnsZeroPriceAmount()
    {
        var price = PriceAmount.FromMinorUnits(0);
        Assert.Equal(0, price.MinorUnits);
        Assert.Equal(PriceAmount.Zero, price);
    }

    [Fact]
    public void FromMinorUnits_PositiveValidAmount_Succeeds()
    {
        var price = PriceAmount.FromMinorUnits(15000); // 150.00
        Assert.Equal(15000, price.MinorUnits);
    }

    [Fact]
    public void FromMinorUnits_MaxAllowedAmount_Succeeds()
    {
        var price = PriceAmount.FromMinorUnits(PriceAmount.MaxMinorUnits);
        Assert.Equal(PriceAmount.MaxMinorUnits, price.MinorUnits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(long.MinValue)]
    public void FromMinorUnits_NegativeAmount_ThrowsDomainException(long negativeAmount)
    {
        var ex = Assert.Throws<DomainException>(() => PriceAmount.FromMinorUnits(negativeAmount));
        Assert.Contains("cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PriceAmount.MaxMinorUnits + 1)]
    [InlineData(long.MaxValue)]
    public void FromMinorUnits_ExcessiveAmount_ThrowsDomainException(long excessiveAmount)
    {
        var ex = Assert.Throws<DomainException>(() => PriceAmount.FromMinorUnits(excessiveAmount));
        Assert.Contains("cannot exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Addition_ValidOperands_ComputesSum()
    {
        var p1 = PriceAmount.FromMinorUnits(500);
        var p2 = PriceAmount.FromMinorUnits(750);
        var sum = p1 + p2;

        Assert.Equal(1250, sum.MinorUnits);
    }

    [Fact]
    public void Addition_OverflowAboveMax_ThrowsDomainException()
    {
        var p1 = PriceAmount.FromMinorUnits(PriceAmount.MaxMinorUnits);
        var p2 = PriceAmount.FromMinorUnits(1);

        var ex = Assert.Throws<DomainException>(() => _ = p1 + p2);
        Assert.Contains("cannot exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Subtraction_ValidOperands_ComputesDifference()
    {
        var p1 = PriceAmount.FromMinorUnits(1000);
        var p2 = PriceAmount.FromMinorUnits(400);
        var diff = p1 - p2;

        Assert.Equal(600, diff.MinorUnits);
    }

    [Fact]
    public void Subtraction_NegativeResult_ThrowsDomainException()
    {
        var p1 = PriceAmount.FromMinorUnits(300);
        var p2 = PriceAmount.FromMinorUnits(500);

        var ex = Assert.Throws<DomainException>(() => _ = p1 - p2);
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Multiplication_ValidScalar_ComputesProduct()
    {
        var price = PriceAmount.FromMinorUnits(250);
        var multiplied = price * 3;

        Assert.Equal(750, multiplied.MinorUnits);
    }

    [Fact]
    public void Multiplication_NegativeMultiplier_ThrowsDomainException()
    {
        var price = PriceAmount.FromMinorUnits(250);
        var ex = Assert.Throws<DomainException>(() => _ = price * -1);
        Assert.Contains("Multiplier cannot be negative", ex.Message);
    }

    [Fact]
    public void Comparisons_WorkProperly()
    {
        var low = PriceAmount.FromMinorUnits(100);
        var high = PriceAmount.FromMinorUnits(200);

        Assert.True(low < high);
        Assert.True(low <= high);
        Assert.True(high > low);
        Assert.True(high >= low);
        Assert.True(low == PriceAmount.FromMinorUnits(100));
        Assert.True(low != high);
    }

    [Fact]
    public void ToString_FormatsInvariantIntegerRepresentation()
    {
        var price = PriceAmount.FromMinorUnits(123456);
        Assert.Equal("123456", price.ToString());
    }
}
