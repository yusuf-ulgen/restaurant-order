using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed class OrderLifecycleService : IOrderLifecycleService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly RestaurantOrder.Application.Events.IOperationalOutboxWriter _outboxWriter;

    public OrderLifecycleService(
        RestaurantOrderDbContext dbContext,
        RestaurantOrder.Application.Events.IOperationalOutboxWriter? outboxWriter = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _outboxWriter = outboxWriter ?? new RestaurantOrder.Infrastructure.Events.OperationalOutboxWriter(_dbContext);
    }

    public async Task<OrderLifecycleResult> StartPreparationAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            tenantId,
            branchId,
            orderId,
            itemId,
            actorId,
            actorRole,
            concurrencyToken,
            action: "StartPreparation",
            (order, item, now) =>
            {
                if (item != null)
                {
                    order.StartItemPreparation(item.Id, now);
                }
                else
                {
                    order.StartPreparation(now);
                }
            },
            cancellationToken);
    }

    public async Task<OrderLifecycleResult> MarkReadyAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            tenantId,
            branchId,
            orderId,
            itemId,
            actorId,
            actorRole,
            concurrencyToken,
            action: "MarkReady",
            (order, item, now) =>
            {
                if (item != null)
                {
                    order.MarkItemReady(item.Id, now);
                }
                else
                {
                    order.MarkReady(now);
                }
            },
            cancellationToken);
    }

    public async Task<OrderLifecycleResult> RecallToPreparationAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            tenantId,
            branchId,
            orderId,
            itemId,
            actorId,
            actorRole,
            concurrencyToken,
            action: "RecallToPreparation",
            (order, item, now) =>
            {
                if (item != null)
                {
                    order.RecallItemToPreparation(item.Id, now);
                }
                else
                {
                    order.RecallToPreparation(now);
                }
            },
            cancellationToken);
    }

    public async Task<OrderLifecycleResult> MarkServedAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            tenantId,
            branchId,
            orderId,
            itemId,
            actorId,
            actorRole,
            concurrencyToken,
            action: "MarkServed",
            (order, item, now) =>
            {
                if (item != null)
                {
                    order.MarkItemServed(item.Id, now);
                }
                else
                {
                    order.MarkServed(now);
                }
            },
            cancellationToken);
    }

    private async Task<OrderLifecycleResult> ExecuteTransitionAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        string action,
        Action<Order, OrderItem?, DateTimeOffset> transitionAction,
        CancellationToken cancellationToken)
    {
        // Begin tenant transaction if no ambient transaction exists
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: cancellationToken);
        }

        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Modifiers)
                .FirstOrDefaultAsync(
                    o => o.TenantId == tenantId &&
                         o.BranchId == branchId &&
                         o.Id == orderId,
                    cancellationToken);

            if (order == null)
            {
                throw new ResourceNotFoundException("Order not found.");
            }

            if (order.ConcurrencyToken != concurrencyToken)
            {
                throw new ConcurrencyConflictException("Order has been modified by another process. Concurrency conflict detected.");
            }

            OrderItem? targetItem = null;
            if (itemId != null)
            {
                targetItem = order.Items.FirstOrDefault(i => i.Id == itemId)
                    ?? throw new ResourceNotFoundException("Order item not found.");
            }

            var previousStatus = order.Status;
            var nowUtc = DateTimeOffset.UtcNow;

            transitionAction(order, targetItem, nowUtc);

            var auditDetails = new
            {
                action,
                targetItemId = targetItem?.Id.Value,
                targetItemName = targetItem?.ItemName,
                previousStatus = previousStatus.ToString(),
                newStatus = order.Status.ToString(),
                orderTotalMinorUnits = order.TotalMinorUnits
            };

            var audit = OrderAuditEntry.Record(
                tenantId,
                order.Id,
                order.DiningSessionId,
                actorId,
                actorRole,
                action,
                previousStatus,
                order.Status,
                reason: $"Lifecycle transition {action}",
                detailsJson: JsonSerializer.Serialize(auditDetails),
                nowUtc: nowUtc);

            _dbContext.OrderAuditEntries.Add(audit);

            // Enqueue real-time item status change events with genuine item and domain statuses
            if (targetItem != null)
            {
                _outboxWriter.EnqueueEvent(
                    tenantId: tenantId.Value,
                    branchId: branchId.Value,
                    aggregateId: order.Id.Value.ToString(),
                    eventType: RestaurantOrder.Application.Events.RealtimeEventTypes.OrderItemStatusChanged,
                    data: new RestaurantOrder.Application.Events.OrderItemStatusChangedEventData(
                        OrderId: order.Id.Value,
                        DiningSessionId: order.DiningSessionId.Value,
                        ItemId: targetItem.Id.Value,
                        NewStatus: targetItem.Status.ToString(),
                        ChangedAtUtc: nowUtc
                    ),
                    idempotencyKey: $"order-transition-{order.Id.Value}-{targetItem.Id.Value}-{action}-{order.ConcurrencyToken}",
                    occurredAtUtc: nowUtc);
            }
            else
            {
                foreach (var item in order.Items)
                {
                    _outboxWriter.EnqueueEvent(
                        tenantId: tenantId.Value,
                        branchId: branchId.Value,
                        aggregateId: order.Id.Value.ToString(),
                        eventType: RestaurantOrder.Application.Events.RealtimeEventTypes.OrderItemStatusChanged,
                        data: new RestaurantOrder.Application.Events.OrderItemStatusChangedEventData(
                            OrderId: order.Id.Value,
                            DiningSessionId: order.DiningSessionId.Value,
                            ItemId: item.Id.Value,
                            NewStatus: item.Status.ToString(),
                            ChangedAtUtc: nowUtc
                        ),
                        idempotencyKey: $"order-transition-{order.Id.Value}-{item.Id.Value}-{action}-{order.ConcurrencyToken}",
                        occurredAtUtc: nowUtc);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (localTx != null)
            {
                await localTx.CommitAsync(cancellationToken);
            }

            var dto = MapToDto(order);
            return new OrderLifecycleResult(dto, action, nowUtc);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    private static StaffOrderResponseDto MapToDto(Order order)
    {
        var items = order.Items.Select(i => new StaffOrderItemDto(
            i.Id.Value,
            i.MenuItemId.Value,
            i.ItemVariantId?.Value,
            i.ItemName,
            i.VariantName,
            i.Quantity,
            i.CustomerNote,
            i.PreparationStationId,
            i.UnitPriceMinorUnits,
            i.ModifierTotalMinorUnits,
            i.LineTotalMinorUnits,
            i.TaxRateBasisPoints,
            i.Status.ToString(),
            i.CreatedAtUtc,
            i.UpdatedAtUtc,
            i.Modifiers.Select(m => new StaffOrderItemModifierDto(
                m.Id,
                m.ModifierGroupId,
                m.ModifierGroupName,
                m.ModifierOptionId,
                m.ModifierOptionName,
                m.PriceDeltaMinorUnits)).ToList())).ToList();

        return new StaffOrderResponseDto(
            order.Id.Value,
            order.TenantId.Value,
            order.BranchId.Value,
            order.DiningSessionId.Value,
            order.TableId.Value,
            order.RoundNumber,
            order.OrderSource.ToString(),
            order.Status.ToString(),
            order.Currency.Code,
            order.SubtotalMinorUnits,
            order.TaxMinorUnits,
            order.ServiceChargeMinorUnits,
            order.TotalMinorUnits,
            order.IdempotencyKey,
            order.ConcurrencyToken,
            order.CreatedAtUtc,
            order.SubmittedAtUtc,
            order.UpdatedAtUtc,
            order.CancellationReason,
            order.CancelledByActorId,
            items);
    }
}
