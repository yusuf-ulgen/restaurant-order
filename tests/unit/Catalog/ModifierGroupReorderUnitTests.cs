using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class ModifierGroupReorderUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void ReorderOptions_SuccessfulReorder_UpdatesSortOrders()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);
        var opt2 = group.AddOption("Pickles", PriceAmount.Zero);
        var opt3 = group.AddOption("Tomatoes", PriceAmount.Zero);

        var newOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt3.Id, 0),
            (opt1.Id, 1),
            (opt2.Id, 2)
        };

        group.ReorderOptions(newOrder);

        Assert.Equal(0, opt3.SortOrder);
        Assert.Equal(1, opt1.SortOrder);
        Assert.Equal(2, opt2.SortOrder);
    }

    [Fact]
    public void ReorderOptions_MissingOption_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);
        var opt2 = group.AddOption("Pickles", PriceAmount.Zero);

        // Missing opt2
        var incompleteOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt1.Id, 0)
        };

        var ex = Assert.Throws<DomainException>(() => group.ReorderOptions(incompleteOrder));
        Assert.Contains("must contain all", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorderOptions_ExtraOrUnknownOption_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);
        var opt2 = group.AddOption("Pickles", PriceAmount.Zero);

        // 2 options provided, but one is unknown
        var extraOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt1.Id, 0),
            (ModifierOptionId.New(), 1)
        };

        var ex = Assert.Throws<DomainException>(() => group.ReorderOptions(extraOrder));
        Assert.Contains("does not belong to this modifier group", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorderOptions_DuplicateOptionId_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);
        var opt2 = group.AddOption("Pickles", PriceAmount.Zero);

        var dupOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt1.Id, 0),
            (opt1.Id, 1)
        };

        var ex = Assert.Throws<DomainException>(() => group.ReorderOptions(dupOrder));
        Assert.Contains("Duplicate modifier option ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorderOptions_NegativeSortOrder_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);

        var negOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt1.Id, -1)
        };

        var ex = Assert.Throws<DomainException>(() => group.ReorderOptions(negOrder));
        Assert.Contains("non-negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorderOptions_DuplicateSortOrder_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Toppings", 0, 3);
        var opt1 = group.AddOption("Onions", PriceAmount.Zero);
        var opt2 = group.AddOption("Pickles", PriceAmount.Zero);

        // Same sort order 0 for both
        var dupSortOrder = new List<(ModifierOptionId OptionId, int SortOrder)>
        {
            (opt1.Id, 0),
            (opt2.Id, 0)
        };

        var ex = Assert.Throws<DomainException>(() => group.ReorderOptions(dupSortOrder));
        Assert.Contains("Duplicate sort order value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
