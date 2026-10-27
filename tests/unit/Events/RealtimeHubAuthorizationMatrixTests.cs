using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class RealtimeHubAuthorizationMatrixTests
{
    private sealed class HttpFeature(HttpContext context) : IHttpContextFeature
    { public HttpContext? HttpContext { get; set; } = context; }

    private sealed class Harness
    {
        public readonly Guid Tenant = Guid.NewGuid(), Branch = Guid.NewGuid(), Session = Guid.NewGuid();
        public readonly Mock<HubCallerContext> Context = new();
        public readonly Mock<IGroupManager> Groups = new();
        public readonly Mock<ITokenRevocationValidator> Tokens = new();
        public readonly Mock<ICustomerSessionValidator> Customers = new();
        public readonly Mock<IBranchScopeValidator> Branches = new();
        public readonly Mock<IRealtimeConnectionAuthorizer> Authority = new();
        public readonly RealtimeConnectionTracker Tracker = new();
        public readonly DefaultHttpContext Http = new();

        public Harness()
        {
            Context.Setup(c => c.ConnectionId).Returns("connection");
            var features = new FeatureCollection(); features.Set<IHttpContextFeature>(new HttpFeature(Http));
            Context.Setup(c => c.Features).Returns(features);
            Tokens.Setup(v => v.ValidateTokenActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Customers.Setup(v => v.ValidateSessionActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Branches.Setup(v => v.ValidateBranchBelongsToTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Authority.Setup(v => v.IsAuthorizedAsync(It.IsAny<TrackedConnection>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        public EventsHub Create(string role, string? missing = null, bool rawRoleClaim = false)
        {
            var claims = new Dictionary<string, string> {
                ["tenant_id"] = Tenant.ToString(), ["branch_id"] = Branch.ToString(),
                [rawRoleClaim ? "role" : ClaimTypes.Role] = role,
                ["sid"] = Session.ToString(), ["sub"] = Guid.NewGuid().ToString(), ["security_version"] = "1",
                ["table_session_id"] = Session.ToString(), ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString()
            };
            if (missing != null) claims.Remove(missing);
            Context.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)), "synthetic")));
            return new EventsHub(Customers.Object, Tokens.Object, Branches.Object, Tracker, NullLogger<EventsHub>.Instance, Authority.Object)
            { Context = Context.Object, Groups = Groups.Object };
        }
    }

    [Theory]
    [InlineData("Kitchen", "station:kitchen", false)]
    [InlineData("Bar", "station:bar", false)]
    [InlineData("Waiter", "floor", false)]
    [InlineData("Cashier", "floor", true)]
    [InlineData("BranchManager", "floor", false)]
    [InlineData("branchmanager", "floor", true)]
    [InlineData("branch_manager", "floor", true)]
    [InlineData("RestaurantAdmin", "floor", false)]
    [InlineData("restaurant_admin", "floor", true)]
    [InlineData("SuperAdmin", "floor", false)]
    [InlineData("super_admin", "floor", true)]
    public async Task SupportedStaffRole_ReceivesOnlyServerDeterminedGroups(string role, string expected, bool rawRole)
    {
        var h = new Harness();
        h.Http.Request.QueryString = new QueryString($"?branchId={h.Branch}");
        using var hub = h.Create(role, rawRoleClaim: rawRole);
        await hub.OnConnectedAsync();
        var connection = Assert.Single(h.Tracker.GetAllConnections());
        Assert.Contains($"tenant:{h.Tenant}:branch:{h.Branch}:{expected}", connection.SubscribedGroups!);
        if (role is "Kitchen" or "Bar") Assert.DoesNotContain($"tenant:{h.Tenant}:branch:{h.Branch}:floor", connection.SubscribedGroups!);
        if (role is "Waiter" or "Cashier") Assert.Equal(2, connection.SubscribedGroups!.Count);
    }

    [Theory]
    [InlineData("tenant_id")]
    [InlineData("branch_id")]
    [InlineData("sid")]
    [InlineData("sub")]
    [InlineData("security_version")]
    public async Task MissingSecurityClaim_AbortsBeforeJoiningAnyGroup(string missing)
    {
        var h = new Harness(); using var hub = h.Create("Waiter", missing);
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(h.Tracker.GetAllConnections());
        h.Groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminMissingOrForeignBranch_IsRejected(bool foreign)
    {
        var h = new Harness();
        if (foreign) h.Http.Request.QueryString = new QueryString($"?branchId={h.Branch}");
        h.Branches.Setup(v => v.ValidateBranchBelongsToTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        using var hub = h.Create("RestaurantAdmin");
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(h.Tracker.GetAllConnections());
    }

    [Theory]
    [InlineData("Waiter")]
    [InlineData("Customer")]
    public async Task AuthoritativeRevocation_AbortsEvenIfCachedAdmissionAllowsConnection(string role)
    {
        var h = new Harness();
        h.Authority.Setup(a => a.IsAuthorizedAsync(It.IsAny<TrackedConnection>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        using var hub = h.Create(role, rawRoleClaim: true);
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(h.Tracker.GetAllConnections());
    }

    [Theory]
    [InlineData("tenant_id")]
    [InlineData("branch_id")]
    [InlineData("table_session_id")]
    [InlineData(ClaimTypes.Role)]
    public async Task CustomerMissingScope_CannotJoinPrivateGroups(string missing)
    {
        var h = new Harness(); using var hub = h.Create("Customer", missing);
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(h.Tracker.GetAllConnections());
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Waiter")]
    public async Task CachedAdmissionRejectsRevokedIdentity_BeforeAuthoritativeCheck(string role)
    {
        var h = new Harness();
        h.Customers.Setup(v => v.ValidateSessionActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        h.Tokens.Setup(v => v.ValidateTokenActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        using var hub = h.Create(role);
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        h.Authority.Verify(a => a.IsAuthorizedAsync(It.IsAny<TrackedConnection>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(h.Tracker.GetAllConnections());
    }

    [Fact]
    public async Task MissingPrincipal_IsAborted()
    {
        var h = new Harness(); using var hub = h.Create("Waiter");
        h.Context.Setup(c => c.User).Returns((ClaimsPrincipal?)null);
        await hub.OnConnectedAsync();
        h.Context.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(h.Tracker.GetAllConnections());
    }
}
