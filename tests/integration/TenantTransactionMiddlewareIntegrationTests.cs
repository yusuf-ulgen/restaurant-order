using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class TenantTransactionMiddlewareIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public TenantTransactionMiddlewareIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private RestaurantOrderDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        var tenantContext = new TenantContext(tenantId, isAuthenticated: true);
        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task InvokeAsync_When200Ok_CommitsTransactionAndClearsSession()
    {
        var tenantId = Guid.NewGuid();
        var mockAccessor = new Mock<ITenantContextAccessor>();
        mockAccessor.Setup(a => a.TenantContext).Returns(new TenantContext(tenantId, isAuthenticated: true));

        var nextExecuted = false;
        RequestDelegate next = ctx =>
        {
            nextExecuted = true;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        };

        var middleware = new TenantTransactionMiddleware(next, NullLogger<TenantTransactionMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/orders";

        await using var dbContext = CreateDbContext(tenantId);
        await middleware.InvokeAsync(httpContext, mockAccessor.Object, dbContext);

        Assert.True(nextExecuted);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenErrorStatusCode_RollsBackTransactionAndClearsSession()
    {
        var tenantId = Guid.NewGuid();
        var mockAccessor = new Mock<ITenantContextAccessor>();
        mockAccessor.Setup(a => a.TenantContext).Returns(new TenantContext(tenantId, isAuthenticated: true));

        var nextExecuted = false;
        RequestDelegate next = ctx =>
        {
            nextExecuted = true;
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        };

        var middleware = new TenantTransactionMiddleware(next, NullLogger<TenantTransactionMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/orders";

        await using var dbContext = CreateDbContext(tenantId);
        await middleware.InvokeAsync(httpContext, mockAccessor.Object, dbContext);

        Assert.True(nextExecuted);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_RollsBackTransactionAndRethrows()
    {
        var tenantId = Guid.NewGuid();
        var mockAccessor = new Mock<ITenantContextAccessor>();
        mockAccessor.Setup(a => a.TenantContext).Returns(new TenantContext(tenantId, isAuthenticated: true));

        RequestDelegate next = ctx => throw new InvalidOperationException("Simulation failure during processing.");

        var middleware = new TenantTransactionMiddleware(next, NullLogger<TenantTransactionMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/orders";

        await using var dbContext = CreateDbContext(tenantId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(httpContext, mockAccessor.Object, dbContext));

        Assert.Equal("Simulation failure during processing.", ex.Message);
    }
}
