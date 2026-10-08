namespace RestaurantOrder.Api.Domain.Pricing;

/// <summary>
/// Domain service implementing restaurant monetary calculations, bill splitting, and platform commissions.
/// Currency operations use high-precision decimal and standard commercial rounding (MidpointRounding.AwayFromZero).
/// </summary>
public class PricingService : IPricingService
{
    public decimal CalculateLineItemSubtotal(decimal basePrice, IEnumerable<decimal>? modifierPrices, int quantity)
    {
        if (basePrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(basePrice), "Base price cannot be negative.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        decimal modifiersTotal = 0m;
        if (modifierPrices != null)
        {
            foreach (var modPrice in modifierPrices)
            {
                if (modPrice < 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(modifierPrices), "Modifier price cannot be negative.");
                }
                modifiersTotal += modPrice;
            }
        }

        var itemUnitPrice = basePrice + modifiersTotal;
        return Math.Round(itemUnitPrice * quantity, 2, MidpointRounding.AwayFromZero);
    }

    public IReadOnlyList<decimal> SplitBillEqually(decimal totalAmount, int guestCount)
    {
        if (totalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "Total amount must be greater than zero.");
        }

        if (guestCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(guestCount), "Guest count must be greater than zero.");
        }

        if (decimal.Round(totalAmount, 2) != totalAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "Total amount must contain whole cents.");
        }

        if (guestCount == 1)
        {
            return [totalAmount];
        }

        // Round down so every share is nonnegative and the remainder is whole cents.
        var baseShare = decimal.Round(totalAmount / guestCount, 2, MidpointRounding.ToZero);
        var remainingCents = (int)((totalAmount - baseShare * guestCount) * 100m);

        var result = new List<decimal>(guestCount);
        for (var i = 0; i < guestCount; i++)
        {
            // Assign one extra cent to each of the last remainingCents guests.
            result.Add(baseShare + (i >= guestCount - remainingCents ? 0.01m : 0m));
        }

        return result;
    }

    public decimal CalculateTax(decimal taxableAmount, decimal taxRatePercentage)
    {
        if (taxableAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxableAmount), "Taxable amount cannot be negative.");
        }

        if (taxRatePercentage < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRatePercentage), "Tax rate percentage cannot be negative.");
        }

        return Math.Round(taxableAmount * (taxRatePercentage / 100m), 2, MidpointRounding.AwayFromZero);
    }

    public PlatformCommissionResult CalculatePlatformCommission(
        decimal transactionAmount,
        decimal commissionRatePercentage,
        decimal fixedFee = 0m)
    {
        if (transactionAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionAmount), "Transaction amount cannot be negative.");
        }

        if (commissionRatePercentage < 0m || commissionRatePercentage > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commissionRatePercentage),
                "Commission rate percentage must be between 0 and 100.");
        }

        if (fixedFee < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedFee), "Fixed fee cannot be negative.");
        }

        var variableFee = Math.Round(transactionAmount * (commissionRatePercentage / 100m), 2, MidpointRounding.AwayFromZero);
        var totalCommission = variableFee + fixedFee;

        if (totalCommission > transactionAmount)
        {
            throw new InvalidOperationException("Total platform commission cannot exceed total transaction amount.");
        }

        var netAmount = transactionAmount - totalCommission;

        return new PlatformCommissionResult(transactionAmount, totalCommission, netAmount);
    }
}
