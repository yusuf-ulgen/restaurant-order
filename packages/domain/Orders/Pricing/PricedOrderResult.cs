using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders.Pricing;

/// <summary>
/// Authoritative calculation result containing order-level financial snapshots,
/// line items, and audit totals.
/// </summary>
public sealed class PricedOrderResult
{
    public long SubtotalMinorUnits { get; }
    public long TaxMinorUnits { get; }
    public long ServiceChargeMinorUnits { get; }
    public long TotalMinorUnits { get; }
    public Currency Currency { get; }
    public bool PricesIncludeTax { get; }
    public int DefaultTaxRateBps { get; }
    public int ServiceChargeRateBps { get; }
    public string PricingPolicyFingerprint { get; }
    public IReadOnlyList<PricedOrderLineItem> LineItems { get; }

    public PricedOrderResult(
        long subtotalMinorUnits,
        long taxMinorUnits,
        long serviceChargeMinorUnits,
        long totalMinorUnits,
        Currency currency,
        bool pricesIncludeTax,
        int defaultTaxRateBps,
        int serviceChargeRateBps,
        IReadOnlyList<PricedOrderLineItem> lineItems,
        string pricingPolicyFingerprint = "")
    {
        SubtotalMinorUnits = subtotalMinorUnits;
        TaxMinorUnits = taxMinorUnits;
        ServiceChargeMinorUnits = serviceChargeMinorUnits;
        TotalMinorUnits = totalMinorUnits;
        Currency = currency;
        PricesIncludeTax = pricesIncludeTax;
        DefaultTaxRateBps = defaultTaxRateBps;
        ServiceChargeRateBps = serviceChargeRateBps;
        LineItems = lineItems;
        PricingPolicyFingerprint = pricingPolicyFingerprint;
    }
}
