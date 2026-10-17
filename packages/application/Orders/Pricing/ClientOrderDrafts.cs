using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Application.Orders.Pricing;

/// <summary>
/// Client-submitted modifier option selection draft containing only identifiers.
/// Prices and names submitted by the client are strictly ignored for security.
/// </summary>
public sealed record ClientSelectedModifierDraft(
    Guid ModifierGroupId,
    Guid ModifierOptionId);

/// <summary>
/// Client-submitted line item draft containing only identifiers, quantity, note,
/// and client's expected unit price for price modification race detection.
/// </summary>
public sealed record ClientOrderItemDraft(
    MenuItemId MenuItemId,
    ItemVariantId? ItemVariantId,
    int Quantity,
    string? CustomerNote,
    IReadOnlyList<ClientSelectedModifierDraft>? SelectedModifiers,
    long? ClientExpectedUnitPriceMinorUnits);
