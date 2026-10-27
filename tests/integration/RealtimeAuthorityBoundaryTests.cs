using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Api.Realtime;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RealtimeAuthorityBoundaryTests(TestcontainersFixture fixture)
    : IClassFixture<TestcontainersFixture>
{
    [Fact]
    public async Task AuthoritativeChecks_RejectIncompleteExpiredForeignAndForgedRoleBindings()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var customer = await h.SeedCustomerAsync(branch);
        var authority = h.A.Services.GetRequiredService<IRealtimeConnectionAuthorizer>();
        var valid = new TrackedConnection("synthetic", branch.Tenant.Value, branch.Branch.Value, "Waiter", null,
            staff.User.Value, staff.Session, 1, DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.True(await authority.IsAuthorizedAsync(valid));
        Assert.False(await authority.IsAuthorizedAsync(valid with { TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { StaffSessionId = null }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { UserId = null }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { SecurityVersion = null }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { SecurityVersion = 99 }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { Role = "unknown-role" }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { Role = "Kitchen" }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { BranchId = Guid.NewGuid() }));
        Assert.False(await authority.IsAuthorizedAsync(valid with { TenantId = Guid.NewGuid() }));
        var customerBinding = valid with { Role = "Customer", DiningSessionId = customer, UserId = null, StaffSessionId = null, SecurityVersion = null };
        Assert.True(await authority.IsAuthorizedAsync(customerBinding));
        Assert.False(await authority.IsAuthorizedAsync(customerBinding with { DiningSessionId = Guid.NewGuid() }));
        Assert.False(await authority.IsAuthorizedAsync(customerBinding with { BranchId = Guid.NewGuid() }));
    }
}
