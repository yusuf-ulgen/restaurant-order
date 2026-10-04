using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class MenuAndCategoryUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void Menu_Create_ValidInputs_InitializesDraftMenu()
    {
        var menu = Menu.Create(
            _tenantId,
            _branchId,
            "Ana Menü",
            "ana-menu",
            "Gündüz ve akşam servisi ana menüsü",
            sortOrder: 1);

        Assert.Equal(_tenantId, menu.TenantId);
        Assert.Equal(_branchId, menu.BranchId);
        Assert.Equal("Ana Menü", menu.Name);
        Assert.Equal("ana-menu", menu.Slug);
        Assert.Equal("Gündüz ve akşam servisi ana menüsü", menu.Description);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Equal(1, menu.SortOrder);
        Assert.NotEqual(Guid.Empty, menu.ConcurrencyToken);
        Assert.NotEqual(default, menu.CreatedAtUtc);
        Assert.Null(menu.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Menu_Create_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Menu.Create(_tenantId, _branchId, invalidName!, "slug-1"));

        Assert.Contains("Menu name cannot be empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ana Menü!")]
    [InlineData("ana menu")]
    [InlineData("-ana-menu")]
    public void Menu_Create_InvalidSlug_ThrowsDomainException(string? invalidSlug)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Menu.Create(_tenantId, _branchId, "Ana Menü", invalidSlug!));

        Assert.NotNull(ex);
    }

    [Fact]
    public void Menu_Create_NegativeSortOrder_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Menu.Create(_tenantId, _branchId, "Ana Menü", "ana-menu", sortOrder: -1));

        Assert.Contains("SortOrder cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Menu_Activate_TransitionsFromDraftToActive()
    {
        var menu = Menu.Create(_tenantId, _branchId, "Ana Menü", "ana-menu");
        var initialToken = menu.ConcurrencyToken;

        menu.Activate();

        Assert.Equal(MenuStatus.Active, menu.Status);
        Assert.NotEqual(initialToken, menu.ConcurrencyToken);
        Assert.NotNull(menu.UpdatedAtUtc);
    }

    [Fact]
    public void Menu_Activate_WhenAlreadyActive_IsIdempotent()
    {
        var menu = Menu.Create(_tenantId, _branchId, "Ana Menü", "ana-menu");
        menu.Activate();

        var token = menu.ConcurrencyToken;
        menu.Activate();

        Assert.Equal(MenuStatus.Active, menu.Status);
        Assert.Equal(token, menu.ConcurrencyToken);
    }

    [Fact]
    public void Menu_Archive_TransitionsToArchivedTerminalState()
    {
        var menu = Menu.Create(_tenantId, _branchId, "Ana Menü", "ana-menu");
        menu.Activate();
        menu.Archive();

        Assert.Equal(MenuStatus.Archived, menu.Status);

        // Cannot reactivate
        var ex = Assert.Throws<DomainException>(() => menu.Activate());
        Assert.Contains("terminal state", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Cannot update details
        var updateEx = Assert.Throws<DomainException>(() => menu.UpdateDetails("Yeni Ad", null, 2));
        Assert.Contains("terminal state", updateEx.Message, StringComparison.OrdinalIgnoreCase);

        // Archive again is idempotent
        var token = menu.ConcurrencyToken;
        menu.Archive();
        Assert.Equal(token, menu.ConcurrencyToken);
    }

    [Fact]
    public void Menu_UpdateDetails_ModifiesFieldsAndRefreshesToken()
    {
        var menu = Menu.Create(_tenantId, _branchId, "Eski Ad", "eski-slug", "Eski Açıklama", 0);
        var oldToken = menu.ConcurrencyToken;

        menu.UpdateDetails("Yeni Ad", "Yeni Açıklama", 3);

        Assert.Equal("Yeni Ad", menu.Name);
        Assert.Equal("Yeni Açıklama", menu.Description);
        Assert.Equal(3, menu.SortOrder);
        Assert.NotEqual(oldToken, menu.ConcurrencyToken);
        Assert.NotNull(menu.UpdatedAtUtc);
    }

    [Fact]
    public void MenuCategory_Create_ValidInputs_InitializesActiveCategory()
    {
        var menuId = MenuId.New();
        var category = MenuCategory.Create(
            _tenantId,
            _branchId,
            menuId,
            "Başlangıçlar",
            "baslangiclar",
            "Nefis mezeler ve çorbalar",
            sortOrder: 1);

        Assert.Equal(_tenantId, category.TenantId);
        Assert.Equal(_branchId, category.BranchId);
        Assert.Equal(menuId, category.MenuId);
        Assert.Equal("Başlangıçlar", category.Name);
        Assert.Equal("baslangiclar", category.Slug);
        Assert.Equal("Nefis mezeler ve çorbalar", category.Description);
        Assert.True(category.IsActive);
        Assert.Equal(1, category.SortOrder);
        Assert.NotEqual(Guid.Empty, category.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MenuCategory_Create_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            MenuCategory.Create(_tenantId, _branchId, MenuId.New(), invalidName!, "slug-1"));

        Assert.Contains("Category name cannot be empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Başlangıç!")]
    [InlineData("baslangic lar")]
    public void MenuCategory_Create_InvalidSlug_ThrowsDomainException(string? invalidSlug)
    {
        var ex = Assert.Throws<DomainException>(() =>
            MenuCategory.Create(_tenantId, _branchId, MenuId.New(), "Başlangıçlar", invalidSlug!));

        Assert.NotNull(ex);
    }

    [Fact]
    public void MenuCategory_ActivateDeactivate_Idempotent()
    {
        var category = MenuCategory.Create(_tenantId, _branchId, MenuId.New(), "Tatlılar", "tatlilar");
        Assert.True(category.IsActive);

        var token1 = category.ConcurrencyToken;
        category.Deactivate();
        Assert.False(category.IsActive);
        Assert.NotEqual(token1, category.ConcurrencyToken);

        // Deactivate again
        var token2 = category.ConcurrencyToken;
        category.Deactivate();
        Assert.False(category.IsActive);
        Assert.Equal(token2, category.ConcurrencyToken);

        // Activate
        category.Activate();
        Assert.True(category.IsActive);
        Assert.NotEqual(token2, category.ConcurrencyToken);

        // Activate again
        var token3 = category.ConcurrencyToken;
        category.Activate();
        Assert.True(category.IsActive);
        Assert.Equal(token3, category.ConcurrencyToken);
    }

    [Fact]
    public void MenuCategory_UpdateDetails_ModifiesFieldsAndRefreshesToken()
    {
        var category = MenuCategory.Create(_tenantId, _branchId, MenuId.New(), "İçecekler", "icecekler", null, 0);
        var oldToken = category.ConcurrencyToken;

        category.UpdateDetails("Soğuk İçecekler", "Meşrubatlar ve limonatalar", 2);

        Assert.Equal("Soğuk İçecekler", category.Name);
        Assert.Equal("Meşrubatlar ve limonatalar", category.Description);
        Assert.Equal(2, category.SortOrder);
        Assert.NotEqual(oldToken, category.ConcurrencyToken);
    }

    [Fact]
    public void MenuCategory_UpdateSortOrder_ValidatesAndUpdates()
    {
        var category = MenuCategory.Create(_tenantId, _branchId, MenuId.New(), "İçecekler", "icecekler", null, 1);

        var token = category.ConcurrencyToken;
        category.UpdateSortOrder(5);
        Assert.Equal(5, category.SortOrder);
        Assert.NotEqual(token, category.ConcurrencyToken);

        // Negative throws
        Assert.Throws<DomainException>(() => category.UpdateSortOrder(-1));
    }
}
