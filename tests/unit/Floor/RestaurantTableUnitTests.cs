using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class RestaurantTableUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _diningAreaId = DiningAreaId.New();

    [Fact]
    public void Create_ValidParameters_SuccessfullyInstantiates()
    {
        var table = RestaurantTable.Create(
            _tenantId,
            _branchId,
            _diningAreaId,
            tableNumber: "T-01",
            name: "Window Table 1",
            capacity: 4,
            positionX: 100,
            positionY: 200,
            width: 120,
            height: 80,
            rotationDegrees: 90,
            shape: TableShape.Rectangle);

        Assert.NotNull(table);
        Assert.NotEqual(Guid.Empty, table.Id.Value);
        Assert.Equal(_tenantId, table.TenantId);
        Assert.Equal(_branchId, table.BranchId);
        Assert.Equal(_diningAreaId, table.DiningAreaId);
        Assert.Equal("T-01", table.TableNumber);
        Assert.Equal("Window Table 1", table.Name);
        Assert.Equal(4, table.Capacity);
        Assert.Equal(100, table.PositionX);
        Assert.Equal(200, table.PositionY);
        Assert.Equal(120, table.Width);
        Assert.Equal(80, table.Height);
        Assert.Equal(90, table.RotationDegrees);
        Assert.Equal(TableShape.Rectangle, table.Shape);
        Assert.True(table.IsActive);
        Assert.Equal(1, table.QrVersion);
        Assert.NotEqual(Guid.Empty, table.ConcurrencyToken);
        Assert.True(table.CreatedAtUtc <= DateTime.UtcNow);
        Assert.Null(table.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Create_CapacityBounds_Valid(int capacity)
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", capacity, 0, 0, 100, 100, 0, TableShape.Square);
        Assert.Equal(capacity, table.Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_CapacityBounds_Invalid_ThrowsDomainException(int capacity)
    {
        var ex = Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", capacity, 0, 0, 100, 100, 0, TableShape.Square));
        Assert.Contains("Capacity", ex.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10000, 10000)]
    public void Create_Coordinates_Valid(int x, int y)
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, x, y, 100, 100, 0, TableShape.Square);
        Assert.Equal(x, table.PositionX);
        Assert.Equal(y, table.PositionY);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(10001, 0)]
    [InlineData(0, 10001)]
    public void Create_Coordinates_Invalid_ThrowsDomainException(int x, int y)
    {
        var ex = Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, x, y, 100, 100, 0, TableShape.Square));
        Assert.Contains("Position", ex.Message);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(5000, 5000)]
    public void Create_Dimensions_Valid(int width, int height)
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, width, height, 0, TableShape.Square);
        Assert.Equal(width, table.Width);
        Assert.Equal(height, table.Height);
    }

    [Theory]
    [InlineData(9, 100)]
    [InlineData(100, 9)]
    [InlineData(5001, 100)]
    [InlineData(100, 5001)]
    [InlineData(-10, 100)]
    public void Create_Dimensions_Invalid_ThrowsDomainException(int width, int height)
    {
        var ex = Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, width, height, 0, TableShape.Square));
        Assert.True(ex.Message.Contains("Width") || ex.Message.Contains("Height"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    [InlineData(359)]
    public void Create_Rotation_Valid(int rotation)
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, 100, 100, rotation, TableShape.Square);
        Assert.Equal(rotation, table.RotationDegrees);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    [InlineData(720)]
    public void Create_Rotation_Invalid_ThrowsDomainException(int rotation)
    {
        var ex = Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, 100, 100, rotation, TableShape.Square));
        Assert.Contains("Rotation", ex.Message);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Table <img src=x onerror=alert(1)>")]
    [InlineData("javascript:evil()")]
    [InlineData("Table<iframe>")]
    public void Create_HtmlOrScriptInjection_ThrowsDomainException(string maliciousInput)
    {
        Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, maliciousInput, "Valid Name", 4, 0, 0, 100, 100, 0, TableShape.Square));

        Assert.Throws<DomainException>(() => RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", maliciousInput, 4, 0, 0, 100, 100, 0, TableShape.Square));
    }

    [Fact]
    public void Update_ModifiesProperties_AndRegeneratesConcurrencyToken()
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Old Name", 2, 0, 0, 100, 100, 0, TableShape.Square);
        var oldToken = table.ConcurrencyToken;
        var newDiningAreaId = DiningAreaId.New();

        table.Update("T-01A", "New Name", 6, newDiningAreaId);

        Assert.Equal("T-01A", table.TableNumber);
        Assert.Equal("New Name", table.Name);
        Assert.Equal(6, table.Capacity);
        Assert.Equal(newDiningAreaId, table.DiningAreaId);
        Assert.NotEqual(oldToken, table.ConcurrencyToken);
        Assert.NotNull(table.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateLayout_ModifiesSpatialProperties_AndRegeneratesConcurrencyToken()
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, 100, 100, 0, TableShape.Square);
        var oldToken = table.ConcurrencyToken;

        table.UpdateLayout(250, 350, 200, 150, 45, TableShape.Round);

        Assert.Equal(250, table.PositionX);
        Assert.Equal(350, table.PositionY);
        Assert.Equal(200, table.Width);
        Assert.Equal(150, table.Height);
        Assert.Equal(45, table.RotationDegrees);
        Assert.Equal(TableShape.Round, table.Shape);
        Assert.NotEqual(oldToken, table.ConcurrencyToken);
        Assert.NotNull(table.UpdatedAtUtc);
    }

    [Fact]
    public void ActivateDeactivate_TransitionsStateCorrectly()
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, 100, 100, 0, TableShape.Square);
        Assert.True(table.IsActive);

        // Cannot activate an already active table
        Assert.Throws<DomainException>(() => table.Activate());

        // Deactivate active table
        var token1 = table.ConcurrencyToken;
        table.Deactivate();
        Assert.False(table.IsActive);
        Assert.NotEqual(token1, table.ConcurrencyToken);

        // Cannot deactivate an already inactive table
        Assert.Throws<DomainException>(() => table.Deactivate());

        // Re-activate inactive table
        var token2 = table.ConcurrencyToken;
        table.Activate();
        Assert.True(table.IsActive);
        Assert.NotEqual(token2, table.ConcurrencyToken);
    }

    [Fact]
    public void BumpQrVersion_IncrementsVersion_AndRegeneratesConcurrencyToken()
    {
        var table = RestaurantTable.Create(
            _tenantId, _branchId, _diningAreaId, "T-1", "Table 1", 4, 0, 0, 100, 100, 0, TableShape.Square);
        Assert.Equal(1, table.QrVersion);

        var oldToken = table.ConcurrencyToken;
        table.BumpQrVersion();

        Assert.Equal(2, table.QrVersion);
        Assert.NotEqual(oldToken, table.ConcurrencyToken);
    }
}
