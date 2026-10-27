using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Api.Orders;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderReadyItemsUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _areaId = DiningAreaId.New();
    private readonly IPermissionRegistry _permissionRegistry = new PermissionRegistry();

    private static (DefaultHttpContext context, Mock<IJwtClaimPrincipalParser> parserMock, Mock<ITenantContext> tenantContextMock, Mock<IStaffOrderService> orderServiceMock)
        CreateTestContext(AuthRole role, Guid tenantId, Guid branchId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/v1/floor/branches/{branchId}/orders/ready-items";

        var scope = role == AuthRole.RestaurantAdmin
            ? AuthorizationScope.ForTenant(new TenantId(tenantId))
            : AuthorizationScope.ForBranch(new TenantId(tenantId), new BranchId(branchId));

        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: role,
            scope: scope,
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Pin,
            securityVersion: 1);

        var parserMock = new Mock<IJwtClaimPrincipalParser>();
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(tenantId);
        tenantContextMock.Setup(t => t.IsAuthenticated).Returns(true);
        tenantContextMock.Setup(t => t.RequireTenantId()).Returns(tenantId);

        var orderServiceMock = new Mock<IStaffOrderService>();

        return (context, parserMock, tenantContextMock, orderServiceMock);
    }

    [Fact]
    public async Task GetBranchReadyItems_AuthorizedWaiter_ReturnsOkWithItems()
    {
        var (context, parserMock, tenantContextMock, orderServiceMock) = CreateTestContext(AuthRole.Waiter, _tenantId.Value, _branchId.Value);

        var readyItems = new List<ReadyOrderItemDto>
        {
            new ReadyOrderItemDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "T-1",
                1,
                "Cheeseburger",
                null,
                2,
                "Ready",
                DateTimeOffset.UtcNow,
                null)
        };

        orderServiceMock.Setup(s => s.ListReadyItemsAsync(
            _tenantId,
            _branchId,
            null,
            null,
            50,
            0,
            It.IsAny<AuthenticatedPrincipal>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(readyItems);

        // Act
        var result = await StaffOrderEndpoints.GetBranchReadyItemsAsync(
            _branchId.Value,
            null,
            null,
            50,
            0,
            context,
            parserMock.Object,
            tenantContextMock.Object,
            orderServiceMock.Object,
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Ok<IReadOnlyList<ReadyOrderItemDto>>>(result);
        Assert.NotNull(okResult.Value);
        Assert.Single(okResult.Value);
        Assert.Equal("T-1", okResult.Value[0].TableNumber);
        Assert.Equal("Cheeseburger", okResult.Value[0].ItemName);
    }

    [Theory]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    public async Task GetBranchReadyItems_UnauthorizedRoles_ReturnsForbidden(AuthRole unauthorizedRole)
    {
        var (context, parserMock, tenantContextMock, orderServiceMock) = CreateTestContext(unauthorizedRole, _tenantId.Value, _branchId.Value);

        orderServiceMock.Setup(s => s.ListReadyItemsAsync(
            _tenantId,
            _branchId,
            null,
            null,
            50,
            0,
            It.IsAny<AuthenticatedPrincipal>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("Missing required permission"));

        // Act
        var result = await StaffOrderEndpoints.GetBranchReadyItemsAsync(
            _branchId.Value,
            null,
            null,
            50,
            0,
            context,
            parserMock.Object,
            tenantContextMock.Object,
            orderServiceMock.Object,
            CancellationToken.None);

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
        var problem = (ProblemHttpResult)result;
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task GetBranchReadyItems_CrossBranchAccess_ThrowsAndReturnsForbidden()
    {
        var foreignBranchId = Guid.NewGuid();
        var (context, parserMock, tenantContextMock, orderServiceMock) = CreateTestContext(AuthRole.Waiter, _tenantId.Value, _branchId.Value);

        orderServiceMock.Setup(s => s.ListReadyItemsAsync(
            _tenantId,
            new BranchId(foreignBranchId),
            null,
            null,
            50,
            0,
            It.IsAny<AuthenticatedPrincipal>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("Actor is not authorized for this branch."));

        // Act - Actor tries to query foreignBranchId
        var result = await StaffOrderEndpoints.GetBranchReadyItemsAsync(
            foreignBranchId,
            null,
            null,
            50,
            0,
            context,
            parserMock.Object,
            tenantContextMock.Object,
            orderServiceMock.Object,
            CancellationToken.None);

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
        var problem = (ProblemHttpResult)result;
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task StaffOrderService_ListReadyItemsAsync_QueriesOnlyReadyItemsWithTableNumber()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"ready_items_test_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var dbContext = new RestaurantOrderDbContext(options, new TenantContext(_tenantId.Value, isAuthenticated: true));

        var table1 = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-10", "Table 10", 4);
        var table2 = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-20", "Table 20", 2);
        dbContext.RestaurantTables.AddRange(table1, table2);

        var session1 = DiningSession.Open(_tenantId, _branchId, table1, 2);
        var session2 = DiningSession.Open(_tenantId, _branchId, table2, 2);
        dbContext.DiningSessions.AddRange(session1, session2);

        var menuItemId = new MenuItemId(Guid.NewGuid());

        var order1Id = OrderId.New();
        var item1 = OrderItem.Create(
            order1Id,
            _tenantId,
            menuItemId,
            null,
            "Soup",
            null,
            1,
            5000L,
            1000);
        var item2 = OrderItem.Create(
            order1Id,
            _tenantId,
            menuItemId,
            null,
            "Soup 2",
            null,
            1,
            5000L,
            1000);

        var order1 = Order.CreateSubmitted(
            _tenantId,
            _branchId,
            session1.Id,
            table1.Id,
            1,
            OrderSource.Customer,
            Currency.Default,
            "idemp-ord-1",
            new[] { item1, item2 });

        item1.StartPreparation();
        item1.MarkReady(); // Status = Ready

        item2.StartPreparation(); // Status = InPreparation (should NOT be returned)

        var order2Id = OrderId.New();
        var item3 = OrderItem.Create(
            order2Id,
            _tenantId,
            menuItemId,
            null,
            "Soup 3",
            null,
            2,
            5000L,
            1000);

        var order2 = Order.CreateSubmitted(
            _tenantId,
            _branchId,
            session2.Id,
            table2.Id,
            2,
            OrderSource.Customer,
            Currency.Default,
            "idemp-ord-2",
            new[] { item3 });

        item3.StartPreparation();
        item3.MarkReady();
        item3.MarkServed(); // Status = Served (should NOT be returned)

        dbContext.Orders.AddRange(order1, order2);
        await dbContext.SaveChangesAsync();

        var service = new StaffOrderService(
            dbContext,
            new Mock<IOrderPricingEngine>().Object,
            _permissionRegistry,
            new Mock<ISupervisorPinService>().Object);

        var waiterPrincipal = AuthenticatedPrincipal.CreateStaff(
            Guid.NewGuid(),
            AuthRole.Waiter,
            AuthorizationScope.ForBranch(_tenantId, _branchId),
            Guid.NewGuid(),
            AuthenticationMethod.Pin,
            1);

        // Act
        var result = await service.ListReadyItemsAsync(
            _tenantId,
            _branchId,
            sessionFilter: null,
            tableFilter: null,
            limit: 10,
            offset: 0,
            actor: waiterPrincipal);

        // Assert
        Assert.Single(result);
        var readyItem = result[0];
        Assert.Equal("Soup", readyItem.ItemName);
        Assert.Equal("T-10", readyItem.TableNumber);
        Assert.Equal(table1.Id.Value, readyItem.TableId);
    }
}
