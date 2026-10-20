using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public sealed class OrderPartialPreparationRegressionTests
{
    [Fact]
    public void MarkReady_WithUnstartedItems_DoesNotCompleteTheOrder()
    {
        var order = CreateOrder();
        var items = order.Items.ToArray();
        order.StartItemPreparation(items[0].Id);

        order.MarkReady();

        Assert.Equal(OrderItemStatus.Ready, items[0].Status);
        Assert.Equal(OrderItemStatus.Submitted, items[1].Status);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(Order.DeriveStatus(order.Items), order.Status);
        Assert.Throws<DomainException>(() => order.MarkServed());
    }

    [Fact]
    public void MarkReady_WithoutPreparingItems_RejectsWithoutMutation()
    {
        var order = CreateOrder();
        var first = order.Items.First();
        order.StartItemPreparation(first.Id);
        order.MarkItemReady(first.Id);
        var token = order.ConcurrencyToken;
        var updated = order.UpdatedAtUtc;

        Assert.Throws<DomainException>(() => order.MarkReady());

        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(token, order.ConcurrencyToken);
        Assert.Equal(updated, order.UpdatedAtUtc);
    }

    [Fact]
    public void MarkReady_AfterRemainingPreparation_CompletesOnlyWhenAllItemsAreReady()
    {
        var order = CreateOrder();
        var items = order.Items.ToArray();
        order.StartItemPreparation(items[0].Id);
        order.MarkReady();
        order.StartItemPreparation(items[1].Id);

        order.MarkReady();

        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.All(order.Items, item => Assert.Equal(OrderItemStatus.Ready, item.Status));
        order.MarkServed();
        Assert.Equal(OrderStatus.Served, order.Status);
        Assert.All(order.Items, item => Assert.Equal(OrderItemStatus.Served, item.Status));
    }

    private static Order CreateOrder()
    {
        var tenant = TenantId.New();
        var items = Enumerable.Range(0, 2).Select(_ => OrderItem.Create(
            OrderId.New(), tenant, MenuItemId.New(), null, "Sentetik ürün", null, 1, 1500, 0)).ToArray();
        return Order.CreateSubmitted(tenant, BranchId.New(), DiningSessionId.New(),
            RestaurantTableId.New(), 1, OrderSource.Customer, Currency.Default, "partial-preparation", items);
    }
}
