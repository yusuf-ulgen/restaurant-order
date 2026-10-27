using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed partial class StaffOrderService
{
    public async Task<IReadOnlyList<ReadyOrderItemDto>> ListReadyItemsAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId? sessionFilter,
        RestaurantTableId? tableFilter,
        int limit,
        int offset,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

        var safeLimit = Math.Clamp(limit, 1, 100);
        var safeOffset = Math.Max(0, offset);

        var query = from order in _dbContext.Orders.AsNoTracking()
                    where order.TenantId == tenantId &&
                          order.BranchId == branchId &&
                          order.Status != OrderStatus.Cancelled
                    from item in order.Items
                    where item.Status == OrderItemStatus.Ready
                    join table in _dbContext.RestaurantTables.AsNoTracking()
                        on new { order.TenantId, order.BranchId, order.TableId }
                        equals new { table.TenantId, table.BranchId, TableId = table.Id }
                    select new
                    {
                        OrderId = order.Id.Value,
                        ItemId = item.Id.Value,
                        DiningSessionId = order.DiningSessionId.Value,
                        TableId = order.TableId.Value,
                        table.TableNumber,
                        order.RoundNumber,
                        item.ItemName,
                        item.VariantName,
                        item.Quantity,
                        Status = item.Status.ToString(),
                        item.CreatedAtUtc,
                        item.UpdatedAtUtc
                    };

        if (sessionFilter != null)
        {
            var sid = sessionFilter.Value;
            query = query.Where(x => x.DiningSessionId == sid);
        }

        if (tableFilter != null)
        {
            var tid = tableFilter.Value;
            query = query.Where(x => x.TableId == tid);
        }

        var results = await query
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.ItemId)
            .Skip(safeOffset)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return results.Select(r => new ReadyOrderItemDto(
            r.OrderId,
            r.ItemId,
            r.DiningSessionId,
            r.TableId,
            r.TableNumber,
            r.RoundNumber,
            r.ItemName,
            r.VariantName,
            r.Quantity,
            r.Status,
            r.CreatedAtUtc,
            r.UpdatedAtUtc
        )).ToList();
    }
}
