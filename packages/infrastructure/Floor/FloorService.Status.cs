using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Floor;

public partial class FloorService
{
    public async Task<BranchFloorStatusDto> GetBranchFloorStatusAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureFloorStatusViewPermission(actor);

        var tables = await _dbContext.RestaurantTables
            .Where(t => t.TenantId == tenantId && t.BranchId == branchId)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        var activeSessions = await _dbContext.DiningSessions
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId && s.Status != DiningSessionStatus.Closed)
            .ToListAsync(ct);

        var sessionsByTable = activeSessions.ToDictionary(s => s.TableId, s => s);
        var activeSessionIds = activeSessions.Select(s => s.Id).ToList();

        // Query active service requests count grouped by table
        var activeRequestsByTable = await _dbContext.ServiceRequests
            .Where(sr => sr.TenantId == tenantId && sr.BranchId == branchId &&
                         (sr.Status == ServiceRequestStatus.Open || sr.Status == ServiceRequestStatus.Acknowledged))
            .GroupBy(sr => sr.TableId)
            .Select(g => new { TableId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TableId, x => x.Count, ct);

        // Query active order counts and ready items count grouped by table
        Dictionary<RestaurantTableId, (int ReadyItems, int ActiveOrders)> orderStatsByTable = new();
        if (activeSessionIds.Count > 0)
        {
            var activeOrders = await _dbContext.Orders
                .Where(o => o.TenantId == tenantId && o.BranchId == branchId &&
                            activeSessionIds.Contains(o.DiningSessionId) &&
                            o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Draft)
                .Select(o => new
                {
                    o.TableId,
                    o.Status,
                    ReadyCount = o.Items.Count(i => i.Status == OrderItemStatus.Ready)
                })
                .ToListAsync(ct);

            orderStatsByTable = activeOrders
                .GroupBy(o => o.TableId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        ReadyItems: g.Sum(x => x.ReadyCount),
                        ActiveOrders: g.Count(x => x.Status == OrderStatus.Submitted || x.Status == OrderStatus.InPreparation)
                    ));
        }

        var tableStatusDtos = tables.Select(t =>
        {
            var hasSession = sessionsByTable.TryGetValue(t.Id, out var session);
            activeRequestsByTable.TryGetValue(t.Id, out var activeRequestsCount);
            orderStatsByTable.TryGetValue(t.Id, out var orderStats);

            var derivedStatus = DeriveTableStatus(t.IsActive, session?.Status, orderStats.ActiveOrders, orderStats.ReadyItems);

            return new TableFloorStatusDto(
                MapTable(t),
                hasSession ? MapSession(session!) : null,
                activeRequestsCount,
                orderStats.ReadyItems,
                orderStats.ActiveOrders,
                derivedStatus);
        }).ToList();

        return new BranchFloorStatusDto(branchId.Value, tableStatusDtos);
    }

    internal static string DeriveTableStatus(
        bool isTableActive,
        DiningSessionStatus? sessionStatus,
        int activeOrdersCount,
        int readyItemsCount)
    {
        if (!isTableActive)
        {
            return "Inactive";
        }

        if (!sessionStatus.HasValue)
        {
            return "Available";
        }

        if (sessionStatus.Value == DiningSessionStatus.BillRequested)
        {
            return "BillRequested";
        }

        if (readyItemsCount > 0)
        {
            return "ReadyToServe";
        }

        if (activeOrdersCount > 0)
        {
            return "OrderPending";
        }

        return "Seated";
    }
}
