using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders.Pricing;

/// <summary>
/// Authoritative pure financial calculator for restaurant orders.
/// 
/// Core Invariants & Financial Rules:
/// 1. Uses strictly 64-bit integer minor-units (e.g. kuruş, cents) and integer basis points (bps, 10000 bps = 100%).
/// 2. Floating point types (double, float, decimal) are strictly prohibited to prevent representation errors.
/// 3. All arithmetic operations are protected against overflow using checked blocks.
/// 4. Rounding Algorithm (Half-Up AwayFromZero):
///    Formula: (Numerator + Denominator / 2) / Denominator for positive numbers.
///    Guarantees deterministic, identical rounding across all platform layers.
/// 5. Tax Policy:
///    - When PricesIncludeTax is true: Catalog prices represent gross totals. Included tax is extracted as:
///      Tax = RoundHalfUp(GrossTotal * TaxRateBps, 10000 + TaxRateBps)
///      NetSubtotal = GrossTotal - Tax
///    - When PricesIncludeTax is false: Catalog prices represent net amounts. Tax is added on top as:
///      Tax = RoundHalfUp(NetSubtotal * TaxRateBps, 10000)
/// 6. Service Charge Policy:
///    - Applied to the post-tax gross total: GrossTotal = NetSubtotal + Tax
///    - ServiceCharge = RoundHalfUp(GrossTotal * ServiceChargeRateBps, 10000)
///    - When disabled, ServiceCharge = 0.
/// 7. Final Order Total:
///    - Total = checked(GrossTotal + ServiceCharge)
/// </summary>
public static class OrderPricingCalculator
{
    public const int BasisPointsDivisor = 10000;

    /// <summary>
    /// Performs standard Half-Up (Away from zero) integer division rounding.
    /// Example: For denominator 10000, 5000 and above rounds up, 4999 rounds down.
    /// </summary>
    public static long RoundHalfUp(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            throw new DomainException($"Denominator must be greater than zero. Received: {denominator}.");
        }

        if (numerator < 0)
        {
            throw new DomainException($"Numerator cannot be negative in financial calculations. Received: {numerator}.");
        }

        checked
        {
            var halfDenominator = denominator / 2L;
            return (numerator + halfDenominator) / denominator;
        }
    }

    /// <summary>
    /// Calculates individual line item financials based on effective unit price, modifiers, quantity, and tax policy.
    /// </summary>
    public static (long lineTotalMinorUnits, long netSubtotalMinorUnits, long taxMinorUnits) CalculateLineItemFinancials(
        long unitPriceMinorUnits,
        long modifierTotalMinorUnits,
        int quantity,
        int taxRateBps,
        bool pricesIncludeTax)
    {
        if (unitPriceMinorUnits < 0)
        {
            throw new DomainException("Unit price cannot be negative.");
        }

        if (modifierTotalMinorUnits < 0)
        {
            throw new DomainException("Modifier total cannot be negative.");
        }

        if (quantity < 1 || quantity > 99)
        {
            throw new DomainException($"Quantity must be between 1 and 99. Received: {quantity}.");
        }

        if (taxRateBps < 0 || taxRateBps > BasisPointsDivisor)
        {
            throw new DomainException($"Tax rate basis points must be between 0 and {BasisPointsDivisor}. Received: {taxRateBps}.");
        }

        long singleItemTotal;
        long lineTotal;
        checked
        {
            singleItemTotal = unitPriceMinorUnits + modifierTotalMinorUnits;
            lineTotal = singleItemTotal * quantity;
        }

        if (taxRateBps == 0)
        {
            return (lineTotal, lineTotal, 0L);
        }

        if (pricesIncludeTax)
        {
            // Catalog line total is gross (tax inclusive)
            // Tax = (lineTotal * taxRateBps) / (10000 + taxRateBps)
            long tax;
            checked
            {
                var numerator = lineTotal * taxRateBps;
                var denominator = BasisPointsDivisor + (long)taxRateBps;
                tax = RoundHalfUp(numerator, denominator);
            }

            var netSubtotal = lineTotal - tax;
            return (lineTotal, netSubtotal, tax);
        }
        else
        {
            // Catalog line total is net (tax exclusive)
            // Tax = (lineTotal * taxRateBps) / 10000
            long tax;
            checked
            {
                var numerator = lineTotal * taxRateBps;
                tax = RoundHalfUp(numerator, BasisPointsDivisor);
            }

            return (lineTotal, lineTotal, tax);
        }
    }

    /// <summary>
    /// Calculates order-level service charge and grand total based on aggregated gross total and branch settings.
    /// </summary>
    public static (long serviceChargeMinorUnits, long grandTotalMinorUnits) CalculateOrderTotals(
        long netSubtotalMinorUnits,
        long taxTotalMinorUnits,
        bool isServiceChargeEnabled,
        int serviceChargeRateBps)
    {
        if (netSubtotalMinorUnits < 0 || taxTotalMinorUnits < 0)
        {
            throw new DomainException("Financial subtotals cannot be negative.");
        }

        if (serviceChargeRateBps < 0 || serviceChargeRateBps > BasisPointsDivisor)
        {
            throw new DomainException($"Service charge rate basis points must be between 0 and {BasisPointsDivisor}. Received: {serviceChargeRateBps}.");
        }

        long grossTotal;
        checked
        {
            grossTotal = netSubtotalMinorUnits + taxTotalMinorUnits;
        }

        long serviceCharge = 0L;
        if (isServiceChargeEnabled && serviceChargeRateBps > 0)
        {
            checked
            {
                var numerator = grossTotal * serviceChargeRateBps;
                serviceCharge = RoundHalfUp(numerator, BasisPointsDivisor);
            }
        }

        long grandTotal;
        checked
        {
            grandTotal = grossTotal + serviceCharge;
        }

        return (serviceCharge, grandTotal);
    }
}
