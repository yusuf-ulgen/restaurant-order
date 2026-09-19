using Xunit;

namespace RestaurantOrder.UnitTests;

public class PricingCalculationTests
{
    [Fact]
    public void CalculateItemSubtotal_ShouldIncludeQuantityAndModifiers()
    {
        decimal basePrice = 120.00m;
        decimal modifierExtraCheese = 15.00m;
        decimal modifierBacon = 25.00m;
        int quantity = 2;

        decimal subtotal = (basePrice + modifierExtraCheese + modifierBacon) * quantity;

        Assert.Equal(320.00m, subtotal);
    }

    [Fact]
    public void SplitBillEqually_ShouldAccountForRoundingDiscrepancy()
    {
        decimal totalBill = 100.00m;
        int guestCount = 3;

        decimal baseShare = Math.Round(totalBill / guestCount, 2); // 33.33
        decimal allocatedSum = baseShare * (guestCount - 1);       // 66.66
        decimal finalGuestShare = totalBill - allocatedSum;        // 33.34

        Assert.Equal(33.33m, baseShare);
        Assert.Equal(33.34m, finalGuestShare);
        Assert.Equal(totalBill, (baseShare * 2) + finalGuestShare);
    }
}
