using System.Text.Json;
using RestaurantOrder.Application.Catalog;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

/// <summary>
/// Verifies that backend RuntimeMenuReadModel and its nested records maintain exact contract alignment
/// with the TypeScript @restaurant-order/contracts definitions without drift.
/// </summary>
public class RuntimeMenuContractDriftTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RuntimeMenuReadModel_SerializesToExpectedCamelCaseJsonStructure()
    {
        var model = new RuntimeMenuReadModel(
            BranchId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Currency: "TRY",
            PricesIncludeTax: true,
            DefaultTaxRateBps: 1000,
            IsServiceChargeEnabled: true,
            ServiceChargeRateBps: 500,
            PricingPolicyFingerprint: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Menus: new[]
            {
                new RuntimeMenuDto(
                    Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Name: "Ana Menü",
                    Slug: "ana-menu",
                    Description: "Lezzetli yemekler",
                    SortOrder: 1,
                    Categories: new[]
                    {
                        new RuntimeMenuCategoryDto(
                            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            Name: "Burgerler",
                            Slug: "burgerler",
                            Description: "Taze burgerler",
                            SortOrder: 1,
                            Items: new[]
                            {
                                new RuntimeMenuItemDto(
                                    Id: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                                    Name: "Klasik Burger",
                                    Slug: "klasik-burger",
                                    ShortDescription: "150g dana köfte",
                                    FullDescription: "150g dana köfte, cheddar, karamelize soğan",
                                    ImageUrl: "https://example.com/burger.jpg",
                                    BasePriceMinorUnits: 25000,
                                    SortOrder: 1,
                                    SpicyLevel: 1,
                                    DietaryTags: new[] { "Halal" },
                                    AllergenTags: new[] { "Gluten", "Lactose" },
                                    IsAvailable: true,
                                    ExpectedAvailableAtUtc: null,
                                    Variants: new[]
                                    {
                                        new RuntimeItemVariantDto(
                                            Id: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                                            Name: "Tek Köfte",
                                            Code: "SINGLE",
                                            AbsolutePriceMinorUnits: 25000,
                                            SortOrder: 1,
                                            IsDefault: true,
                                            IsAvailable: true,
                                            ExpectedAvailableAtUtc: null
                                        )
                                    },
                                    ModifierGroups: new[]
                                    {
                                        new RuntimeModifierGroupDto(
                                            Id: Guid.Parse("66666666-6666-6666-6666-666666666666"),
                                            Name: "Sos Seçimi",
                                            MinSelections: 0,
                                            MaxSelections: 2,
                                            SortOrder: 1,
                                            Options: new[]
                                            {
                                                new RuntimeModifierOptionDto(
                                                    Id: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                                                    Name: "Barbekü Sos",
                                                    PriceDeltaMinorUnits: 1500,
                                                    SortOrder: 1,
                                                    IsDefault: false
                                                )
                                            }
                                        )
                                    }
                                )
                            }
                        )
                    }
                )
            }
        );

        var json = JsonSerializer.Serialize(model, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level
        Assert.True(root.TryGetProperty("branchId", out var branchIdProp));
        Assert.Equal("11111111-1111-1111-1111-111111111111", branchIdProp.GetString());
        Assert.True(root.TryGetProperty("currency", out var currencyProp));
        Assert.Equal("TRY", currencyProp.GetString());
        Assert.True(root.TryGetProperty("pricesIncludeTax", out var pitProp));
        Assert.True(pitProp.GetBoolean());
        Assert.True(root.TryGetProperty("defaultTaxRateBps", out var taxProp));
        Assert.Equal(1000, taxProp.GetInt32());
        Assert.True(root.TryGetProperty("isServiceChargeEnabled", out var sceProp));
        Assert.True(sceProp.GetBoolean());
        Assert.True(root.TryGetProperty("serviceChargeRateBps", out var scProp));
        Assert.Equal(500, scProp.GetInt32());
        Assert.True(root.TryGetProperty("pricingPolicyFingerprint", out var ppfProp));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ppfProp.GetString());
        Assert.True(root.TryGetProperty("menus", out var menusProp));
        Assert.Equal(1, menusProp.GetArrayLength());

        // Menu
        var menu = menusProp[0];
        Assert.True(menu.TryGetProperty("id", out _));
        Assert.True(menu.TryGetProperty("name", out _));
        Assert.True(menu.TryGetProperty("slug", out _));
        Assert.True(menu.TryGetProperty("description", out _));
        Assert.True(menu.TryGetProperty("sortOrder", out _));
        Assert.True(menu.TryGetProperty("categories", out var catsProp));

        // Category
        var cat = catsProp[0];
        Assert.True(cat.TryGetProperty("id", out _));
        Assert.True(cat.TryGetProperty("name", out _));
        Assert.True(cat.TryGetProperty("slug", out _));
        Assert.True(cat.TryGetProperty("description", out _));
        Assert.True(cat.TryGetProperty("sortOrder", out _));
        Assert.True(cat.TryGetProperty("items", out var itemsProp));

        // Item
        var item = itemsProp[0];
        Assert.True(item.TryGetProperty("id", out _));
        Assert.True(item.TryGetProperty("name", out _));
        Assert.True(item.TryGetProperty("slug", out _));
        Assert.True(item.TryGetProperty("shortDescription", out _));
        Assert.True(item.TryGetProperty("fullDescription", out _));
        Assert.True(item.TryGetProperty("imageUrl", out _));
        Assert.True(item.TryGetProperty("basePriceMinorUnits", out _));
        Assert.True(item.TryGetProperty("sortOrder", out _));
        Assert.True(item.TryGetProperty("spicyLevel", out _));
        Assert.True(item.TryGetProperty("dietaryTags", out _));
        Assert.True(item.TryGetProperty("allergenTags", out _));
        Assert.True(item.TryGetProperty("isAvailable", out _));
        Assert.True(item.TryGetProperty("expectedAvailableAtUtc", out _));
        Assert.True(item.TryGetProperty("variants", out var variantsProp));
        Assert.True(item.TryGetProperty("modifierGroups", out var modGroupsProp));

        // Variant
        var variant = variantsProp[0];
        Assert.True(variant.TryGetProperty("id", out _));
        Assert.True(variant.TryGetProperty("name", out _));
        Assert.True(variant.TryGetProperty("code", out _));
        Assert.True(variant.TryGetProperty("absolutePriceMinorUnits", out _));
        Assert.True(variant.TryGetProperty("sortOrder", out _));
        Assert.True(variant.TryGetProperty("isDefault", out _));
        Assert.True(variant.TryGetProperty("isAvailable", out _));
        Assert.True(variant.TryGetProperty("expectedAvailableAtUtc", out _));

        // Modifier Group & Option
        var group = modGroupsProp[0];
        Assert.True(group.TryGetProperty("id", out _));
        Assert.True(group.TryGetProperty("name", out _));
        Assert.True(group.TryGetProperty("minSelections", out _));
        Assert.True(group.TryGetProperty("maxSelections", out _));
        Assert.True(group.TryGetProperty("sortOrder", out _));
        Assert.True(group.TryGetProperty("options", out var optionsProp));

        var opt = optionsProp[0];
        Assert.True(opt.TryGetProperty("id", out _));
        Assert.True(opt.TryGetProperty("name", out _));
        Assert.True(opt.TryGetProperty("priceDeltaMinorUnits", out _));
        Assert.True(opt.TryGetProperty("sortOrder", out _));
        Assert.True(opt.TryGetProperty("isDefault", out _));
    }
}
