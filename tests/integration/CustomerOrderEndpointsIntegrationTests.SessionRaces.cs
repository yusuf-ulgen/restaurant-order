using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CustomerOrderEndpointsIntegrationTests
{
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task SessionMutation_WaitsForCustomerOrStaffOrderCommit(bool staff, int targetStatus)
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();
        var (tenant, branch, _, session, item) = await SetupSessionAndCatalogAsync();
        await using var db = await CreateRuntimeOrderContextAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new PausedPricingEngine(new OrderPricingEngine(db), entered, release);
        var orderTask = CreateReviewedOrderAsync(db, engine, staff, tenant, branch, session, item);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await using var mutation = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await mutation.OpenAsync();
        await using var command = BuildSessionMutation(mutation, tenant, session, targetStatus);
        var mutationTask = command.ExecuteNonQueryAsync();
        bool wasBlocked;
        try
        {
            wasBlocked = await WaitForDatabaseBlockAsync(mutationTask, mutation.ProcessID);
        }
        finally
        {
            release.TrySetResult();
        }

        await orderTask;
        Assert.Equal(1, await mutationTask);
        Assert.True(wasBlocked, "Oturum değişikliği, siparişin doğrulama ve commit aralığını aşmamalıdır.");
        Assert.Equal(1, await CountSessionOrdersAsync(tenant, session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderCreation_WaitsForConcurrentClose_ThenRejectsWithoutPersistence(bool staff)
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();
        var (tenant, branch, _, session, item) = await SetupSessionAndCatalogAsync();
        await using var mutation = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await mutation.OpenAsync();
        await using var transaction = await mutation.BeginTransactionAsync();
        await using var command = BuildSessionMutation(mutation, tenant, session, 4);
        command.Transaction = transaction;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        await using var db = await CreateRuntimeOrderContextAsync();
        await db.Database.OpenConnectionAsync();
        var orderPid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var orderTask = CreateReviewedOrderAsync(db, new OrderPricingEngine(db), staff, tenant, branch, session, item);
        var wasBlocked = await WaitForDatabaseBlockAsync(orderTask, orderPid);
        await transaction.CommitAsync();

        var error = await Assert.ThrowsAsync<OrderPricingException>(() => orderTask);
        Assert.Equal(OrderPricingException.CodeSessionNotActive, error.ErrorCode);
        Assert.True(wasBlocked, "Sipariş, bekleyen kapanışın eski Active görüntüsüyle oluşturulmamalıdır.");
        Assert.Equal(0, await CountSessionOrdersAsync(tenant, session));
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeOrderContextAsync()
    {
        var connection = await _fixture.ProvisionTemporaryRuntimeRoleAsync();
        return new RestaurantOrderDbContext(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connection).Options);
    }

    private static async Task CreateReviewedOrderAsync(RestaurantOrderDbContext db, IOrderPricingEngine engine,
        bool staff, Guid tenant, Guid branch, Guid session, Guid item)
    {
        var tenantId = new TenantId(tenant);
        var branchId = new BranchId(branch);
        var sessionId = new DiningSessionId(session);
        var key = $"session-race-{Guid.NewGuid():N}";
        if (staff)
        {
            var actor = AuthenticatedPrincipal.CreateStaff(Guid.NewGuid(), AuthRole.Waiter,
                AuthorizationScope.ForBranch(tenantId, branchId), Guid.NewGuid(), AuthenticationMethod.Password, 1);
            var service = new StaffOrderService(db, engine, new PermissionRegistry(), Mock.Of<ISupervisorPinService>());
            await service.CreateStaffOrderAsync(tenantId, branchId, sessionId,
                new CreateStaffOrderRequest([new(item, null, 1, null, null, 2500)],
                    RestaurantConfigTestHelpers.DefaultTaxZeroPolicyFingerprint, 2500), key, actor);
        }
        else
        {
            var service = new CustomerOrderService(db, engine);
            await service.CreateOrderAsync(tenantId, branchId, sessionId, CreateRequest(item), key,
                AuthenticatedPrincipal.CreateCustomer(session, tenantId, branchId));
        }
    }

    private static NpgsqlCommand BuildSessionMutation(NpgsqlConnection connection, Guid tenant, Guid session, int status)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenancy.dining_sessions SET status = @status,
                bill_requested_at_utc = NOW(),
                closed_at_utc = CASE WHEN @status = 4 THEN NOW() ELSE NULL END,
                concurrency_token = gen_random_uuid()
            WHERE tenant_id = @tenant AND id = @session
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("session", session);
        return command;
    }

    private async Task<bool> WaitForDatabaseBlockAsync(Task operation, int processId)
    {
        await using var observer = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await observer.OpenAsync();
        await using var command = observer.CreateCommand();
        command.CommandText = "SELECT cardinality(pg_blocking_pids(@pid)) > 0";
        command.Parameters.AddWithValue("pid", processId);
        var elapsed = Stopwatch.StartNew();
        while (!operation.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            if ((bool)(await command.ExecuteScalarAsync())!) return true;
            await Task.Delay(20);
        }
        return false;
    }

    private async Task<long> CountSessionOrdersAsync(Guid tenant, Guid session)
    {
        await using var observer = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await observer.OpenAsync();
        await using var command = observer.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tenancy.orders WHERE tenant_id = @tenant AND dining_session_id = @session";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("session", session);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class PausedPricingEngine(IOrderPricingEngine inner, TaskCompletionSource entered, TaskCompletionSource release)
        : IOrderPricingEngine
    {
        public async Task<PricedOrderResult> PriceAndValidateOrderAsync(TenantId tenantId, BranchId branchId,
            DiningSessionId sessionId, IReadOnlyList<ClientOrderItemDraft> items, string? clientPricingPolicyFingerprint = null,
            long? clientExpectedTotalMinorUnits = null, CancellationToken cancellationToken = default)
        {
            var priced = await inner.PriceAndValidateOrderAsync(tenantId, branchId, sessionId, items,
                clientPricingPolicyFingerprint, clientExpectedTotalMinorUnits, cancellationToken);
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return priced;
        }
    }
}
