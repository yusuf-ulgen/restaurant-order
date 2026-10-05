using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class MenuItemAndVariantUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly MenuId _menuId = MenuId.New();
    private readonly MenuCategoryId _categoryId = MenuCategoryId.New();

    [Fact]
    public void MenuItem_Create_ValidInputs_InitializesActiveItem()
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Margherita Pizza",
            "margherita-pizza",
            PriceAmount.FromMinorUnits(18000),
            "Classic pizza",
            "Tomato sauce, fresh mozzarella, and basil",
            "https://cdn.example.com/pizza.jpg",
            1);

        Assert.Equal(_tenantId, item.TenantId);
        Assert.Equal(_branchId, item.BranchId);
        Assert.Equal(_menuId, item.MenuId);
        Assert.Equal(_categoryId, item.CategoryId);
        Assert.Equal("Margherita Pizza", item.Name);
        Assert.Equal("margherita-pizza", item.Slug);
        Assert.Equal(18000, item.BasePriceMinorUnits.MinorUnits);
        Assert.Equal("Classic pizza", item.ShortDescription);
        Assert.Equal("Tomato sauce, fresh mozzarella, and basil", item.FullDescription);
        Assert.Equal("https://cdn.example.com/pizza.jpg", item.ImageUrl);
        Assert.Equal(1, item.SortOrder);
        Assert.True(item.IsActive);
        Assert.NotEqual(Guid.Empty, item.ConcurrencyToken);
        Assert.NotEqual(default, item.CreatedAtUtc);
        Assert.Null(item.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("/images/pizza.jpg")]
    [InlineData("https://cdn.example.com/pizza.jpg")]
    [InlineData("https://assets.restaurant.com/images/menu/burger_v2.png")]
    public void MenuItem_Create_ValidImageUrl_Succeeds(string validUrl)
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Pizza",
            "pizza",
            PriceAmount.FromMinorUnits(1000),
            imageUrl: validUrl);

        Assert.Equal(validUrl, item.ImageUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://insecure.com/image.png")]
    [InlineData("//protocol-relative.com/image.png")]
    [InlineData("ftp://files.com/image.png")]
    [InlineData("vbscript:msgbox(1)")]
    public void MenuItem_Create_InsecureOrDisallowedImageUrl_ThrowsDomainException(string insecureUrl)
    {
        var ex = Assert.Throws<DomainException>(() => MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Pizza",
            "pizza",
            PriceAmount.FromMinorUnits(1000),
            imageUrl: insecureUrl));

        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>Pizza")]
    [InlineData("Pizza <b>Bold</b>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void MenuItem_Create_HtmlInNameOrDescription_ThrowsDomainException(string dirtyInput)
    {
        Assert.Throws<DomainException>(() => MenuItem.Create(
            _tenantId, _branchId, _menuId, _categoryId, dirtyInput, "pizza-clean", PriceAmount.FromMinorUnits(1000)));

        Assert.Throws<DomainException>(() => MenuItem.Create(
            _tenantId, _branchId, _menuId, _categoryId, "Pizza Clean", "pizza-clean-2", PriceAmount.FromMinorUnits(1000), shortDescription: dirtyInput));

        Assert.Throws<DomainException>(() => MenuItem.Create(
            _tenantId, _branchId, _menuId, _categoryId, "Pizza Clean", "pizza-clean-3", PriceAmount.FromMinorUnits(1000), fullDescription: dirtyInput));
    }

    [Fact]
    public void MenuItem_AddVariant_SingleActiveDefaultVariant_Succeeds()
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Espresso",
            "espresso",
            PriceAmount.FromMinorUnits(5000));

        var single = item.AddVariant("Single", "ESP-SGL", PriceAmount.FromMinorUnits(5000), isDefault: true);
        var @double = item.AddVariant("Double", "ESP-DBL", PriceAmount.FromMinorUnits(7500), isDefault: false);

        Assert.Equal(2, item.Variants.Count);
        Assert.True(single.IsDefault);
        Assert.False(@double.IsDefault);
    }

    [Fact]
    public void MenuItem_AddVariant_MultipleActiveDefaults_ThrowsDomainException()
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Espresso",
            "espresso",
            PriceAmount.FromMinorUnits(5000));

        item.AddVariant("Single", "ESP-SGL", PriceAmount.FromMinorUnits(5000), isDefault: true);

        var ex = Assert.Throws<DomainException>(() =>
            item.AddVariant("Double", "ESP-DBL", PriceAmount.FromMinorUnits(7500), isDefault: true));

        Assert.Contains("A menu item can have at most one active default variant", ex.Message);
    }

    [Fact]
    public void ItemVariant_Create_ValidInputs_InitializesVariant()
    {
        var itemId = MenuItemId.New();
        var variant = ItemVariant.Create(
            _tenantId,
            _branchId,
            _menuId,
            itemId,
            "Large",
            "PIZ-LRG",
            PriceAmount.FromMinorUnits(24000),
            sortOrder: 2,
            isDefault: false);

        Assert.Equal(_tenantId, variant.TenantId);
        Assert.Equal(_branchId, variant.BranchId);
        Assert.Equal(_menuId, variant.MenuId);
        Assert.Equal(itemId, variant.MenuItemId);
        Assert.Equal("Large", variant.Name);
        Assert.Equal("PIZ-LRG", variant.Code);
        Assert.Equal(24000, variant.AbsolutePriceMinorUnits.MinorUnits);
        Assert.Equal(2, variant.SortOrder);
        Assert.False(variant.IsDefault);
        Assert.True(variant.IsActive);
        Assert.NotEqual(Guid.Empty, variant.ConcurrencyToken);
    }

    [Theory]
    [InlineData("INVALID CODE!")]
    [InlineData("code with spaces")]
    [InlineData("")]
    [InlineData("code@special")]
    public void ItemVariant_Create_InvalidCode_ThrowsDomainException(string invalidCode)
    {
        var itemId = MenuItemId.New();
        var ex = Assert.Throws<DomainException>(() => ItemVariant.Create(
            _tenantId,
            _branchId,
            _menuId,
            itemId,
            "Large",
            invalidCode,
            PriceAmount.FromMinorUnits(1000)));

        Assert.Contains("variant code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemVariant_PriceUpdate_UpdatesPriceAndConcurrencyToken()
    {
        var itemId = MenuItemId.New();
        var variant = ItemVariant.Create(
            _tenantId,
            _branchId,
            _menuId,
            itemId,
            "Regular",
            "REG",
            PriceAmount.FromMinorUnits(1000));

        var initialToken = variant.ConcurrencyToken;
        variant.UpdatePrice(PriceAmount.FromMinorUnits(1250));

        Assert.Equal(1250, variant.AbsolutePriceMinorUnits.MinorUnits);
        Assert.NotEqual(initialToken, variant.ConcurrencyToken);
        Assert.NotNull(variant.UpdatedAtUtc);
    }

    [Fact]
    public void MenuItem_ActivateDeactivate_TogglesActiveAndRefreshesToken()
    {
        var item = MenuItem.Create(
            _tenantId,
            _branchId,
            _menuId,
            _categoryId,
            "Item 1",
            "item-1",
            PriceAmount.FromMinorUnits(1000));

        Assert.True(item.IsActive);

        var token1 = item.ConcurrencyToken;
        item.Deactivate();
        Assert.False(item.IsActive);
        Assert.NotEqual(token1, item.ConcurrencyToken);

        var token2 = item.ConcurrencyToken;
        item.Activate();
        Assert.True(item.IsActive);
        Assert.NotEqual(token2, item.ConcurrencyToken);
    }
}
