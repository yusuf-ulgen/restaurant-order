using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
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

public class StaffServiceRequestServiceUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningAreaId _areaId = DiningAreaId.New();
    private readonly IPermissionRegistry _permissionRegistry = new PermissionRegistry();

    private (RestaurantOrderDbContext dbContext, TestOutboxWriter outboxWriter, StaffServiceRequestService service) CreateService(
        TenantId? tenantId = null)
    {
        var effectiveTenantId = tenantId ?? _tenantId;
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"staff_sr_test_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var dbContext = new RestaurantOrderDbContext(options, new TenantContext(effectiveTenantId.Value, isAuthenticated: true));
        var outboxWriter = new TestOutboxWriter();
        var service = new StaffServiceRequestService(
            dbContext,
            _permissionRegistry,
            outboxWriter,
            NullLogger<StaffServiceRequestService>.Instance);

        return (dbContext, outboxWriter, service);
    }

    private RestaurantTable CreateTable(string number, TenantId? tenantId = null, BranchId? branchId = null)
    {
        return RestaurantTable.Create(
            tenantId ?? _tenantId,
            branchId ?? _branchId,
            _areaId,
            number,
            $"Table {number}",
            4);
    }

    private AuthenticatedPrincipal CreateStaffActor(AuthRole role, TenantId? tenantId = null, BranchId? branchId = null)
    {
        var tid = tenantId ?? _tenantId;
        var bid = branchId ?? _branchId;
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        if (role == AuthRole.Customer)
        {
            return AuthenticatedPrincipal.CreateCustomer(sessionId, tid, bid);
        }

        if (role == AuthRole.SuperAdmin)
        {
            return AuthenticatedPrincipal.CreateStaff(
                userId,
                role,
                AuthorizationScope.Platform(),
                sessionId,
                AuthenticationMethod.Password,
                1);
        }

        if (role == AuthRole.RestaurantAdmin)
        {
            return AuthenticatedPrincipal.CreateStaff(
                userId,
                role,
                AuthorizationScope.ForTenant(tid),
                sessionId,
                AuthenticationMethod.Password,
                1);
        }

        return AuthenticatedPrincipal.CreateStaff(
            userId,
            role,
            AuthorizationScope.ForBranch(tid, bid),
            sessionId,
            AuthenticationMethod.Pin,
            1);
    }

    [Fact]
    public async Task ListServiceRequestsAsync_WithPagingAndFilters_ReturnsDeterministicList()
    {
        var (dbContext, _, service) = CreateService();
        var table1 = CreateTable("T-1");
        var table2 = CreateTable("T-2");
        dbContext.RestaurantTables.AddRange(table1, table2);

        var session1 = DiningSessionId.New();
        var session2 = DiningSessionId.New();

        var sr1 = ServiceRequest.Create(_tenantId, _branchId, session1, table1.Id, ServiceRequestType.Water, null, "key-1");
        var sr2 = ServiceRequest.Create(_tenantId, _branchId, session1, table1.Id, ServiceRequestType.Napkin, null, "key-2");
        var sr3 = ServiceRequest.Create(_tenantId, _branchId, session2, table2.Id, ServiceRequestType.Waiter, null, "key-3");

        dbContext.ServiceRequests.AddRange(sr1, sr2, sr3);
        await dbContext.SaveChangesAsync();

        var waiterActor = CreateStaffActor(AuthRole.Waiter);

        // Act 1: List all
        var resultAll = await service.ListServiceRequestsAsync(_tenantId, _branchId, null, null, null, 10, 0, waiterActor);
        Assert.Equal(3, resultAll.Count);

        // Act 2: Filter by Table
        var resultTable1 = await service.ListServiceRequestsAsync(_tenantId, _branchId, null, table1.Id, null, 10, 0, waiterActor);
        Assert.Equal(2, resultTable1.Count);
        Assert.All(resultTable1, item => Assert.Equal("T-1", item.TableNumber));

        // Act 3: Filter by Status
        sr2.Cancel();
        await dbContext.SaveChangesAsync();

        var resultOpen = await service.ListServiceRequestsAsync(_tenantId, _branchId, ServiceRequestStatus.Open, null, null, 10, 0, waiterActor);
        Assert.Equal(2, resultOpen.Count);
        Assert.All(resultOpen, item => Assert.Equal("Open", item.Status));
    }

    [Fact]
    public async Task AcknowledgeRequestAsync_ValidOpenRequest_TransitionsStatusAndEnqueuesOutboxAndAudit()
    {
        var (dbContext, outboxWriter, service) = CreateService();
        var table = CreateTable("T-5");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Waiter, "Need napkins", "idemp-ack-1");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var waiter = CreateStaffActor(AuthRole.Waiter);

        var originalToken = sr.ConcurrencyToken;

        // Act
        var result = await service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, originalToken, waiter);

        // Assert
        Assert.Equal(ServiceRequestStatus.Acknowledged.ToString(), result.Status);
        Assert.NotNull(result.AcknowledgedAtUtc);
        Assert.NotEqual(originalToken, result.ConcurrencyToken);
        Assert.Equal("T-5", result.TableNumber);

        // Verify outbox
        Assert.Single(outboxWriter.EnqueuedEvents);
        var evt = outboxWriter.EnqueuedEvents[0];
        Assert.Equal(RealtimeEventTypes.TableServiceRequestStatusChanged, evt.EventType);

        // Verify audit event
        var audit = await dbContext.SecurityAuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal(SecurityAuditEventType.StaffServiceRequestAcknowledged, audit.EventType);
    }

    [Fact]
    public async Task AcknowledgeRequestAsync_MismatchedConcurrencyToken_ThrowsConcurrencyConflictException()
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-1");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Waiter, null, "idemp-ack-2");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var waiter = CreateStaffActor(AuthRole.Waiter);
        var staleToken = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, staleToken, waiter));
    }

    [Fact]
    public async Task AcknowledgeRequestAsync_CustomerCancelledRace_ThrowsDomainException()
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-1");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Waiter, null, "idemp-ack-3");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var originalToken = sr.ConcurrencyToken;

        // Customer cancels request concurrently
        sr.Cancel();
        await dbContext.SaveChangesAsync();

        var waiter = CreateStaffActor(AuthRole.Waiter);

        // Act & Assert: Token has changed, throws ConcurrencyConflictException
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, originalToken, waiter));
    }

    [Fact]
    public async Task CompleteRequestAsync_ValidRequest_TransitionsStatusAndEnqueuesOutboxAndAudit()
    {
        var (dbContext, outboxWriter, service) = CreateService();
        var table = CreateTable("T-3");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Water, null, "idemp-comp-1");
        sr.Acknowledge();
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var cashier = CreateStaffActor(AuthRole.Cashier);

        var originalToken = sr.ConcurrencyToken;

        // Act
        var result = await service.CompleteRequestAsync(_tenantId, _branchId, sr.Id, originalToken, cashier);

        // Assert
        Assert.Equal(ServiceRequestStatus.Completed.ToString(), result.Status);
        Assert.NotNull(result.CompletedAtUtc);
        Assert.NotEqual(originalToken, result.ConcurrencyToken);
        Assert.Equal("T-3", result.TableNumber);

        // Verify outbox
        Assert.Single(outboxWriter.EnqueuedEvents);
        var evt = outboxWriter.EnqueuedEvents[0];
        Assert.Equal(RealtimeEventTypes.TableServiceRequestStatusChanged, evt.EventType);

        // Verify audit event
        var audit = await dbContext.SecurityAuditEvents.FirstOrDefaultAsync(a => a.EventType == SecurityAuditEventType.StaffServiceRequestCompleted);
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task CompleteRequestAsync_AlreadyCompleted_ThrowsDomainException()
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-3");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Water, null, "idemp-comp-2");
        sr.Acknowledge();
        sr.Complete();
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var waiter = CreateStaffActor(AuthRole.Waiter);

        // Attempting to complete already completed request throws ConcurrencyConflictException (409)
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.CompleteRequestAsync(_tenantId, _branchId, sr.Id, sr.ConcurrencyToken, waiter));
    }

    [Theory]
    [InlineData(AuthRole.RestaurantAdmin, true)]
    [InlineData(AuthRole.BranchManager, true)]
    [InlineData(AuthRole.Cashier, true)]
    [InlineData(AuthRole.Waiter, true)]
    [InlineData(AuthRole.Kitchen, false)]
    [InlineData(AuthRole.Bar, false)]
    [InlineData(AuthRole.Customer, false)]
    [InlineData(AuthRole.SuperAdmin, false)]
    public async Task RoleMatrix_ServiceRequestManage_EnforcesCorrectAccess(AuthRole role, bool expectedSuccess)
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-9");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Water, null, $"idemp-rbac-{role}");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var actor = CreateStaffActor(role);

        if (expectedSuccess)
        {
            var res = await service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, sr.ConcurrencyToken, actor);
            Assert.Equal(ServiceRequestStatus.Acknowledged.ToString(), res.Status);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
                service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, sr.ConcurrencyToken, actor));
        }
    }

    [Fact]
    public async Task TenantAndBranchMismatch_ThrowsInvalidAuthorizationScopeException()
    {
        var (dbContext, _, service) = CreateService();
        var otherBranch = BranchId.New();
        var table = CreateTable("T-1", _tenantId, otherBranch);
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, otherBranch, DiningSessionId.New(), table.Id, ServiceRequestType.Water, null, "idemp-mismatch-1");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var waiterActor = CreateStaffActor(AuthRole.Waiter); // Actor is scoped to _branchId, not otherBranch

        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.AcknowledgeRequestAsync(_tenantId, otherBranch, sr.Id, sr.ConcurrencyToken, waiterActor));
    }

    [Fact]
    public async Task TwoWaiters_ConcurrentAcknowledge_OnlyOneSucceedsAndSecondGetsConflict()
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-Race-1");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Waiter, "Garson çağrısı", "idemp-race-ack");
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var initialToken = sr.ConcurrencyToken;

        var waiter1 = CreateStaffActor(AuthRole.Waiter);
        var waiter2 = CreateStaffActor(AuthRole.Waiter);

        // Waiter 1 acknowledges first -> SUCCESS
        var result1 = await service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, initialToken, waiter1);
        Assert.Equal(ServiceRequestStatus.Acknowledged.ToString(), result1.Status);
        Assert.NotEqual(initialToken, result1.ConcurrencyToken);

        // Waiter 2 arrives with stale initialToken -> 409 ConcurrencyConflictException
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.AcknowledgeRequestAsync(_tenantId, _branchId, sr.Id, initialToken, waiter2));
    }

    [Fact]
    public async Task TwoStaff_ConcurrentComplete_OnlyOneSucceedsAndSecondGetsConflict()
    {
        var (dbContext, _, service) = CreateService();
        var table = CreateTable("T-Race-2");
        dbContext.RestaurantTables.Add(table);

        var sr = ServiceRequest.Create(_tenantId, _branchId, DiningSessionId.New(), table.Id, ServiceRequestType.Water, null, "idemp-race-comp");
        sr.Acknowledge();
        dbContext.ServiceRequests.Add(sr);
        await dbContext.SaveChangesAsync();

        var initialToken = sr.ConcurrencyToken;

        var staff1 = CreateStaffActor(AuthRole.Waiter);
        var staff2 = CreateStaffActor(AuthRole.Cashier);

        // Staff 1 completes first -> SUCCESS
        var result1 = await service.CompleteRequestAsync(_tenantId, _branchId, sr.Id, initialToken, staff1);
        Assert.Equal(ServiceRequestStatus.Completed.ToString(), result1.Status);

        // Staff 2 arrives with stale initialToken -> 409 ConcurrencyConflictException
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.CompleteRequestAsync(_tenantId, _branchId, sr.Id, initialToken, staff2));
    }


    public sealed class TestOutboxWriter : IOperationalOutboxWriter
    {
        public List<(string EventType, object Data)> EnqueuedEvents { get; } = new();

        public void EnqueueEvent<T>(
            Guid tenantId,
            Guid branchId,
            string aggregateId,
            string eventType,
            T data,
            string idempotencyKey,
            DateTimeOffset? occurredAtUtc = null,
            int schemaVersion = 1,
            Guid? eventId = null)
        {
            EnqueuedEvents.Add((eventType, data!));
        }
    }
}
