using System.Text.Json;
using RestaurantOrder.Application.Orders;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

/// <summary>
/// Verifies that backend CustomerOrderResponseDto, CreateCustomerOrderRequest, and related models
/// maintain exact camelCase contract alignment with @restaurant-order/contracts definitions without drift.
/// </summary>
public class CustomerOrderContractDriftTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CustomerOrderResponseDto_SerializesToExpectedCamelCaseJsonStructure()
    {
        var response = new CustomerOrderResponseDto(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BranchId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DiningSessionId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TableId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            RoundNumber: 1,
            OrderSource: "Customer",
            Status: "Submitted",
            Currency: "TRY",
            SubtotalMinorUnits: 5000,
            TaxMinorUnits: 500,
            ServiceChargeMinorUnits: 250,
            TotalMinorUnits: 5750,
            IdempotencyKey: "ord-test-key-123",
            ConcurrencyToken: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            CreatedAtUtc: new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
            SubmittedAtUtc: new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
            Items: new List<CustomerOrderItemDto>
            {
                new(
                    Id: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    MenuItemId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    ItemVariantId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    ItemName: "Klasik Burger",
                    VariantName: "Duble",
                    Quantity: 2,
                    CustomerNote: "Acısız lütfen",
                    UnitPriceMinorUnits: 2500,
                    ModifierTotalMinorUnits: 300,
                    LineTotalMinorUnits: 5600,
                    TaxRateBasisPoints: 1000,
                    Status: "Submitted",
                    Modifiers: new List<CustomerOrderItemModifierDto>
                    {
                        new(
                            Id: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            ModifierGroupId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            ModifierGroupName: "Peynir Seçimi",
                            ModifierOptionId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                            ModifierOptionName: "Ekstra Cheddar",
                            PriceDeltaMinorUnits: 300
                        )
                    }
                )
            }
        );

        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level properties
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("id").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("tenantId").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333333", root.GetProperty("branchId").GetString());
        Assert.Equal("44444444-4444-4444-4444-444444444444", root.GetProperty("diningSessionId").GetString());
        Assert.Equal("55555555-5555-5555-5555-555555555555", root.GetProperty("tableId").GetString());
        Assert.Equal(1, root.GetProperty("roundNumber").GetInt32());
        Assert.Equal("Customer", root.GetProperty("orderSource").GetString());
        Assert.Equal("Submitted", root.GetProperty("status").GetString());
        Assert.Equal("TRY", root.GetProperty("currency").GetString());
        Assert.Equal(5000, root.GetProperty("subtotalMinorUnits").GetInt64());
        Assert.Equal(5000 + 500 + 250, root.GetProperty("totalMinorUnits").GetInt64());
        Assert.Equal("ord-test-key-123", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("66666666-6666-6666-6666-666666666666", root.GetProperty("concurrencyToken").GetString());

        // Items array
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var item0 = items[0];
        Assert.Equal("77777777-7777-7777-7777-777777777777", item0.GetProperty("id").GetString());
        Assert.Equal("88888888-8888-8888-8888-888888888888", item0.GetProperty("menuItemId").GetString());
        Assert.Equal("Klasik Burger", item0.GetProperty("itemName").GetString());
        Assert.Equal("Duble", item0.GetProperty("variantName").GetString());
        Assert.Equal(2, item0.GetProperty("quantity").GetInt32());
        Assert.Equal("Acısız lütfen", item0.GetProperty("customerNote").GetString());
        Assert.Equal(2500, item0.GetProperty("unitPriceMinorUnits").GetInt64());
        Assert.Equal(5600, item0.GetProperty("lineTotalMinorUnits").GetInt64());

        // Modifiers array
        var mods = item0.GetProperty("modifiers");
        Assert.Equal(1, mods.GetArrayLength());
        var mod0 = mods[0];
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", mod0.GetProperty("id").GetString());
        Assert.Equal("Peynir Seçimi", mod0.GetProperty("modifierGroupName").GetString());
        Assert.Equal("Ekstra Cheddar", mod0.GetProperty("modifierOptionName").GetString());
        Assert.Equal(300, mod0.GetProperty("priceDeltaMinorUnits").GetInt64());
    }

    [Fact]
    public void CreateCustomerOrderRequest_DeserializesFromExpectedCamelCaseJson()
    {
        var json = @"
        {
            ""items"": [
                {
                    ""menuItemId"": ""88888888-8888-8888-8888-888888888888"",
                    ""itemVariantId"": null,
                    ""quantity"": 1,
                    ""customerNote"": ""Soğansız"",
                    ""selectedModifiers"": [
                        {
                            ""modifierGroupId"": ""bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"",
                            ""modifierOptionId"": ""cccccccc-cccc-cccc-cccc-cccccccccccc""
                        }
                    ],
                    ""clientExpectedUnitPriceMinorUnits"": 2500
                }
            ],
            ""clientPricingPolicyFingerprint"": ""0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9"",
            ""clientExpectedTotalMinorUnits"": 2500
        }";

        var request = JsonSerializer.Deserialize<CreateCustomerOrderRequest>(json, JsonOptions);
        Assert.NotNull(request);
        Assert.Single(request.Items);
        Assert.Equal("0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9", request.ClientPricingPolicyFingerprint);
        Assert.Equal(2500, request.ClientExpectedTotalMinorUnits);

        var item = request.Items[0];
        Assert.Equal(Guid.Parse("88888888-8888-8888-8888-888888888888"), item.MenuItemId);
        Assert.Null(item.ItemVariantId);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("Soğansız", item.CustomerNote);
        Assert.NotNull(item.SelectedModifiers);
        Assert.Single(item.SelectedModifiers);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), item.SelectedModifiers[0].ModifierGroupId);
        Assert.Equal(2500, item.ClientExpectedUnitPriceMinorUnits);
    }
}
