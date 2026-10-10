using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class DiningSessionMovementUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _diningAreaId = DiningAreaId.From(Guid.NewGuid());

    private RestaurantTable CreateTable(int capacity = 4, bool isActive = true, string tableNumber = "T-1")
    {
        var table = RestaurantTable.Create(
            _tenantId,
            _branchId,
            _diningAreaId,
            tableNumber,
            "Table " + tableNumber,
            capacity);

        if (!isActive)
        {
            table.Deactivate();
        }

        return table;
    }

    [Fact]
    public void TransferToTable_Success_UpdatesTableIdAndConcurrencyToken()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 6, tableNumber: "T-2");
        var session = DiningSession.Open(_tenantId, _branchId, table1, 3);
        var initialToken = session.ConcurrencyToken;

        session.TransferToTable(table2);

        Assert.Equal(table2.Id, session.TableId);
        Assert.NotEqual(initialToken, session.ConcurrencyToken);
        Assert.NotNull(session.UpdatedAtUtc);
    }

    [Fact]
    public void TransferToTable_SameTable_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var session = DiningSession.Open(_tenantId, _branchId, table1, 2);

        var ex = Assert.Throws<DomainException>(() => session.TransferToTable(table1));
        Assert.Contains("same table", ex.Message);
    }

    [Fact]
    public void TransferToTable_InactiveTable_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 4, isActive: false, tableNumber: "T-2");
        var session = DiningSession.Open(_tenantId, _branchId, table1, 2);

        var ex = Assert.Throws<DomainException>(() => session.TransferToTable(table2));
        Assert.Contains("inactive table", ex.Message);
    }

    [Fact]
    public void TransferToTable_CapacityExceeded_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 6, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 2, tableNumber: "T-2");
        var session = DiningSession.Open(_tenantId, _branchId, table1, 4);

        var ex = Assert.Throws<DomainException>(() => session.TransferToTable(table2));
        Assert.Contains("exceeds target table capacity", ex.Message);
    }

    [Fact]
    public void TransferToTable_BillRequestedOrClosed_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 4, tableNumber: "T-2");
        var session = DiningSession.Open(_tenantId, _branchId, table1, 2);
        session.Activate();
        session.RequestBill();

        var ex1 = Assert.Throws<DomainException>(() => session.TransferToTable(table2));
        Assert.Contains("bill requested", ex1.Message);

        session.Close("Settled");
        var ex2 = Assert.Throws<DomainException>(() => session.TransferToTable(table2));
        Assert.Contains("closed", ex2.Message);
    }

    [Fact]
    public void TransferToTable_CrossTenantOrBranch_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var foreignTenantTable = RestaurantTable.Create(
            TenantId.New(), _branchId, _diningAreaId, "T-F1", "Foreign", 4);
        var session = DiningSession.Open(_tenantId, _branchId, table1, 2);

        var ex = Assert.Throws<DomainException>(() => session.TransferToTable(foreignTenantTable));
        Assert.Contains("tenant", ex.Message);
    }

    [Fact]
    public void MergeInto_Success_TerminalizesSourceAndSetsMergedIntoSessionId()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 8, tableNumber: "T-2");
        var sourceSession = DiningSession.Open(_tenantId, _branchId, table1, 2);
        var targetSession = DiningSession.Open(_tenantId, _branchId, table2, 3);

        targetSession.AcceptMergedGuests(sourceSession.GuestCount, table2);
        sourceSession.MergeInto(targetSession, "Combining party");

        Assert.Equal(DiningSessionStatus.Closed, sourceSession.Status);
        Assert.Equal(targetSession.Id, sourceSession.MergedIntoSessionId);
        Assert.NotNull(sourceSession.ClosedAtUtc);
        Assert.Equal("Combining party", sourceSession.CloseReason);
        Assert.Equal(5, targetSession.GuestCount);
    }

    [Fact]
    public void MergeInto_SelfMerge_ThrowsDomainException()
    {
        var table = CreateTable(capacity: 6, tableNumber: "T-1");
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);

        var ex = Assert.Throws<DomainException>(() => session.MergeInto(session));
        Assert.Contains("into itself", ex.Message);
    }

    [Fact]
    public void MergeInto_ClosedOrBillRequested_ThrowsDomainException()
    {
        var table1 = CreateTable(capacity: 4, tableNumber: "T-1");
        var table2 = CreateTable(capacity: 6, tableNumber: "T-2");
        var source = DiningSession.Open(_tenantId, _branchId, table1, 2);
        var target = DiningSession.Open(_tenantId, _branchId, table2, 2);
        source.Activate();
        source.RequestBill();

        var ex1 = Assert.Throws<DomainException>(() => source.MergeInto(target));
        Assert.Contains("bill requested", ex1.Message);

        source.Close("Paid");
        var ex2 = Assert.Throws<DomainException>(() => source.MergeInto(target));
        Assert.Contains("already closed", ex2.Message);
    }

    [Fact]
    public void AcceptMergedGuests_CombinedCapacityExceeded_ThrowsDomainException()
    {
        var table = CreateTable(capacity: 4, tableNumber: "T-1");
        var target = DiningSession.Open(_tenantId, _branchId, table, 3);

        var ex = Assert.Throws<DomainException>(() => target.AcceptMergedGuests(2, table));
        Assert.Contains("exceeds target table capacity", ex.Message);
    }

    [Fact]
    public void CreateTransfer_ValidInput_PopulatesAllFields()
    {
        var sessionId = DiningSessionId.New();
        var t1 = RestaurantTableId.New();
        var t2 = RestaurantTableId.New();
        var actorId = Guid.NewGuid();

        var movement = DiningSessionMovement.CreateTransfer(
            _tenantId, _branchId, sessionId, t1, t2, 3, actorId, "Waiter", "key-123", "Moving closer to window");

        Assert.Equal(DiningSessionMovementType.Transfer, movement.MovementType);
        Assert.Equal(sessionId, movement.SourceSessionId);
        Assert.Equal(sessionId, movement.TargetSessionId);
        Assert.Equal(t1, movement.SourceTableId);
        Assert.Equal(t2, movement.TargetTableId);
        Assert.Equal(3, movement.GuestsMoved);
        Assert.Equal("key-123", movement.IdempotencyKey);
        Assert.Equal("Moving closer to window", movement.Reason);
    }

    [Fact]
    public void CreateMerge_ValidInput_PopulatesAllFields()
    {
        var s1 = DiningSessionId.New();
        var s2 = DiningSessionId.New();
        var t1 = RestaurantTableId.New();
        var t2 = RestaurantTableId.New();
        var actorId = Guid.NewGuid();

        var movement = DiningSessionMovement.CreateMerge(
            _tenantId, _branchId, s1, t1, s2, t2, 2, actorId, "BranchManager", "key-merge", "Joined friends");

        Assert.Equal(DiningSessionMovementType.Merge, movement.MovementType);
        Assert.Equal(s1, movement.SourceSessionId);
        Assert.Equal(s2, movement.TargetSessionId);
        Assert.Equal(t1, movement.SourceTableId);
        Assert.Equal(t2, movement.TargetTableId);
        Assert.Equal(2, movement.GuestsMoved);
        Assert.Equal("key-merge", movement.IdempotencyKey);
    }
}
