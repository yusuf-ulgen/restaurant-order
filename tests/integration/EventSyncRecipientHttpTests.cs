using System.Text.Json;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Events;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class EventSyncRecipientHttpTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Theory]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    public async Task StationHttpSync_ScansFilteredPagesAndRecoversPastCorruptPayload(AuthRole role)
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var scope = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(scope, role);
        var types = new[] { "table.service_requested", "order.created", "order.item_status_changed",
            "menu.item_availability_changed", "catalog.policy_invalidated" };
        await using (var db = h.Admin())
        {
            foreach (var type in types)
                db.OperationalOutbox.Add(OperationalOutboxMessage.Create(scope.Tenant, scope.Branch, Guid.NewGuid().ToString(),
                    type, "{}", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            db.OperationalOutbox.Add(OperationalOutboxMessage.Create(scope.Tenant, scope.Branch, "corrupt", "order.created",
                "[]", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        using var client = Phase9RealtimeHarness.Client(h.B, staff.Token);
        long cursor = 0;
        var events = new List<string>(); var resync = false; var finished = false; var emptyScanned = false;
        for (var page = 0; page < 10; page++)
        {
            using var response = await client.GetAsync($"/api/v1/events/sync?since_sequence={cursor}&limit=1");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            resync |= response.Headers.TryGetValues("X-Resync-Required", out var values) && values.Contains("true");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var next = root.GetProperty("latest_sequence_number").GetInt64();
            var returned = root.GetProperty("events").EnumerateArray().ToArray();
            emptyScanned |= returned.Length == 0 && next > cursor;
            events.AddRange(returned.Select(e => e.GetProperty("event_type").GetString()!));
            Assert.True(next > cursor);
            cursor = next;
            if (!root.GetProperty("has_more").GetBoolean()) { finished = true; break; }
        }
        Assert.True(finished); Assert.True(emptyScanned); Assert.True(resync);
        Assert.Equal(types.Skip(1).Order(), events.Order());
        using var after = await client.GetAsync($"/api/v1/events/sync?since_sequence={cursor}&limit=1");
        Assert.True(after.IsSuccessStatusCode);
        Assert.False(after.Headers.Contains("X-Resync-Required"));
        using var final = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.Empty(final.RootElement.GetProperty("events").EnumerateArray());
    }
}
