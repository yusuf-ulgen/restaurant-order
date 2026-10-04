using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Catalog;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class ModifierAndMetadataUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(2, 1)]
    public void ModifierGroup_InvalidMinMaxCombinations_ThrowsDomainException(int min, int max)
    {
        Assert.Throws<DomainException>(() =>
            ModifierGroup.Create(_tenantId, _branchId, "Toppings", min, max));
    }

    [Theory]
    [InlineData(1, 1)] // Required single-select
    [InlineData(0, 1)] // Optional single-select
    [InlineData(0, 3)] // Optional multi-select
    [InlineData(2, 4)] // Required multi-select
    public void ModifierGroup_ValidMinMaxCombinations_Succeeds(int min, int max)
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Choice", min, max);
        Assert.Equal(min, group.MinSelections);
        Assert.Equal(max, group.MaxSelections);
        Assert.True(group.IsActive);
    }

    [Fact]
    public void ModifierGroup_MaxSelectionsCannotExceedOptionCount_WhenUpdated()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Sauce", 0, 1);
        group.AddOption("Ketchup", PriceAmount.Zero);

        // Group has 1 option. Trying to set maxSelections to 2 should throw.
        var ex = Assert.Throws<DomainException>(() =>
            group.UpdateDetails("Sauce", 0, 2, 0));

        Assert.Contains("cannot exceed total active options", ex.Message);
    }

    [Fact]
    public void ModifierGroup_DefaultOptionsCannotExceedMaxSelections()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Cheese", 0, 1);
        group.AddOption("Cheddar", PriceAmount.Zero, isDefault: true);

        // Adding a second default option when maxSelections=1 must throw
        var ex = Assert.Throws<DomainException>(() =>
            group.AddOption("Swiss", PriceAmount.Zero, isDefault: true));

        Assert.Contains("exceeds MaxSelections", ex.Message);
    }

    [Fact]
    public void ModifierGroup_DuplicateOptionNameInSameGroup_ThrowsDomainException()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Bread", 1, 1);
        group.AddOption("White", PriceAmount.Zero);

        var ex = Assert.Throws<DomainException>(() =>
            group.AddOption("white", PriceAmount.Zero));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModifierOption_NegativePriceDelta_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            PriceAmount.FromMinorUnits(-500));
    }

    [Fact]
    public void ModifierOption_ZeroPriceDelta_IsAllowed()
    {
        var option = ModifierOption.Create(
            _tenantId, _branchId, ModifierGroupId.New(), "Free Option", PriceAmount.Zero);

        Assert.Equal(0, option.PriceDeltaMinorUnits.MinorUnits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SpicyLevel_ValidLevels_Succeeds(int level)
    {
        var spicy = new SpicyLevel(level);
        Assert.Equal(level, spicy.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void SpicyLevel_InvalidLevels_ThrowsDomainException(int level)
    {
        Assert.Throws<DomainException>(() => new SpicyLevel(level));
    }

    [Fact]
    public void DietaryAndAllergen_GlutenFreeWithGluten_ThrowsDomainException()
    {
        var dietary = new HashSet<DietaryTag> { DietaryTag.GlutenFree };
        var allergens = new HashSet<AllergenTag> { AllergenTag.Gluten };

        var ex = Assert.Throws<DomainException>(() =>
            MenuItem.ValidateDietaryAndAllergenConsistency(dietary, allergens));

        Assert.Contains("GlutenFree", ex.Message);
        Assert.Contains("Gluten", ex.Message);
    }

    [Fact]
    public void DietaryAndAllergen_DairyFreeWithMilk_ThrowsDomainException()
    {
        var dietary = new HashSet<DietaryTag> { DietaryTag.DairyFree };
        var allergens = new HashSet<AllergenTag> { AllergenTag.Milk };

        var ex = Assert.Throws<DomainException>(() =>
            MenuItem.ValidateDietaryAndAllergenConsistency(dietary, allergens));

        Assert.Contains("DairyFree", ex.Message);
        Assert.Contains("Milk", ex.Message);
    }

    [Theory]
    [InlineData(AllergenTag.Milk)]
    [InlineData(AllergenTag.Eggs)]
    [InlineData(AllergenTag.Fish)]
    [InlineData(AllergenTag.Crustaceans)]
    [InlineData(AllergenTag.Molluscs)]
    public void DietaryAndAllergen_VeganWithAnimalAllergens_ThrowsDomainException(AllergenTag allergen)
    {
        var dietary = new HashSet<DietaryTag> { DietaryTag.Vegan };
        var allergens = new HashSet<AllergenTag> { allergen };

        var ex = Assert.Throws<DomainException>(() =>
            MenuItem.ValidateDietaryAndAllergenConsistency(dietary, allergens));

        Assert.Contains("Vegan", ex.Message);
    }

    [Theory]
    [InlineData(AllergenTag.Fish)]
    [InlineData(AllergenTag.Crustaceans)]
    [InlineData(AllergenTag.Molluscs)]
    public void DietaryAndAllergen_VegetarianWithMeatOrFishAllergens_ThrowsDomainException(AllergenTag allergen)
    {
        var dietary = new HashSet<DietaryTag> { DietaryTag.Vegetarian };
        var allergens = new HashSet<AllergenTag> { allergen };

        var ex = Assert.Throws<DomainException>(() =>
            MenuItem.ValidateDietaryAndAllergenConsistency(dietary, allergens));

        Assert.Contains("Vegetarian", ex.Message);
    }

    [Fact]
    public void DietaryAndAllergen_VegetarianWithMilkAndEggs_IsAllowed()
    {
        var dietary = new HashSet<DietaryTag> { DietaryTag.Vegetarian };
        var allergens = new HashSet<AllergenTag> { AllergenTag.Milk, AllergenTag.Eggs };

        // Should not throw
        MenuItem.ValidateDietaryAndAllergenConsistency(dietary, allergens);
    }

    [Fact]
    public void ParseDietaryTags_InvalidValue_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CatalogService.ParseDietaryTags(new[] { "NonExistentDietaryTag" }));

        Assert.Contains("Invalid dietary tag", ex.Message);
    }

    [Fact]
    public void ParseAllergenTags_InvalidValue_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CatalogService.ParseAllergenTags(new[] { "FakeAllergen" }));

        Assert.Contains("Invalid allergen tag", ex.Message);
    }

    [Fact]
    public void MenuItem_AssignModifierGroup_CrossTenant_ThrowsDomainException()
    {
        var item = MenuItem.Create(
            _tenantId, _branchId, MenuId.New(), MenuCategoryId.New(), "Burger", "burger", PriceAmount.FromMinorUnits(1000));

        var otherTenantGroup = ModifierGroup.Create(
            TenantId.New(), _branchId, "Sides", 0, 1);
        otherTenantGroup.AddOption("Fries", PriceAmount.Zero);

        var ex = Assert.Throws<DomainException>(() =>
            item.AssignModifierGroup(otherTenantGroup));

        Assert.Contains("Cannot assign modifier group from tenant", ex.Message);
    }

    [Fact]
    public void MenuItem_AssignModifierGroup_CrossBranch_ThrowsDomainException()
    {
        var item = MenuItem.Create(
            _tenantId, _branchId, MenuId.New(), MenuCategoryId.New(), "Burger", "burger", PriceAmount.FromMinorUnits(1000));

        var otherBranchGroup = ModifierGroup.Create(
            _tenantId, BranchId.New(), "Sides", 0, 1);
        otherBranchGroup.AddOption("Fries", PriceAmount.Zero);

        var ex = Assert.Throws<DomainException>(() =>
            item.AssignModifierGroup(otherBranchGroup));

        Assert.Contains("Cannot assign modifier group from branch", ex.Message);
    }

    [Fact]
    public void ModifierGroup_ReusableAcrossMultipleItemsInSameBranch()
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Extras", 0, 2);
        group.AddOption("Bacon", PriceAmount.FromMinorUnits(200));
        group.AddOption("Cheese", PriceAmount.FromMinorUnits(150));

        var item1 = MenuItem.Create(_tenantId, _branchId, MenuId.New(), MenuCategoryId.New(), "Burger 1", "burger-1", PriceAmount.FromMinorUnits(1000));
        var item2 = MenuItem.Create(_tenantId, _branchId, MenuId.New(), MenuCategoryId.New(), "Burger 2", "burger-2", PriceAmount.FromMinorUnits(1200));

        item1.AssignModifierGroup(group, sortOrder: 0);
        item2.AssignModifierGroup(group, sortOrder: 1);

        Assert.Single(item1.ModifierGroupAssignments);
        Assert.Single(item2.ModifierGroupAssignments);
        Assert.Equal(group.Id, item1.ModifierGroupAssignments[0].ModifierGroupId);
        Assert.Equal(group.Id, item2.ModifierGroupAssignments[0].ModifierGroupId);
    }
}
