using System.Text.Json;
using RestaurantOrder.Application.Orders;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderContractDriftTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StaffOrderResponseDto_SerializesToExpectedCamelCaseJsonStructure()
    {
        var response = new StaffOrderResponseDto(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BranchId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DiningSessionId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TableId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            RoundNumber: 1,
            OrderSource: "Staff",
            Status: "Submitted",
            Currency: "TRY",
            SubtotalMinorUnits: 5000,
            TaxMinorUnits: 500,
            ServiceChargeMinorUnits: 250,
            TotalMinorUnits: 5750,
            IdempotencyKey: "staff-key-123",
            ConcurrencyToken: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            CreatedAtUtc: new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
            SubmittedAtUtc: new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc: null,
            CancellationReason: null,
            CancelledByActorId: null,
            Items: new List<StaffOrderItemDto>
            {
                new(
                    Id: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    MenuItemId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    ItemVariantId: null,
                    ItemName: "Pizza",
                    VariantName: null,
                    Quantity: 1,
                    CustomerNote: "Acılı",
                    PreparationStationId: null,
                    UnitPriceMinorUnits: 5000,
                    ModifierTotalMinorUnits: 0,
                    LineTotalMinorUnits: 5000,
                    TaxRateBasisPoints: 1000,
                    Status: "Submitted",
                    CreatedAtUtc: new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc: null,
                    Modifiers: new List<StaffOrderItemModifierDto>()
                )
            });

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("tenantId", out _));
        Assert.True(root.TryGetProperty("branchId", out _));
        Assert.True(root.TryGetProperty("diningSessionId", out _));
        Assert.True(root.TryGetProperty("tableId", out _));
        Assert.True(root.TryGetProperty("roundNumber", out _));
        Assert.True(root.TryGetProperty("orderSource", out _));
        Assert.True(root.TryGetProperty("status", out _));
        Assert.True(root.TryGetProperty("concurrencyToken", out _));
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(1, items.GetArrayLength());
    }

    [Fact]
    public void CancelOrderItemRequest_DeserializesFromExpectedCamelCaseJson()
    {
        var json = """
        {
            "reason": "Müşteri isteği",
            "supervisorPin": "1234",
            "supervisorUserId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "concurrencyToken": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
        }
        """;

        var request = JsonSerializer.Deserialize<CancelOrderItemRequest>(json, JsonOptions);
        Assert.NotNull(request);
        Assert.Equal("Müşteri isteği", request.Reason);
        Assert.Equal("1234", request.SupervisorPin);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), request.SupervisorUserId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), request.ConcurrencyToken);
    }
}
