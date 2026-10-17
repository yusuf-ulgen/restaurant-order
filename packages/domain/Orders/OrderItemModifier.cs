using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Immutable snapshot of a selected modifier option for an order item.
/// Captures the modifier group, selected option name, and price delta at order creation time.
/// </summary>
public sealed class OrderItemModifier
{
    public const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public OrderItemId OrderItemId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public string ModifierGroupName { get; private set; } = string.Empty;
    public Guid ModifierOptionId { get; private set; }
    public string ModifierOptionName { get; private set; } = string.Empty;
    public long PriceDeltaMinorUnits { get; private set; }

    // Parameterless constructor for EF Core materialization
    private OrderItemModifier()
    {
    }

    public static OrderItemModifier Create(
        OrderItemId orderItemId,
        Guid modifierGroupId,
        string modifierGroupName,
        Guid modifierOptionId,
        string modifierOptionName,
        long priceDeltaMinorUnits)
    {
        if (modifierGroupId == Guid.Empty)
        {
            throw new DomainException("ModifierGroupId cannot be empty.");
        }

        if (modifierOptionId == Guid.Empty)
        {
            throw new DomainException("ModifierOptionId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(modifierGroupName))
        {
            throw new DomainException("Modifier group name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(modifierOptionName))
        {
            throw new DomainException("Modifier option name cannot be empty.");
        }

        var trimmedGroupName = modifierGroupName.Trim();
        if (trimmedGroupName.Length > MaxNameLength)
        {
            throw new DomainException($"Modifier group name cannot exceed {MaxNameLength} characters.");
        }

        var trimmedOptionName = modifierOptionName.Trim();
        if (trimmedOptionName.Length > MaxNameLength)
        {
            throw new DomainException($"Modifier option name cannot exceed {MaxNameLength} characters.");
        }

        if (priceDeltaMinorUnits < 0)
        {
            throw new DomainException("Price delta minor units cannot be negative.");
        }

        return new OrderItemModifier
        {
            Id = Guid.NewGuid(),
            OrderItemId = orderItemId,
            ModifierGroupId = modifierGroupId,
            ModifierGroupName = trimmedGroupName,
            ModifierOptionId = modifierOptionId,
            ModifierOptionName = trimmedOptionName,
            PriceDeltaMinorUnits = priceDeltaMinorUnits
        };
    }
}
