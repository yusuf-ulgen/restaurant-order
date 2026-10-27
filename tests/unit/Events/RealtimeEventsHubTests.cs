using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class RealtimeEventsHubTests
{
    private readonly Mock<ICustomerSessionValidator> _sessionValidatorMock = new();
    private readonly Mock<ITokenRevocationValidator> _tokenValidatorMock = new();
    private readonly Mock<IBranchScopeValidator> _branchScopeValidatorMock = new();
    private readonly RealtimeConnectionTracker _tracker = new();
    private readonly Mock<HubCallerContext> _contextMock = new();
    private readonly Mock<IGroupManager> _groupsMock = new();

    private EventsHub CreateHub(ClaimsPrincipal? user, string connectionId = "conn-1")
    {
        _contextMock.Setup(c => c.User).Returns(user);
        _contextMock.Setup(c => c.ConnectionId).Returns(connectionId);
        _contextMock.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var hub = new EventsHub(
            _sessionValidatorMock.Object,
            _tokenValidatorMock.Object,
            _branchScopeValidatorMock.Object,
            _tracker,
            NullLogger<EventsHub>.Instance,
            Mock.Of<IRealtimeConnectionAuthorizer>(a => a.IsAuthorizedAsync(It.IsAny<TrackedConnection>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
        {
            Context = _contextMock.Object,
            Groups = _groupsMock.Object
        };

        return hub;
    }

    [Fact]
    public async Task OnConnectedAsync_UnauthenticatedUser_AbortsConnection()
    {
        var hub = CreateHub(user: null);

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Once);
        Assert.Empty(_tracker.GetConnectionsForSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task OnConnectedAsync_MissingScopeClaims_AbortsConnection()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Customer")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var hub = CreateHub(principal);

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_CustomerWithInactiveSession_AbortsConnection()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var hub = CreateHub(principal);

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_CustomerWithActiveSession_JoinsGroupsAndTracks()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var hub = CreateHub(principal, "cust-conn-1");

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Never);

        var expectedSessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{sessionId}";
        var expectedCatalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";

        _groupsMock.Verify(g => g.AddToGroupAsync("cust-conn-1", expectedSessionGroup, It.IsAny<CancellationToken>()), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("cust-conn-1", expectedCatalogGroup, It.IsAny<CancellationToken>()), Times.Once);

        var tracked = _tracker.GetConnectionsForSession(tenantId, branchId, sessionId);
        Assert.Single(tracked);
        Assert.Equal("cust-conn-1", tracked[0].ConnectionId);
        hub.Dispose();
        tracked[0].Abort!();
        _contextMock.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_StaffWithActiveToken_JoinsFloorAndStationGroups()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var staffSessionId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Kitchen"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.Subject, staffUserId.ToString()),
            new Claim(JwtClaimNames.SessionId, staffSessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _tokenValidatorMock
            .Setup(t => t.ValidateTokenActiveAsync(staffSessionId, staffUserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var hub = CreateHub(principal, "staff-conn-1");

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Never);

        var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
        var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";
        var stationGroup = $"tenant:{tenantId}:branch:{branchId}:station:kitchen";

        // Kitchen receives queue and catalog only; must not receive floor noise
        _groupsMock.Verify(g => g.AddToGroupAsync("staff-conn-1", floorGroup, It.IsAny<CancellationToken>()), Times.Never);
        _groupsMock.Verify(g => g.AddToGroupAsync("staff-conn-1", catalogGroup, It.IsAny<CancellationToken>()), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("staff-conn-1", stationGroup, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_StaffWithRevokedToken_AbortsConnection()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var staffSessionId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.Subject, staffUserId.ToString()),
            new Claim(JwtClaimNames.SessionId, staffSessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _tokenValidatorMock
            .Setup(t => t.ValidateTokenActiveAsync(staffSessionId, staffUserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var hub = CreateHub(principal, "staff-conn-revoked");

        await hub.OnConnectedAsync();

        _contextMock.Verify(c => c.Abort(), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnDisconnectedAsync_UntracksConnection()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _tracker.Track(new TrackedConnection(
            ConnectionId: "conn-to-disconnect",
            TenantId: tenantId,
            BranchId: branchId,
            Role: "Customer",
            DiningSessionId: sessionId,
            UserId: null));

        Assert.Single(_tracker.GetConnectionsForSession(tenantId, branchId, sessionId));

        var hub = CreateHub(user: null, "conn-to-disconnect");
        await hub.OnDisconnectedAsync(null);

        Assert.Empty(_tracker.GetConnectionsForSession(tenantId, branchId, sessionId));
    }
}
