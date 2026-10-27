using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorMovementOrdersAndCallsUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningSessionId _session1 = DiningSessionId.New();
    private readonly DiningSessionId _session2 = DiningSessionId.New();
    private readonly RestaurantTableId _table1 = RestaurantTableId.New();
    private readonly RestaurantTableId _table2 = RestaurantTableId.New();

    private Order CreateSampleOrder(DiningSessionId session, RestaurantTableId table, int round)
    {
        var orderId = OrderId.New();
        var item = OrderItem.Create(
            orderId,
            _tenantId,
            new MenuItemId(Guid.NewGuid()),
            null,
            "Pide",
            null,
            1,
            15000L,
            1000);

        return Order.CreateSubmitted(
            _tenantId,
            _branchId,
            session,
            table,
            round,
            OrderSource.Staff,
            Currency.Default,
            $"idem-{Guid.NewGuid():N}",
            new[] { item });
    }

    [Fact]
    public void Order_UpdateOperationalTable_UpdatesTableAndConcurrencyToken()
    {
        var order = CreateSampleOrder(_session1, _table1, 1);
        var oldToken = order.ConcurrencyToken;
        var now = DateTimeOffset.UtcNow;

        order.UpdateOperationalTable(_table2, now);

        Assert.Equal(_table2, order.TableId);
        Assert.NotEqual(oldToken, order.ConcurrencyToken);
        Assert.Equal(now, order.UpdatedAtUtc);
    }

    [Fact]
    public void Order_ReassignSessionOnMerge_UpdatesSessionTableAndRoundNumber()
    {
        var order = CreateSampleOrder(_session1, _table1, 1);
        var oldToken = order.ConcurrencyToken;
        var now = DateTimeOffset.UtcNow;

        // Reassign to target session with round number 3
        order.ReassignSessionOnMerge(_session2, _table2, 3, now);

        Assert.Equal(_session2, order.DiningSessionId);
        Assert.Equal(_table2, order.TableId);
        Assert.Equal(3, order.RoundNumber);
        Assert.NotEqual(oldToken, order.ConcurrencyToken);
        Assert.Equal(now, order.UpdatedAtUtc);
    }

    [Fact]
    public void Order_ReassignSessionOnMerge_ThrowsOnInvalidArguments()
    {
        var order = CreateSampleOrder(_session1, _table1, 1);

        Assert.Throws<DomainException>(() => order.ReassignSessionOnMerge(new DiningSessionId(Guid.Empty), _table2, 2));
        Assert.Throws<DomainException>(() => order.ReassignSessionOnMerge(_session2, new RestaurantTableId(Guid.Empty), 2));
        Assert.Throws<DomainException>(() => order.ReassignSessionOnMerge(_session2, _table2, 0));
        Assert.Throws<DomainException>(() => order.ReassignSessionOnMerge(_session2, _table2, -1));
    }

    [Fact]
    public void ServiceRequest_UpdateOperationalTable_And_ReassignOnMerge_UpdatesFieldsCorrectly()
    {
        var request = ServiceRequest.Create(
            _tenantId,
            _branchId,
            _session1,
            _table1,
            ServiceRequestType.Water,
            "Su lütfen",
            "idem-sr-1");

        var now = DateTimeOffset.UtcNow;
        request.UpdateOperationalTable(_table2, now);

        Assert.Equal(_table2, request.TableId);
        Assert.Equal(now, request.UpdatedAtUtc);

        var now2 = DateTimeOffset.UtcNow.AddMinutes(1);
        request.ReassignSessionOnMerge(_session2, _table2, now2);

        Assert.Equal(_session2, request.DiningSessionId);
        Assert.Equal(_table2, request.TableId);
        Assert.Equal(now2, request.UpdatedAtUtc);
    }
}
