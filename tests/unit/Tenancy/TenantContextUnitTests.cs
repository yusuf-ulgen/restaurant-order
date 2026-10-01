using RestaurantOrder.Application.Tenancy;
using Xunit;

namespace RestaurantOrder.UnitTests.Tenancy;

public class TenantContextUnitTests
{
    [Fact]
    public void TenantContext_WhenValidAndAuthenticated_ReturnsValues()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var context = new TenantContext(tenantId, branchId, isAuthenticated: true);

        Assert.True(context.HasTenant);
        Assert.True(context.IsAuthenticated);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal(branchId, context.BranchId);
        Assert.Equal(tenantId, context.RequireTenantId());
    }

    [Fact]
    public void TenantContext_WhenUnauthenticatedOrEmpty_FailsClosed()
    {
        var unauthenticated = new TenantContext(Guid.NewGuid(), isAuthenticated: false);
        Assert.False(unauthenticated.IsAuthenticated);
        Assert.Throws<TenantContextException>(() => unauthenticated.RequireTenantId());

        var empty = new TenantContext(Guid.Empty, isAuthenticated: true);
        Assert.False(empty.HasTenant);
        Assert.False(empty.IsAuthenticated);
        Assert.Null(empty.TenantId);
        Assert.Throws<TenantContextException>(() => empty.RequireTenantId());
    }

    [Fact]
    public void TenantCacheKeyFactory_Generates_Valid_Keys()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var branchKey = TenantCacheKeyFactory.Create(tenantId, branchId, "orders", "123");
        Assert.Equal($"cache:{tenantId:D}:{branchId:D}:orders:123", branchKey);

        var globalKey = TenantCacheKeyFactory.CreateTenantScoped(tenantId, "menu", "root");
        Assert.Equal($"cache:{tenantId:D}:_global:menu:root", globalKey);
    }

    [Theory]
    [InlineData("order:inject")]
    [InlineData("order\ninject")]
    [InlineData("order\rinject")]
    [InlineData("order\0inject")]
    [InlineData("order inject")]
    [InlineData("order\tinject")]
    public void TenantCacheKeyFactory_Rejects_KeyInjection_Characters(string maliciousToken)
    {
        var tenantId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            TenantCacheKeyFactory.Create(tenantId, null, maliciousToken, "123"));

        Assert.Throws<ArgumentException>(() =>
            TenantCacheKeyFactory.Create(tenantId, null, "orders", maliciousToken));
    }

    [Fact]
    public void TenantCacheKeyFactory_Rejects_EmptyTenantId()
    {
        Assert.Throws<ArgumentException>(() =>
            TenantCacheKeyFactory.Create(Guid.Empty, null, "orders", "123"));
    }

    [Fact]
    public async Task TenantWorkerJobRunner_ExecutesInContext_And_CleansUp()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var runner = new TenantWorkerJobRunner(accessor);

        var tenantId = Guid.NewGuid();
        var envelope = new TestJobEnvelope(tenantId, null, "order-sync", "key-1");

        Assert.False(accessor.TenantContext.HasTenant);

        await runner.ExecuteAsync(envelope, async (ctx, ct) =>
        {
            await Task.Yield();
            Assert.True(accessor.TenantContext.HasTenant);
            Assert.Equal(tenantId, accessor.TenantContext.TenantId);
            Assert.Equal(tenantId, ctx.TenantId);
        });

        // After completion, ambient context must be cleaned up
        Assert.False(accessor.TenantContext.HasTenant);
    }

    [Fact]
    public async Task TenantWorkerJobRunner_Rejects_EmptyTenantId_FailClosed()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var runner = new TenantWorkerJobRunner(accessor);

        var envelope = new TestJobEnvelope(Guid.Empty, null, "order-sync", "key-2");

        await Assert.ThrowsAsync<TenantContextException>(() =>
            runner.ExecuteAsync(envelope, (ctx, ct) => Task.CompletedTask));

        Assert.False(accessor.TenantContext.HasTenant);
    }

    [Fact]
    public async Task TenantWorkerJobRunner_SequentialJobs_DoNotLeakContext()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var runner = new TenantWorkerJobRunner(accessor);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await runner.ExecuteAsync(new TestJobEnvelope(tenantA, null, "job-a", "key-a"), (ctx, ct) =>
        {
            Assert.Equal(tenantA, accessor.TenantContext.TenantId);
            return Task.CompletedTask;
        });

        Assert.False(accessor.TenantContext.HasTenant);

        await runner.ExecuteAsync(new TestJobEnvelope(tenantB, null, "job-b", "key-b"), (ctx, ct) =>
        {
            Assert.Equal(tenantB, accessor.TenantContext.TenantId);
            return Task.CompletedTask;
        });

        Assert.False(accessor.TenantContext.HasTenant);
    }

    [Fact]
    public async Task TenantWorkerJobRunner_ParallelJobs_MaintainIsolation()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var runner = new TenantWorkerJobRunner(accessor);

        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var tenantId = Guid.NewGuid();
            var envelope = new TestJobEnvelope(tenantId, null, $"job-{i}", $"key-{i}");

            await runner.ExecuteAsync(envelope, async (ctx, ct) =>
            {
                await Task.Delay(10, ct);
                Assert.Equal(tenantId, accessor.TenantContext.TenantId);
                Assert.Equal(tenantId, ctx.TenantId);
            });
        });

        await Task.WhenAll(tasks);
        Assert.False(accessor.TenantContext.HasTenant);
    }

    private record TestJobEnvelope(
        Guid TenantId,
        Guid? BranchId,
        string JobType,
        string IdempotencyKey) : ITenantJobEnvelope;
}
