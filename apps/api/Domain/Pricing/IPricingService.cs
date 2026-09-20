namespace RestaurantOrder.Api.Domain.Pricing;

/// <summary>
/// Domain service interface for monetary calculations, bill splitting, tax, and commissions.
/// Adheres strictly to docs/PAYMENTS-TIPS-COMMISSIONS.md and docs/TESTING.md requirements.
/// </summary>
public interface IPricingService
{
    /// <summary>
    /// Calculates subtotal for an order line item including base price, modifier additions, and quantity.
    /// </summary>
    decimal CalculateLineItemSubtotal(decimal basePrice, IEnumerable<decimal>? modifierPrices, int quantity);

    /// <summary>
    /// Splits a total bill equally across N guests, distributing rounding penny discrepancies to the final guest.
    /// Guaranteed invariant: Sum of all guest shares strictly equals totalAmount.
    /// </summary>
    IReadOnlyList<decimal> SplitBillEqually(decimal totalAmount, int guestCount);

    /// <summary>
    /// Calculates sales/value-added tax on a taxable amount using commercial rounding.
    /// </summary>
    decimal CalculateTax(decimal taxableAmount, decimal taxRatePercentage);

    /// <summary>
    /// Calculates platform commission fee and net tenant payout for digital gateway transactions.
    /// </summary>
    PlatformCommissionResult CalculatePlatformCommission(
        decimal transactionAmount,
        decimal commissionRatePercentage,
        decimal fixedFee = 0m);
}
