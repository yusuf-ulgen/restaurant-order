using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderLifecycleDomainTests
{
    private static readonly TenantId SampleTenantId = new(Guid.NewGuid());
    private static readonly BranchId SampleBranchId = new(Guid.NewGuid());
    private static readonly DiningSessionId SampleSessionId = new(Guid.NewGuid());
    private static readonly RestaurantTableId SampleTableId = new(Guid.NewGuid());

    [Fact]
    public void OrderItem_Transitions_FollowValidStateMachine()
    {
        var item = CreateTestItem();
        Assert.Equal(OrderItemStatus.Draft, item.Status);

        // Draft -> Submitted
        item.MarkSubmitted();
        Assert.Equal(OrderItemStatus.Submitted, item.Status);

        // Submitted -> InPreparation
        item.StartPreparation();
        Assert.Equal(OrderItemStatus.InPreparation, item.Status);

        // InPreparation -> Ready
        item.MarkReady();
        Assert.Equal(OrderItemStatus.Ready, item.Status);

        // Ready -> InPreparation (Recall)
        item.RecallToPreparation();
        Assert.Equal(OrderItemStatus.InPreparation, item.Status);

        // InPreparation -> Ready
        item.MarkReady();
        Assert.Equal(OrderItemStatus.Ready, item.Status);

        // Ready -> Served
        item.MarkServed();
        Assert.Equal(OrderItemStatus.Served, item.Status);

        // Served -> Cancelled (Supervisor void)
        item.VoidBySupervisor("Misafir yemedi, masadan geri alındı");
        Assert.Equal(OrderItemStatus.Cancelled, item.Status);
    }

    [Fact]
    public void OrderItem_CancelPrePrep_WhenSubmitted_Succeeds()
    {
        var item = CreateTestItem();
        item.MarkSubmitted();

        item.CancelPrePrep("Müşteri vazgeçti");
        Assert.Equal(OrderItemStatus.Cancelled, item.Status);
    }

    [Fact]
    public void OrderItem_CancelPrePrep_WhenInPreparation_ThrowsDomainException()
    {
        var item = CreateTestItem();
        item.MarkSubmitted();
        item.StartPreparation();

        var ex = Assert.Throws<DomainException>(() => item.CancelPrePrep("Garson iptali"));
        Assert.Contains("without supervisor void authority", ex.Message);
    }

    [Fact]
    public void OrderItem_DoubleCancellation_ThrowsDomainException()
    {
        var item = CreateTestItem();
        item.MarkSubmitted();
        item.CancelPrePrep("İlk iptal");

        var ex = Assert.Throws<DomainException>(() => item.CancelPrePrep("İkinci iptal"));
        Assert.Equal("OrderItem is already cancelled.", ex.Message);

        var exVoid = Assert.Throws<DomainException>(() => item.VoidBySupervisor("Supervisor tekrar"));
        Assert.Equal("OrderItem is already cancelled.", exVoid.Message);
    }

    [Fact]
    public void OrderItem_CancellationWithoutReason_ThrowsDomainException()
    {
        var item = CreateTestItem();
        item.MarkSubmitted();

        var ex = Assert.Throws<DomainException>(() => item.CancelPrePrep("   "));
        Assert.Equal("Reason is required to cancel an order item.", ex.Message);

        var exVoid = Assert.Throws<DomainException>(() => item.VoidBySupervisor(""));
        Assert.Equal("Reason is required to void an order item.", exVoid.Message);
    }

    [Fact]
    public void DeriveStatus_AllCancelled_ReturnsCancelled()
    {
        var item1 = CreateTestItem();
        item1.MarkSubmitted();
        item1.CancelPrePrep("İptal 1");

        var item2 = CreateTestItem();
        item2.MarkSubmitted();
        item2.CancelPrePrep("İptal 2");

        var status = Order.DeriveStatus(new[] { item1, item2 });
        Assert.Equal(OrderStatus.Cancelled, status);
    }

    [Fact]
    public void DeriveStatus_MixedCancelledAndServed_ReturnsServed()
    {
        var item1 = CreateTestItem();
        item1.MarkSubmitted();
        item1.StartPreparation();
        item1.MarkReady();
        item1.MarkServed();

        var item2 = CreateTestItem();
        item2.MarkSubmitted();
        item2.CancelPrePrep("İptal");

        var status = Order.DeriveStatus(new[] { item1, item2 });
        Assert.Equal(OrderStatus.Served, status);
    }

    [Fact]
    public void DeriveStatus_MixedReadyAndServed_ReturnsReady()
    {
        var item1 = CreateTestItem();
        item1.MarkSubmitted();
        item1.StartPreparation();
        item1.MarkReady();
        item1.MarkServed();

        var item2 = CreateTestItem();
        item2.MarkSubmitted();
        item2.StartPreparation();
        item2.MarkReady();

        var status = Order.DeriveStatus(new[] { item1, item2 });
        Assert.Equal(OrderStatus.Ready, status);
    }

    [Fact]
    public void DeriveStatus_MixedInPreparationAndReady_ReturnsInPreparation()
    {
        var item1 = CreateTestItem();
        item1.MarkSubmitted();
        item1.StartPreparation();

        var item2 = CreateTestItem();
        item2.MarkSubmitted();
        item2.StartPreparation();
        item2.MarkReady();

        var status = Order.DeriveStatus(new[] { item1, item2 });
        Assert.Equal(OrderStatus.InPreparation, status);
    }

    [Fact]
    public void DeriveStatus_MixedSubmittedAndReady_ReturnsInPreparation()
    {
        var item1 = CreateTestItem();
        item1.MarkSubmitted();

        var item2 = CreateTestItem();
        item2.MarkSubmitted();
        item2.StartPreparation();
        item2.MarkReady();

        var status = Order.DeriveStatus(new[] { item1, item2 });
        Assert.Equal(OrderStatus.InPreparation, status);
    }

    [Fact]
    public void Order_RecallItemToPreparation_UpdatesOrderStatusAndConcurrencyToken()
    {
        var item = CreateTestItem();
        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            1,
            OrderSource.Staff,
            Currency.Default,
            "key-recall-1",
            new[] { item });

        var initialToken = order.ConcurrencyToken;
        order.StartPreparation();
        order.MarkReady();
        Assert.Equal(OrderStatus.Ready, order.Status);

        order.RecallItemToPreparation(item.Id);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(OrderItemStatus.InPreparation, item.Status);
        Assert.NotEqual(initialToken, order.ConcurrencyToken);
    }

    [Fact]
    public void Order_CustomerRole_CannotCancelItemsDirectly()
    {
        var item = CreateTestItem();
        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            1,
            OrderSource.Customer,
            Currency.Default,
            "key-cust-cancel",
            new[] { item });

        var exPrePrep = Assert.Throws<DomainException>(() =>
            order.CancelItemPrePrep(item.Id, Guid.NewGuid(), "Customer", "İptal isteği"));
        Assert.Contains("Customer cannot directly cancel", exPrePrep.Message);

        var exVoid = Assert.Throws<DomainException>(() =>
            order.VoidItemBySupervisor(item.Id, Guid.NewGuid(), "Customer", "İptal isteği"));
        Assert.Contains("Customer cannot directly cancel or void", exVoid.Message);
    }

    private static OrderItem CreateTestItem()
    {
        return OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            MenuItemId.New(),
            null,
            "Test Pizza",
            null,
            quantity: 1,
            unitPriceMinorUnits: 1500L,
            taxRateBasisPoints: 1000);
    }
}
