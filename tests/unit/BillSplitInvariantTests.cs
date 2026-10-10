using RestaurantOrder.Api.Domain.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class BillSplitInvariantTests
{
    private readonly PricingService _pricing = new();

    [Fact]
    public void TwoCentsAcrossFourGuests_NeverCreatesNegativeDebt()
    {
        Assert.Equal(new[] { 0m, 0m, 0.01m, 0.01m }, _pricing.SplitBillEqually(0.02m, 4));
    }

    [Fact]
    public void MultipleRemainingCents_AreDistributedAcrossGuests()
    {
        Assert.Equal(new[] { 33.33m, 33.34m, 33.34m }, _pricing.SplitBillEqually(100.01m, 3));
    }

    [Fact]
    public void WholeCentTotals_ConserveMoneyAndRemainFair()
    {
        for (var cents = 1; cents <= 1000; cents++)
        {
            for (var guests = 1; guests <= 50; guests++)
            {
                var total = cents / 100m;
                var shares = _pricing.SplitBillEqually(total, guests);
                Assert.Equal(guests, shares.Count);
                Assert.Equal(total, shares.Sum());
                Assert.All(shares, share =>
                {
                    Assert.True(share >= 0m);
                    Assert.Equal(share, decimal.Round(share, 2));
                });
                Assert.InRange(shares.Max() - shares.Min(), 0m, 0.01m);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FractionalCentTotal_IsRejectedEvenForSingleGuest(int guests)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _pricing.SplitBillEqually(1.001m, guests));
        Assert.Equal("totalAmount", error.ParamName);
    }
}
