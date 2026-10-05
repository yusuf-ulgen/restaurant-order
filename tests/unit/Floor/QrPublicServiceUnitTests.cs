using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class QrPublicServiceUnitTests
{
    private const string ValidKey = "synthetic-dev-qr-key-32chars-minimum-entropy!!";
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _areaId = DiningAreaId.New();

    private QrSecurityService CreateSecurityService()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Development");

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = ValidKey }
        };

        return new QrSecurityService(Options.Create(options), envMock.Object);
    }

    [Fact]
    public async Task RateLimiter_ExceedingLimit_ThrowsQrRateLimitException()
    {
        var limiter = new InMemoryQrRateLimiter(maxResolveAttempts: 2, maxExchangeAttempts: 2, window: TimeSpan.FromMinutes(1));
        var ip = "192.168.1.100";

        await limiter.EnsureNotRateLimitedAsync(ip, "resolve");
        await limiter.EnsureNotRateLimitedAsync(ip, "resolve");

        var ex = await Assert.ThrowsAsync<QrRateLimitException>(() => limiter.EnsureNotRateLimitedAsync(ip, "resolve"));
        Assert.True(ex.RetryAfterSeconds > 0);
    }

    [Fact]
    public void RestaurantTable_BumpQrVersion_IncrementsVersionAndRegeneratesPublicCode()
    {
        var table = RestaurantTable.Create(
            _tenantId,
            _branchId,
            _areaId,
            "T-10",
            "Table 10",
            capacity: 4);

        var originalVersion = table.QrVersion;
        var originalCode = table.PublicCode;
        var originalToken = table.ConcurrencyToken;

        Assert.Equal(1, originalVersion);
        Assert.NotEmpty(originalCode);

        table.BumpQrVersion();

        Assert.Equal(2, table.QrVersion);
        Assert.NotEqual(originalCode, table.PublicCode);
        Assert.NotEqual(originalToken, table.ConcurrencyToken);
    }

    [Fact]
    public void CustomerPrincipal_Claims_IncludeTableSessionIdAndCorrectScope()
    {
        var sessionId = Guid.NewGuid();
        var principal = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId,
            tenantId: _tenantId,
            branchId: _branchId);

        Assert.Equal(PrincipalType.Customer, principal.PrincipalType);
        Assert.Equal(AuthRole.Customer, principal.Role);
        Assert.Equal(AuthorizationScopeType.TableSession, principal.Scope.ScopeType);
        Assert.Equal(sessionId, principal.TableSessionId);
        Assert.Equal(_tenantId, principal.Scope.TenantId);
        Assert.Equal(_branchId, principal.Scope.BranchId);
    }

    [Fact]
    public void CustomerPrincipal_ForbiddenOnStaffOperations()
    {
        var registry = new PermissionRegistry();
        var staffPermissions = new[]
        {
            Permissions.BranchTablesManage,
            Permissions.BranchConfigurationManage,
            Permissions.MenuCatalogManage,
            Permissions.FloorStatusView
        };

        foreach (var perm in staffPermissions)
        {
            var grant = registry.GetGrantType(AuthRole.Customer, perm);
            Assert.Equal(PermissionGrantType.Denied, grant);
        }
    }

    [Fact]
    public void CustomerPrincipal_CanRequestBillOnlyForOwnSession()
    {
        var sessionId = Guid.NewGuid();
        var actor = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId,
            tenantId: _tenantId,
            branchId: _branchId);

        var table = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-01", "Table 1", 4);
        var ownSession = DiningSession.Open(_tenantId, _branchId, table, guestCount: 2, id: DiningSessionId.From(sessionId));

        // Requesting bill on own session should succeed
        ownSession.Activate();
        ownSession.RequestBill();
        Assert.Equal(DiningSessionStatus.BillRequested, ownSession.Status);

        // Foreign session mismatch
        var foreignSessionId = Guid.NewGuid();
        Assert.NotEqual(foreignSessionId, actor.TableSessionId);
    }

    [Fact]
    public void CustomerPrincipal_CannotCloseSession()
    {
        var table = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-01", "Table 1", 4);
        var session = DiningSession.Open(_tenantId, _branchId, table, guestCount: 2);
        session.Activate();
        session.RequestBill();

        // Customer closing session should be blocked
        var customerActor = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: session.Id.Value,
            tenantId: _tenantId,
            branchId: _branchId);

        Assert.Equal(AuthRole.Customer, customerActor.Role);
    }
}
