using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorStatusDerivedStateUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _areaId = DiningAreaId.New();
    private readonly IPermissionRegistry _permissionRegistry = new PermissionRegistry();

    private (RestaurantOrderDbContext dbContext, FloorService service) CreateService()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"floor_status_test_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var dbContext = new RestaurantOrderDbContext(options, new TenantContext(_tenantId.Value, isAuthenticated: true));
        var service = new FloorService(dbContext, NullLogger<FloorService>.Instance, _permissionRegistry);

        return (dbContext, service);
    }

    [Theory]
    [InlineData(false, null, 0, 0, "Inactive")]
    [InlineData(true, null, 0, 0, "Available")]
    [InlineData(true, DiningSessionStatus.Active, 0, 0, "Seated")]
    [InlineData(true, DiningSessionStatus.BillRequested, 0, 0, "BillRequested")]
    [InlineData(true, DiningSessionStatus.Active, 2, 1, "ReadyToServe")]
    [InlineData(true, DiningSessionStatus.Active, 1, 0, "OrderPending")]
    public void DeriveTableStatus_AllScenarios_ReturnsExpectedDeterministicState(
        bool isTableActive,
        DiningSessionStatus? sessionStatus,
        int activeOrdersCount,
        int readyItemsCount,
        string expectedStatus)
    {
        var derived = FloorService.DeriveTableStatus(
            isTableActive: isTableActive,
            sessionStatus: sessionStatus,
            activeOrdersCount: activeOrdersCount,
            readyItemsCount: readyItemsCount);

        Assert.Equal(expectedStatus, derived);
    }

    [Fact]
    public async Task GetBranchFloorStatusAsync_AggregatesSessionsRequestsAndOrdersAccurately()
    {
        var (dbContext, service) = CreateService();

        var table1 = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-1", "Table 1", 4);
        var table2 = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-2", "Table 2", 2);
        var table3 = RestaurantTable.Create(_tenantId, _branchId, _areaId, "T-3", "Table 3", 6);
        dbContext.RestaurantTables.AddRange(table1, table2, table3);

        // Session on Table 1: Seated with 1 active service request
        var session1 = DiningSession.Open(_tenantId, _branchId, table1, 2);
        session1.Activate();
        dbContext.DiningSessions.Add(session1);

        var sr1 = ServiceRequest.Create(_tenantId, _branchId, session1.Id, table1.Id, ServiceRequestType.Water, null, "sr-1");
        dbContext.ServiceRequests.Add(sr1);

        // Session on Table 2: Bill requested
        var session2 = DiningSession.Open(_tenantId, _branchId, table2, 2);
        session2.Activate();
        session2.RequestBill();
        dbContext.DiningSessions.Add(session2);

        // Table 3: No session (Available)
        await dbContext.SaveChangesAsync();

        var waiterActor = AuthenticatedPrincipal.CreateStaff(
            Guid.NewGuid(),
            AuthRole.Waiter,
            AuthorizationScope.ForBranch(_tenantId, _branchId),
            Guid.NewGuid(),
            AuthenticationMethod.Pin,
            1);

        // Act
        var statusResult = await service.GetBranchFloorStatusAsync(_tenantId, _branchId, waiterActor);

        // Assert
        Assert.NotNull(statusResult);
        Assert.Equal(3, statusResult.Tables.Count);

        var statusTable1 = statusResult.Tables.First(s => s.Table.Id == table1.Id.Value);
        Assert.Equal("Seated", statusTable1.DerivedStatus);
        Assert.Equal(1, statusTable1.ActiveServiceRequestsCount);
        Assert.Equal(session1.Id.Value, statusTable1.ActiveSession?.Id);

        var statusTable2 = statusResult.Tables.First(s => s.Table.Id == table2.Id.Value);
        Assert.Equal("BillRequested", statusTable2.DerivedStatus);
        Assert.Equal(DiningSessionStatus.BillRequested.ToString(), statusTable2.ActiveSession?.Status);

        var statusTable3 = statusResult.Tables.First(s => s.Table.Id == table3.Id.Value);
        Assert.Equal("Available", statusTable3.DerivedStatus);
        Assert.Null(statusTable3.ActiveSession);
    }
}
