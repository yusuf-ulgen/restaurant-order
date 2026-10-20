using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed partial class StaffOrderService
{
    public async Task<StaffOrderResponseDto> CancelOrderItemAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        OrderItemId itemId,
        CancelOrderItemRequest request,
        Guid? ifMatchConcurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainException("Cancellation reason is required.");
        }

        var effectiveToken = ifMatchConcurrencyToken ?? request.ConcurrencyToken;
        if (!effectiveToken.HasValue || effectiveToken.Value == Guid.Empty)
        {
            throw new ConcurrencyPreconditionException("If-Match header or concurrency token is required.");
        }

        // Begin tenant transaction if no ambient transaction exists
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: cancellationToken);
        }

        try
        {
            if (_dbContext.Database.IsNpgsql())
            {
                var lockKey = $"order_session_{sessionId.Value:D}";
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
                    cancellationToken);
            }

        var order = await _dbContext.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(
                o => o.TenantId == tenantId &&
                     o.BranchId == branchId &&
                     o.DiningSessionId == sessionId &&
                     o.Id == orderId,
                cancellationToken);

        if (order == null)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        if (order.ConcurrencyToken != effectiveToken.Value)
        {
            throw new ConcurrencyConflictException("The order has been modified by another process. Concurrency conflict detected.");
        }

        var item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new ResourceNotFoundException("Order item not found.");

        if (item.Status == OrderItemStatus.Cancelled)
        {
            throw new DomainException("OrderItem is already cancelled.");
        }

        var previousItemStatus = item.Status;
        var previousOrderStatus = order.Status;
        var nowUtc = DateTimeOffset.UtcNow;
        bool isSupervisorVoid = false;
        Guid auditActorId = actor.SubjectId;
        string auditActorRole = actor.Role.ToString();

        if (item.Status == OrderItemStatus.Submitted)
        {
            if (!_permissionRegistry.IsPermitted(actor, Permissions.OrdersItemsCancelPrePrep))
            {
                throw new InvalidAuthorizationScopeException("Missing required permission 'orders.items.cancel_pre_prep'.");
            }

            order.CancelItemPrePrep(itemId, actor.SubjectId, actor.Role.ToString(), request.Reason.Trim(), nowUtc);
        }
        else if (item.Status is OrderItemStatus.InPreparation or OrderItemStatus.Ready or OrderItemStatus.Served)
        {
            // Supervisor void required
            if (_permissionRegistry.IsPermitted(actor, Permissions.OrdersItemsVoidInPrep))
            {
                isSupervisorVoid = true;
                auditActorId = actor.SubjectId;
                auditActorRole = actor.Role.ToString();
            }
            else
            {
                if (!request.SupervisorUserId.HasValue || string.IsNullOrWhiteSpace(request.SupervisorPin))
                {
                    throw new InvalidAuthorizationScopeException("Supervisor authorization is required to void items in preparation.");
                }

                // Verify supervisor PIN authorization
                var (verifiedSupervisorId, verifiedRole) = await _supervisorPinService.VerifySupervisorPinAsync(
                    tenantId,
                    branchId,
                    request.SupervisorUserId,
                    request.SupervisorPin,
                    cancellationToken);

                isSupervisorVoid = true;
                auditActorId = verifiedSupervisorId;
                auditActorRole = verifiedRole;
            }

            order.VoidItemBySupervisor(itemId, auditActorId, auditActorRole, request.Reason.Trim(), nowUtc);
        }
        else
        {
            throw new DomainException($"Cannot cancel OrderItem in status '{item.Status}'.");
        }

        // Record immutable audit entry with snapshot minor units (WITHOUT customer notes)
        var auditDetails = new
        {
            itemId = item.Id.Value,
            itemName = item.ItemName,
            variantName = item.VariantName,
            quantity = item.Quantity,
            lineTotalMinorUnits = item.LineTotalMinorUnits,
            previousItemStatus = previousItemStatus.ToString(),
            newItemStatus = item.Status.ToString(),
            isSupervisorVoid,
            orderTotalMinorUnits = order.TotalMinorUnits,
            currency = order.Currency.Code
        };

        var audit = OrderAuditEntry.Record(
            tenantId,
            order.Id,
            sessionId,
            auditActorId,
            auditActorRole,
            action: isSupervisorVoid ? "WASTE_CANCEL" : "ItemCancelled",
            previousStatus: previousOrderStatus,
            newStatus: order.Status,
            reason: request.Reason.Trim(),
            detailsJson: JsonSerializer.Serialize(auditDetails),
            nowUtc: nowUtc);

        _dbContext.OrderAuditEntries.Add(audit);

        // Enqueue real-time item status change event
        _outboxWriter.EnqueueEvent(
            tenantId: tenantId.Value,
            branchId: branchId.Value,
            aggregateId: order.Id.Value.ToString(),
            eventType: RestaurantOrder.Application.Events.RealtimeEventTypes.OrderItemStatusChanged,
            data: new RestaurantOrder.Application.Events.OrderItemStatusChangedEventData(
                OrderId: order.Id.Value,
                ItemId: item.Id.Value,
                NewStatus: "Cancelled",
                ChangedAtUtc: nowUtc
            ),
            idempotencyKey: $"item-cancelled-{order.Id.Value}-{item.Id.Value}-{order.ConcurrencyToken}",
            occurredAtUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (localTx != null)
        {
            await localTx.CommitAsync(cancellationToken);
        }

        return MapToDto(order);
    }
    finally
    {
        if (localTx != null)
        {
            await localTx.DisposeAsync();
        }
    }
}
}
