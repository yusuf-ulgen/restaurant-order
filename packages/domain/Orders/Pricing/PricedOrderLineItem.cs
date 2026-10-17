using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Domain.Orders.Pricing;

/// <summary>
/// Represents the authoritative calculated financial and snapshot details for an individual line item.
/// </summary>
public sealed class PricedOrderLineItem
{
    public MenuItemId MenuItemId { get; }
    public ItemVariantId? ItemVariantId { get; }
    public string ItemName { get; }
    public string? VariantName { get; }
    public int Quantity { get; }
    public long UnitPriceMinorUnits { get; }
    public long ModifierTotalMinorUnits { get; }
    public long LineTotalMinorUnits { get; }
    public long NetSubtotalMinorUnits { get; }
    public long TaxMinorUnits { get; }
    public int TaxRateBasisPoints { get; }
    public Guid? PreparationStationId { get; }
    public string? CustomerNote { get; }
    public IReadOnlyList<OrderItemModifier> Modifiers { get; }

    public PricedOrderLineItem(
        MenuItemId menuItemId,
        ItemVariantId? itemVariantId,
        string itemName,
        string? variantName,
        int quantity,
        long unitPriceMinorUnits,
        long modifierTotalMinorUnits,
        long lineTotalMinorUnits,
        long netSubtotalMinorUnits,
        long taxMinorUnits,
        int taxRateBasisPoints,
        Guid? preparationStationId,
        string? customerNote,
        IReadOnlyList<OrderItemModifier> modifiers)
    {
        MenuItemId = menuItemId;
        ItemVariantId = itemVariantId;
        ItemName = itemName;
        VariantName = variantName;
        Quantity = quantity;
        UnitPriceMinorUnits = unitPriceMinorUnits;
        ModifierTotalMinorUnits = modifierTotalMinorUnits;
        LineTotalMinorUnits = lineTotalMinorUnits;
        NetSubtotalMinorUnits = netSubtotalMinorUnits;
        TaxMinorUnits = taxMinorUnits;
        TaxRateBasisPoints = taxRateBasisPoints;
        PreparationStationId = preparationStationId;
        CustomerNote = customerNote;
        Modifiers = modifiers;
    }
}
