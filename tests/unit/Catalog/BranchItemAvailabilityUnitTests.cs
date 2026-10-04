using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class BranchItemAvailabilityUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly MenuItemId _menuItemId = MenuItemId.New();
    private readonly ItemVariantId _variantId = ItemVariantId.New();
    private readonly UserId _userId = UserId.New();

    [Fact]
    public void BranchItemAvailabilityId_Validations_WorkAsExpected()
    {
        var id = BranchItemAvailabilityId.New();
        Assert.NotEqual(Guid.Empty, id.Value);

        var fromId = BranchItemAvailabilityId.From(id.Value);
        Assert.Equal(id, fromId);
        Assert.Equal(id.Value.ToString(), id.ToString());

        Guid implicitGuid = id;
        Assert.Equal(id.Value, implicitGuid);

        var ex = Assert.Throws<DomainException>(() => new BranchItemAvailabilityId(Guid.Empty));
        Assert.Contains("cannot be empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AvailabilityReasonCode.SoldOut)]
    [InlineData(AvailabilityReasonCode.IngredientUnavailable)]
    [InlineData(AvailabilityReasonCode.TemporarilyDisabled)]
    [InlineData(AvailabilityReasonCode.KitchenCapacity)]
    [InlineData(AvailabilityReasonCode.Manual)]
    public void CreateItemUnavailable_ValidReasonCodes_InitializesUnavailableRecord(AvailabilityReasonCode reason)
    {
        var futureTime = DateTime.UtcNow.AddHours(2);
        var record = BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            reason,
            "Fresh ingredients arriving soon",
            futureTime,
            _userId);

        Assert.Equal(_tenantId, record.TenantId);
        Assert.Equal(_branchId, record.BranchId);
        Assert.Equal(_menuItemId, record.MenuItemId);
        Assert.Null(record.ItemVariantId);
        Assert.False(record.IsAvailable);
        Assert.Equal(reason, record.ReasonCode);
        Assert.Equal("Fresh ingredients arriving soon", record.Note);
        Assert.Equal(futureTime, record.ExpectedAvailableAtUtc);
        Assert.Equal(_userId, record.ChangedByUserId);
        Assert.NotEqual(Guid.Empty, record.ConcurrencyToken);
        Assert.NotEqual(default, record.ChangedAtUtc);
    }

    [Fact]
    public void CreateItemUnavailable_RestockedReasonCode_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            AvailabilityReasonCode.Restocked,
            null,
            null,
            _userId));

        Assert.Contains("cannot be used when marking an item or variant unavailable", ex.Message);
    }

    [Fact]
    public void CreateVariantUnavailable_ValidInput_InitializesVariantUnavailableRecord()
    {
        var record = BranchItemAvailability.CreateVariantUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            _variantId,
            AvailabilityReasonCode.IngredientUnavailable,
            "Almond milk out of stock",
            null,
            _userId);

        Assert.Equal(_tenantId, record.TenantId);
        Assert.Equal(_branchId, record.BranchId);
        Assert.Equal(_menuItemId, record.MenuItemId);
        Assert.Equal(_variantId, record.ItemVariantId);
        Assert.False(record.IsAvailable);
        Assert.Equal(AvailabilityReasonCode.IngredientUnavailable, record.ReasonCode);
        Assert.Equal("Almond milk out of stock", record.Note);
        Assert.Null(record.ExpectedAvailableAtUtc);
    }

    [Fact]
    public void ValidateNote_HtmlMarkup_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            AvailabilityReasonCode.SoldOut,
            "Sold out <script>alert(1)</script>",
            null,
            _userId));

        Assert.Contains("cannot contain HTML or markup tags", ex.Message);
    }

    [Fact]
    public void ValidateNote_Exceeds500Characters_ThrowsDomainException()
    {
        var longNote = new string('x', 501);
        var ex = Assert.Throws<DomainException>(() => BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            AvailabilityReasonCode.Manual,
            longNote,
            null,
            _userId));

        Assert.Contains("cannot exceed 500 characters", ex.Message);
    }

    [Fact]
    public void ValidateExpectedAvailableAtUtc_PastDate_ThrowsDomainException()
    {
        var pastTime = DateTime.UtcNow.AddMinutes(-5);
        var ex = Assert.Throws<DomainException>(() => BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            AvailabilityReasonCode.KitchenCapacity,
            "Busy kitchen",
            pastTime,
            _userId));

        Assert.Contains("cannot be in the past", ex.Message);
    }

    [Fact]
    public void Restock_UpdatesRecordToAvailable_AndClearsExpectedAvailableAt()
    {
        var record = BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            _menuItemId,
            AvailabilityReasonCode.KitchenCapacity,
            "Oven cooling",
            DateTime.UtcNow.AddHours(1),
            _userId);

        var initialToken = record.ConcurrencyToken;
        var newUserId = UserId.New();

        record.Restock("Restocked and ready to prepare", newUserId);

        Assert.True(record.IsAvailable);
        Assert.Equal(AvailabilityReasonCode.Restocked, record.ReasonCode);
        Assert.Equal("Restocked and ready to prepare", record.Note);
        Assert.Null(record.ExpectedAvailableAtUtc);
        Assert.Equal(newUserId, record.ChangedByUserId);
        Assert.NotEqual(initialToken, record.ConcurrencyToken);
    }

    [Fact]
    public void Quick86_DoesNotDeactivateCatalogItem()
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            MenuId.New(),
            MenuCategoryId.New(),
            "Cheeseburger",
            "cheeseburger",
            PriceAmount.FromMinorUnits(15000));

        Assert.True(item.IsActive);

        var availability = BranchItemAvailability.CreateItemUnavailable(
            _tenantId,
            _branchId,
            item.Id,
            AvailabilityReasonCode.SoldOut,
            null,
            null,
            _userId);

        // 86 affects inventory availability without changing catalog IsActive lifecycle
        Assert.False(availability.IsAvailable);
        Assert.True(item.IsActive);
    }
}
