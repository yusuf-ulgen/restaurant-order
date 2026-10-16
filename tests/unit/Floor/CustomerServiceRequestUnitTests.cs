using Microsoft.AspNetCore.Http;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class CustomerServiceRequestUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningSessionId _sessionId = DiningSessionId.New();

    private AuthenticatedPrincipal CreateCustomerActor(Guid? tenantId = null, Guid? branchId = null, Guid? sessionId = null)
    {
        return AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId ?? _sessionId.Value,
            tenantId: new TenantId(tenantId ?? _tenantId.Value),
            branchId: new BranchId(branchId ?? _branchId.Value));
    }

    private AuthenticatedPrincipal CreateStaffActor()
    {
        return AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.Waiter,
            scope: AuthorizationScope.ForBranch(_tenantId, _branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);
    }

    [Fact]
    public void StaffPrincipal_IsNotCustomer()
    {
        var staff = CreateStaffActor();
        Assert.Equal(AuthRole.Waiter, staff.Role);
        Assert.False(staff.IsCustomer);
    }

    [Fact]
    public void CustomerScope_MismatchedTenant_EvaluatesDifferently()
    {
        var foreignTenant = TenantId.New();
        var customer = CreateCustomerActor(tenantId: foreignTenant.Value);

        Assert.Equal(foreignTenant, customer.TenantId);
        Assert.NotEqual(_tenantId, customer.TenantId);
    }

    [Fact]
    public void CustomerScope_MismatchedSession_EvaluatesDifferently()
    {
        var foreignSession = Guid.NewGuid();
        var customer = CreateCustomerActor(sessionId: foreignSession);

        Assert.Equal(foreignSession, customer.TableSessionId);
        Assert.NotEqual(_sessionId.Value, customer.TableSessionId);
    }

    [Fact]
    public void ServiceRequestDto_MapsAllContractProperties()
    {
        var id = Guid.NewGuid();
        var tId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var sId = Guid.NewGuid();
        var tblId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dto = new ServiceRequestDto(
            Id: id,
            TenantId: tId,
            BranchId: bId,
            DiningSessionId: sId,
            TableId: tblId,
            RequestType: "Water",
            Status: "Open",
            Note: "Soğuk su lütfen",
            IdempotencyKey: "idem-123",
            CreatedAtUtc: now,
            AcknowledgedAtUtc: null,
            CompletedAtUtc: null,
            ConcurrencyToken: token);

        Assert.Equal(id, dto.Id);
        Assert.Equal("Water", dto.RequestType);
        Assert.Equal("Open", dto.Status);
        Assert.Equal("Soğuk su lütfen", dto.Note);
        Assert.Equal("idem-123", dto.IdempotencyKey);
        Assert.Equal(token, dto.ConcurrencyToken);
    }
}
