using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Represents an immutable snapshot of an ordered line item, including its unit price,
/// selected modifiers, tax snapshot, and preparation status.
/// </summary>
public sealed class OrderItem
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 99;
    public const int MaxNameLength = 150;
    public const int MaxVariantNameLength = 100;
    public const int MaxCustomerNoteLength = 500;
    public const int MaxTaxRateBasisPoints = 10000; // 100.00%

    private readonly List<OrderItemModifier> _modifiers = new();

    public OrderItemId Id { get; private set; }
    public OrderId OrderId { get; private set; }
    public TenantId TenantId { get; private set; }
    public MenuItemId MenuItemId { get; private set; }
    public ItemVariantId? ItemVariantId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string? VariantName { get; private set; }
    public int Quantity { get; private set; }
    public string? CustomerNote { get; private set; }
    public Guid? PreparationStationId { get; private set; }
    public long UnitPriceMinorUnits { get; private set; }
    public long ModifierTotalMinorUnits { get; private set; }
    public long LineTotalMinorUnits { get; private set; }
    public int TaxRateBasisPoints { get; private set; }
    public OrderItemStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<OrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    // Parameterless constructor for EF Core materialization
    private OrderItem()
    {
    }

    public static OrderItem Create(
        OrderId orderId,
        TenantId tenantId,
        MenuItemId menuItemId,
        ItemVariantId? itemVariantId,
        string itemName,
        string? variantName,
        int quantity,
        long unitPriceMinorUnits,
        int taxRateBasisPoints,
        Guid? preparationStationId = null,
        string? customerNote = null,
        IEnumerable<OrderItemModifier>? modifiers = null,
        DateTimeOffset? nowUtc = null)
    {
        if (menuItemId.Value == Guid.Empty)
        {
            throw new DomainException("MenuItemId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new DomainException("Item name cannot be empty.");
        }

        var trimmedItemName = itemName.Trim();
        if (trimmedItemName.Length > MaxNameLength)
        {
            throw new DomainException($"Item name cannot exceed {MaxNameLength} characters.");
        }

        var trimmedVariantName = string.IsNullOrWhiteSpace(variantName) ? null : variantName.Trim();
        if (trimmedVariantName != null && trimmedVariantName.Length > MaxVariantNameLength)
        {
            throw new DomainException($"Variant name cannot exceed {MaxVariantNameLength} characters.");
        }

        if (quantity < MinQuantity || quantity > MaxQuantity)
        {
            throw new DomainException($"Quantity must be between {MinQuantity} and {MaxQuantity}. Received: {quantity}.");
        }

        if (unitPriceMinorUnits < 0)
        {
            throw new DomainException("Unit price minor units cannot be negative.");
        }

        if (taxRateBasisPoints < 0 || taxRateBasisPoints > MaxTaxRateBasisPoints)
        {
            throw new DomainException($"Tax rate basis points must be between 0 and {MaxTaxRateBasisPoints}. Received: {taxRateBasisPoints}.");
        }

        var sanitizedNote = ValidateAndSanitizeCustomerNote(customerNote);
        var itemId = OrderItemId.New();
        var creationTime = nowUtc ?? DateTimeOffset.UtcNow;

        long modifierSum = 0;
        var modifierList = new List<OrderItemModifier>();
        if (modifiers != null)
        {
            foreach (var mod in modifiers)
            {
                checked
                {
                    modifierSum += mod.PriceDeltaMinorUnits;
                }
                modifierList.Add(mod);
            }
        }

        long lineTotal;
        checked
        {
            var singleItemTotal = unitPriceMinorUnits + modifierSum;
            lineTotal = singleItemTotal * quantity;
        }

        var item = new OrderItem
        {
            Id = itemId,
            OrderId = orderId,
            TenantId = tenantId,
            MenuItemId = menuItemId,
            ItemVariantId = itemVariantId,
            ItemName = trimmedItemName,
            VariantName = trimmedVariantName,
            Quantity = quantity,
            CustomerNote = sanitizedNote,
            PreparationStationId = preparationStationId,
            UnitPriceMinorUnits = unitPriceMinorUnits,
            ModifierTotalMinorUnits = modifierSum,
            LineTotalMinorUnits = lineTotal,
            TaxRateBasisPoints = taxRateBasisPoints,
            Status = OrderItemStatus.Draft,
            CreatedAtUtc = creationTime,
            UpdatedAtUtc = null
        };

        item._modifiers.AddRange(modifierList);
        return item;
    }

    public void MarkSubmitted(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderItemStatus.Draft)
        {
            throw new DomainException($"Cannot submit OrderItem in status '{Status}'. Only Draft items can be submitted.");
        }

        Status = OrderItemStatus.Submitted;
        UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public void StartPreparation(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderItemStatus.Submitted)
        {
            throw new DomainException($"Cannot start preparation for OrderItem in status '{Status}'. Only Submitted items can transition to InPreparation.");
        }

        Status = OrderItemStatus.InPreparation;
        UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public void MarkReady(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderItemStatus.InPreparation)
        {
            throw new DomainException($"Cannot mark OrderItem as Ready from status '{Status}'. Only InPreparation items can transition to Ready.");
        }

        Status = OrderItemStatus.Ready;
        UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public void MarkServed(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderItemStatus.Ready)
        {
            throw new DomainException($"Cannot mark OrderItem as Served from status '{Status}'. Only Ready items can transition to Served.");
        }

        Status = OrderItemStatus.Served;
        UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public void CancelByStaff(string reason, DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Reason is required to cancel an order item.");
        }

        if (Status == OrderItemStatus.Served)
        {
            throw new DomainException("Cannot cancel an OrderItem that has already been served.");
        }

        if (Status == OrderItemStatus.Cancelled)
        {
            throw new DomainException("OrderItem is already cancelled.");
        }

        Status = OrderItemStatus.Cancelled;
        UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public static string? ValidateAndSanitizeCustomerNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        if (trimmed.Length > MaxCustomerNoteLength)
        {
            throw new DomainException($"Customer note cannot exceed {MaxCustomerNoteLength} characters.");
        }

        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new DomainException("Customer note cannot contain HTML tags or markup.");
        }

        foreach (var c in trimmed)
        {
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                throw new DomainException("Customer note cannot contain control characters.");
            }
        }

        return trimmed;
    }
}
