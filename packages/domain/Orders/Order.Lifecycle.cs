using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders;

public sealed partial class Order
{
    /// <summary>
    /// Deterministically derives the aggregate Order status from the current statuses of its items.
    /// Prevents premature completion, data loss, and ambiguous states.
    /// </summary>
    public static OrderStatus DeriveStatus(IReadOnlyCollection<OrderItem> items)
    {
        if (items == null || items.Count == 0)
        {
            return OrderStatus.Draft;
        }

        if (items.All(i => i.Status == OrderItemStatus.Draft))
        {
            return OrderStatus.Draft;
        }

        if (items.All(i => i.Status == OrderItemStatus.Cancelled))
        {
            return OrderStatus.Cancelled;
        }

        var activeItems = items.Where(i => i.Status != OrderItemStatus.Cancelled).ToList();
        if (activeItems.Count == 0)
        {
            return OrderStatus.Cancelled;
        }

        if (activeItems.All(i => i.Status == OrderItemStatus.Served))
        {
            return OrderStatus.Served;
        }

        if (activeItems.All(i => i.Status == OrderItemStatus.Ready || i.Status == OrderItemStatus.Served) &&
            activeItems.Any(i => i.Status == OrderItemStatus.Ready))
        {
            return OrderStatus.Ready;
        }

        if (activeItems.Any(i => i.Status == OrderItemStatus.InPreparation) ||
            (activeItems.Any(i => i.Status == OrderItemStatus.Submitted) && activeItems.Any(i => i.Status == OrderItemStatus.Ready || i.Status == OrderItemStatus.Served)))
        {
            return OrderStatus.InPreparation;
        }

        if (activeItems.All(i => i.Status == OrderItemStatus.Submitted))
        {
            return OrderStatus.Submitted;
        }

        return OrderStatus.Submitted;
    }

    /// <summary>
    /// Refreshes the Order aggregate status based on its item collection.
    /// </summary>
    public void RefreshDerivedStatus(DateTimeOffset? nowUtc = null)
    {
        var derived = DeriveStatus(_items);
        if (derived != Status)
        {
            Status = derived;
            UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Transitions all Ready items back to InPreparation (recall ticket).
    /// </summary>
    public void RecallToPreparation(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderStatus.Ready && !_items.Any(i => i.Status == OrderItemStatus.Ready))
        {
            throw new DomainException($"Cannot recall Order to preparation from status '{Status}'. Only Ready orders/items can transition to InPreparation.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        var recalledAny = false;
        foreach (var item in _items)
        {
            if (item.Status == OrderItemStatus.Ready)
            {
                item.RecallToPreparation(timestamp);
                recalledAny = true;
            }
        }

        if (!recalledAny)
        {
            throw new DomainException("No items in Ready status found to recall.");
        }

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Starts preparation on a specific item within the order.
    /// </summary>
    public void StartItemPreparation(OrderItemId itemId, DateTimeOffset? nowUtc = null)
    {
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        item.StartPreparation(timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Marks a specific item within the order as Ready.
    /// </summary>
    public void MarkItemReady(OrderItemId itemId, DateTimeOffset? nowUtc = null)
    {
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        item.MarkReady(timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Recalls a specific item within the order from Ready back to InPreparation.
    /// </summary>
    public void RecallItemToPreparation(OrderItemId itemId, DateTimeOffset? nowUtc = null)
    {
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        item.RecallToPreparation(timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Marks a specific item within the order as Served.
    /// </summary>
    public void MarkItemServed(OrderItemId itemId, DateTimeOffset? nowUtc = null)
    {
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        item.MarkServed(timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Cancels a pre-prep item by an authorized staff member (e.g. Waiter or Cashier).
    /// If item is already InPreparation or Ready, throws a DomainException requiring supervisor void.
    /// </summary>
    public void CancelItemPrePrep(
        OrderItemId itemId,
        Guid staffActorId,
        string staffRole,
        string reason,
        DateTimeOffset? nowUtc = null)
    {
        ValidateActorForCancellation(staffActorId, staffRole);
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;

        item.CancelPrePrep(reason, timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Voids an item by an authorized supervisor (e.g. BranchManager or RestaurantAdmin).
    /// Allowed from Submitted, InPreparation, Ready, and Served.
    /// </summary>
    public void VoidItemBySupervisor(
        OrderItemId itemId,
        Guid supervisorActorId,
        string supervisorRole,
        string reason,
        DateTimeOffset? nowUtc = null)
    {
        ValidateActorForCancellation(supervisorActorId, supervisorRole);
        var item = FindItemOrThrow(itemId);
        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;

        item.VoidBySupervisor(reason, timestamp);

        RefreshDerivedStatus(timestamp);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    /// <summary>
    /// Voids all active items of an order by an authorized supervisor.
    /// </summary>
    public void VoidBySupervisor(
        Guid supervisorActorId,
        string supervisorRole,
        string reason,
        DateTimeOffset? nowUtc = null)
    {
        ValidateActorForCancellation(supervisorActorId, supervisorRole);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancellation reason is required.");
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new DomainException($"Cancellation reason cannot exceed {MaxReasonLength} characters.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException("Order is already cancelled.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        foreach (var item in _items)
        {
            if (item.Status != OrderItemStatus.Cancelled)
            {
                item.VoidBySupervisor(trimmedReason, timestamp);
            }
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = trimmedReason;
        CancelledByActorId = supervisorActorId;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;
    }

    private OrderItem FindItemOrThrow(OrderItemId itemId)
    {
        return _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException($"OrderItem with ID '{itemId.Value}' does not belong to this order.");
    }

    private static void ValidateActorForCancellation(Guid actorId, string actorRole)
    {
        if (actorId == Guid.Empty)
        {
            throw new DomainException("Actor ID is required for cancellation.");
        }

        if (string.IsNullOrWhiteSpace(actorRole) || actorRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Customer cannot directly cancel or void an order item.");
        }
    }
}
