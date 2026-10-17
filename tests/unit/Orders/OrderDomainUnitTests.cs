using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderDomainUnitTests
{
    private static readonly TenantId SampleTenantId = new(Guid.NewGuid());
    private static readonly BranchId SampleBranchId = new(Guid.NewGuid());
    private static readonly DiningSessionId SampleSessionId = new(Guid.NewGuid());
    private static readonly RestaurantTableId SampleTableId = new(Guid.NewGuid());
    private static readonly MenuItemId SampleMenuItemId = new(Guid.NewGuid());

    [Fact]
    public void CreateSubmitted_ValidInputs_CalculatesTotalsAndSnapshotsCorrectly()
    {
        // Arrange
        var modifier = OrderItemModifier.Create(
            OrderItemId.New(),
            Guid.NewGuid(),
            "Ekstra",
            Guid.NewGuid(),
            "Peynir",
            500L); // 5.00 TRY

        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Cheeseburger",
            null,
            quantity: 2,
            unitPriceMinorUnits: 2500L, // 25.00 TRY
            taxRateBasisPoints: 1000,    // 10.00%
            modifiers: new[] { modifier });

        // Act
        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            "idempotency-key-001",
            new[] { item },
            serviceChargeMinorUnits: 200L);

        // Assert
        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal(OrderItemStatus.Submitted, item.Status);
        Assert.Equal(1, order.RoundNumber);
        Assert.Equal(OrderSource.Customer, order.OrderSource);
        Assert.Equal("TRY", order.Currency.Code);

        // Item total: (2500 + 500) * 2 = 6000
        Assert.Equal(6000L, item.LineTotalMinorUnits);
        Assert.Equal(500L, item.ModifierTotalMinorUnits);
        // Order subtotal: 6000
        Assert.Equal(6000L, order.SubtotalMinorUnits);
        // Tax: (6000 * 1000) / 10000 = 600
        Assert.Equal(600L, order.TaxMinorUnits);
        Assert.Equal(200L, order.ServiceChargeMinorUnits);
        // Total: 6000 + 600 + 200 = 6800
        Assert.Equal(6800L, order.TotalMinorUnits);
        Assert.NotEmpty(order.RequestFingerprint);
        Assert.NotEqual(Guid.Empty, order.ConcurrencyToken);
    }

    [Fact]
    public void CreateSubmitted_EmptyItems_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Order.CreateSubmitted(
                SampleTenantId,
                SampleBranchId,
                SampleSessionId,
                SampleTableId,
                roundNumber: 1,
                OrderSource.Customer,
                Currency.Default,
                "key-1",
                Array.Empty<OrderItem>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateSubmitted_InvalidRoundNumber_ThrowsDomainException(int invalidRound)
    {
        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Burger",
            null,
            quantity: 1,
            unitPriceMinorUnits: 1000L,
            taxRateBasisPoints: 1000);

        Assert.Throws<DomainException>(() =>
            Order.CreateSubmitted(
                SampleTenantId,
                SampleBranchId,
                SampleSessionId,
                SampleTableId,
                invalidRound,
                OrderSource.Customer,
                Currency.Default,
                "key-1",
                new[] { item }));
    }

    [Fact]
    public void CreateSubmitted_CrossTenantItem_ThrowsDomainException()
    {
        var otherTenantId = new TenantId(Guid.NewGuid());
        var item = OrderItem.Create(
            OrderId.New(),
            otherTenantId,
            SampleMenuItemId,
            null,
            "Burger",
            null,
            quantity: 1,
            unitPriceMinorUnits: 1000L,
            taxRateBasisPoints: 1000);

        Assert.Throws<DomainException>(() =>
            Order.CreateSubmitted(
                SampleTenantId,
                SampleBranchId,
                SampleSessionId,
                SampleTableId,
                roundNumber: 1,
                OrderSource.Customer,
                Currency.Default,
                "key-1",
                new[] { item }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void OrderItem_InvalidQuantity_ThrowsDomainException(int invalidQuantity)
    {
        Assert.Throws<DomainException>(() =>
            OrderItem.Create(
                OrderId.New(),
                SampleTenantId,
                SampleMenuItemId,
                null,
                "Burger",
                null,
                invalidQuantity,
                unitPriceMinorUnits: 1000L,
                taxRateBasisPoints: 1000));
    }

    [Fact]
    public void OrderItem_NegativeUnitPrice_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            OrderItem.Create(
                OrderId.New(),
                SampleTenantId,
                SampleMenuItemId,
                null,
                "Burger",
                null,
                quantity: 1,
                unitPriceMinorUnits: -500L,
                taxRateBasisPoints: 1000));
    }

    [Fact]
    public void OrderItemModifier_NegativePriceDelta_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            OrderItemModifier.Create(
                OrderItemId.New(),
                Guid.NewGuid(),
                "Extras",
                Guid.NewGuid(),
                "Cheese",
                priceDeltaMinorUnits: -100L));
    }

    [Fact]
    public void OrderItem_CustomerNote_SanitizesAndEnforcesLength()
    {
        // HTML rejection
        Assert.Throws<DomainException>(() =>
            OrderItem.Create(
                OrderId.New(),
                SampleTenantId,
                SampleMenuItemId,
                null,
                "Burger",
                null,
                quantity: 1,
                unitPriceMinorUnits: 1000L,
                taxRateBasisPoints: 1000,
                customerNote: "<script>alert(1)</script>"));

        // Max length rejection
        var tooLongNote = new string('A', 501);
        Assert.Throws<DomainException>(() =>
            OrderItem.Create(
                OrderId.New(),
                SampleTenantId,
                SampleMenuItemId,
                null,
                "Burger",
                null,
                quantity: 1,
                unitPriceMinorUnits: 1000L,
                taxRateBasisPoints: 1000,
                customerNote: tooLongNote));

        // Valid trimmed note
        var validItem = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Burger",
            null,
            quantity: 1,
            unitPriceMinorUnits: 1000L,
            taxRateBasisPoints: 1000,
            customerNote: "  Lütfen soğansız olsun  ");

        Assert.Equal("Lütfen soğansız olsun", validItem.CustomerNote);
    }

    [Fact]
    public void Order_LifecycleTransitions_SucceedsInValidSequence()
    {
        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Pizza",
            null,
            quantity: 1,
            unitPriceMinorUnits: 3000L,
            taxRateBasisPoints: 1000);

        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            "key-lifecycle",
            new[] { item });

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal(OrderItemStatus.Submitted, item.Status);

        order.StartPreparation();
        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(OrderItemStatus.InPreparation, item.Status);

        order.MarkReady();
        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(OrderItemStatus.Ready, item.Status);

        order.MarkServed();
        Assert.Equal(OrderStatus.Served, order.Status);
        Assert.Equal(OrderItemStatus.Served, item.Status);
    }

    [Fact]
    public void Order_InvalidTransition_ThrowsDomainException()
    {
        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Pizza",
            null,
            quantity: 1,
            unitPriceMinorUnits: 3000L,
            taxRateBasisPoints: 1000);

        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            "key-invalid-transition",
            new[] { item });

        // Cannot skip directly from Submitted to Ready or Served
        Assert.Throws<DomainException>(() => order.MarkReady());
        Assert.Throws<DomainException>(() => order.MarkServed());
    }

    [Fact]
    public void Order_CustomerCannotDirectlyCancelOrder_StaffRequired()
    {
        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Soup",
            null,
            quantity: 1,
            unitPriceMinorUnits: 1500L,
            taxRateBasisPoints: 1000);

        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            "key-cancel",
            new[] { item });

        // Customer role attempted cancellation
        Assert.Throws<DomainException>(() =>
            order.CancelByStaff(Guid.NewGuid(), "Customer", "Müşteri vazgeçti"));

        // Valid staff cancellation
        var staffId = Guid.NewGuid();
        order.CancelByStaff(staffId, "Waiter", "Müşteri siparişi değiştirmek istedi");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(OrderItemStatus.Cancelled, item.Status);
        Assert.Equal(staffId, order.CancelledByActorId);
        Assert.Equal("Müşteri siparişi değiştirmek istedi", order.CancellationReason);
    }

    [Fact]
    public void Order_ServedOrderCannotBeCancelled()
    {
        var item = OrderItem.Create(
            OrderId.New(),
            SampleTenantId,
            SampleMenuItemId,
            null,
            "Coffee",
            null,
            quantity: 1,
            unitPriceMinorUnits: 800L,
            taxRateBasisPoints: 1000);

        var order = Order.CreateSubmitted(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            roundNumber: 1,
            OrderSource.Staff,
            Currency.Default,
            "key-served",
            new[] { item });

        order.StartPreparation();
        order.MarkReady();
        order.MarkServed();

        Assert.Throws<DomainException>(() =>
            order.CancelByStaff(Guid.NewGuid(), "BranchManager", "Geç iptal denemesi"));
    }

    [Fact]
    public void OrderAuditEntry_Record_CreatesImmutableAuditRecord()
    {
        var orderId = OrderId.New();
        var actorId = Guid.NewGuid();

        var audit = OrderAuditEntry.Record(
            SampleTenantId,
            orderId,
            SampleSessionId,
            actorId,
            "Waiter",
            "OrderSubmitted",
            previousStatus: null,
            newStatus: OrderStatus.Submitted,
            reason: null,
            detailsJson: "{\"round\":1}");

        Assert.Equal(SampleTenantId, audit.TenantId);
        Assert.Equal(orderId, audit.OrderId);
        Assert.Equal(SampleSessionId, audit.DiningSessionId);
        Assert.Equal("OrderSubmitted", audit.Action);
        Assert.Equal(OrderStatus.Submitted, audit.NewStatus);
        Assert.Equal("{\"round\":1}", audit.DetailsJson);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }

    [Fact]
    public void Order_RequestFingerprint_MatchesConsistently()
    {
        var fp1 = Order.ComputeFingerprint(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            1,
            OrderSource.Customer,
            Currency.Default,
            1,
            2500L,
            "same-key");

        var fp2 = Order.ComputeFingerprint(
            SampleTenantId,
            SampleBranchId,
            SampleSessionId,
            SampleTableId,
            1,
            OrderSource.Customer,
            Currency.Default,
            1,
            2500L,
            "same-key");

        Assert.Equal(fp1, fp2);
        Assert.True(Order.MatchesFingerprint(fp1, fp2));
        Assert.False(Order.MatchesFingerprint(fp1, "different-hash"));
    }
}
