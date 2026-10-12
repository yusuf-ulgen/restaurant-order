using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class DiningSessionUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _diningAreaId = DiningAreaId.New();

    private RestaurantTable CreateActiveTable(int capacity = 4)
    {
        return RestaurantTable.Create(
            _tenantId,
            _branchId,
            _diningAreaId,
            "T-10",
            "Table 10",
            capacity);
    }

    [Fact]
    public void Open_WithValidParameters_CreatesSessionInOpenStatus()
    {
        var table = CreateActiveTable(4);
        var waiterId = Guid.NewGuid();

        var session = DiningSession.Open(_tenantId, _branchId, table, 2, waiterId);

        Assert.NotEqual(Guid.Empty, session.Id.Value);
        Assert.Equal(_tenantId, session.TenantId);
        Assert.Equal(_branchId, session.BranchId);
        Assert.Equal(table.Id, session.TableId);
        Assert.Equal(DiningSessionStatus.Open, session.Status);
        Assert.Equal(2, session.GuestCount);
        Assert.Equal(waiterId, session.AssignedWaiterId);
        Assert.True(session.OpenedAtUtc <= DateTimeOffset.UtcNow);
        Assert.Null(session.ActivatedAtUtc);
        Assert.Null(session.BillRequestedAtUtc);
        Assert.Null(session.ClosedAtUtc);
        Assert.Null(session.CloseReason);
        Assert.Null(session.MergedIntoSessionId);
        Assert.NotEqual(Guid.Empty, session.ConcurrencyToken);
        Assert.Null(session.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Open_WithZeroOrNegativeGuestCount_ThrowsDomainException(int invalidCount)
    {
        var table = CreateActiveTable(4);

        var ex = Assert.Throws<DomainException>(() =>
            DiningSession.Open(_tenantId, _branchId, table, invalidCount));

        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_WithGuestCountExceedingCapacity_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);

        var ex = Assert.Throws<DomainException>(() =>
            DiningSession.Open(_tenantId, _branchId, table, 5));

        Assert.Contains("exceed table capacity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_WithExactCapacity_Succeeds()
    {
        var table = CreateActiveTable(6);

        var session = DiningSession.Open(_tenantId, _branchId, table, 6);

        Assert.Equal(6, session.GuestCount);
    }

    [Fact]
    public void Open_WithInactiveTable_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        table.Deactivate();

        var ex = Assert.Throws<DomainException>(() =>
            DiningSession.Open(_tenantId, _branchId, table, 2));

        Assert.Contains("inactive table", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_WithCrossTenantTable_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var otherTenantId = TenantId.New();

        var ex = Assert.Throws<DomainException>(() =>
            DiningSession.Open(otherTenantId, _branchId, table, 2));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_WithCrossBranchTable_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var otherBranchId = BranchId.New();

        var ex = Assert.Throws<DomainException>(() =>
            DiningSession.Open(_tenantId, otherBranchId, table, 2));

        Assert.Contains("branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateMachine_FullLinearProgression_Succeeds()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        var initialToken = session.ConcurrencyToken;

        // 1. Open -> Active
        session.Activate();
        Assert.Equal(DiningSessionStatus.Active, session.Status);
        Assert.NotNull(session.ActivatedAtUtc);
        Assert.NotEqual(initialToken, session.ConcurrencyToken);
        var activeToken = session.ConcurrencyToken;

        // 2. Active -> BillRequested
        session.RequestBill();
        Assert.Equal(DiningSessionStatus.BillRequested, session.Status);
        Assert.NotNull(session.BillRequestedAtUtc);
        Assert.NotEqual(activeToken, session.ConcurrencyToken);
        var billToken = session.ConcurrencyToken;

        // 3. BillRequested -> Closed
        session.Close("Guest settled bill via POS");
        Assert.Equal(DiningSessionStatus.Closed, session.Status);
        Assert.NotNull(session.ClosedAtUtc);
        Assert.Equal("Guest settled bill via POS", session.CloseReason);
        Assert.NotEqual(billToken, session.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();

        var ex = Assert.Throws<DomainException>(() => session.Activate());
        Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_WhenBillRequested_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();

        var ex = Assert.Throws<DomainException>(() => session.Activate());
        Assert.Contains("Cannot activate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_WhenClosed_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();
        session.Close();

        var ex = Assert.Throws<DomainException>(() => session.Activate());
        Assert.Contains("Cannot activate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestBill_WhenInOpenStatus_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);

        var ex = Assert.Throws<DomainException>(() => session.RequestBill());
        Assert.Contains("not yet active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestBill_WhenAlreadyBillRequested_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();

        var ex = Assert.Throws<DomainException>(() => session.RequestBill());
        Assert.Contains("already been requested", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestBill_WhenClosed_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();
        session.Close();

        var ex = Assert.Throws<DomainException>(() => session.RequestBill());
        Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenInOpenStatus_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);

        var ex = Assert.Throws<DomainException>(() => session.Close());
        Assert.Contains("Bill must be requested before closing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenInActiveStatus_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();

        var ex = Assert.Throws<DomainException>(() => session.Close());
        Assert.Contains("Bill must be requested before closing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();
        session.Close();

        var ex = Assert.Throws<DomainException>(() => session.Close());
        Assert.Contains("already closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WithExcessiveReasonLength_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();

        var longReason = new string('x', 501);
        var ex = Assert.Throws<DomainException>(() => session.Close(longReason));
        Assert.Contains("cannot exceed 500 characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssignWaiter_WhenOpenOrActive_Succeeds()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);

        var newWaiterId = Guid.NewGuid();
        session.AssignWaiter(newWaiterId);

        Assert.Equal(newWaiterId, session.AssignedWaiterId);
        Assert.NotNull(session.UpdatedAtUtc);
    }

    [Fact]
    public void AssignWaiter_WhenClosed_ThrowsDomainException()
    {
        var table = CreateActiveTable(4);
        var session = DiningSession.Open(_tenantId, _branchId, table, 2);
        session.Activate();
        session.RequestBill();
        session.Close();

        var ex = Assert.Throws<DomainException>(() => session.AssignWaiter(Guid.NewGuid()));
        Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiningSessionId_EmptyGuid_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => new DiningSessionId(Guid.Empty));
    }

    [Fact]
    public void DiningSessionId_FromAndCompare_WorksCorrectly()
    {
        var guid = Guid.NewGuid();
        var id1 = DiningSessionId.From(guid);
        var id2 = new DiningSessionId(guid);

        Assert.Equal(id1, id2);
        Assert.Equal(0, id1.CompareTo(id2));
        Assert.Equal(guid.ToString(), id1.ToString());
        Guid implicitGuid = id1;
        Assert.Equal(guid, implicitGuid);
    }
}
