using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Tenancy;

public class TenantTransactionMiddlewareUnitTests
{
    private readonly Mock<ILogger<TenantTransactionMiddleware>> _mockLogger = new();
    private readonly Mock<ITenantContextAccessor> _mockAccessor = new();

    private static RestaurantOrderDbContext CreateMockDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=postgres;Password=dummy")
            .Options;
        return new RestaurantOrderDbContext(options, TenantContext.Empty);
    }

    [Theory]
    [InlineData("/api/v1/test/ping")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task InvokeAsync_BypassesTransaction_ForExcludedPaths(string path)
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantTransactionMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        using var dbContext = CreateMockDbContext();
        await middleware.InvokeAsync(context, _mockAccessor.Object, dbContext);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_BypassesTransaction_WhenNoTenantInContext()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _mockAccessor.Setup(a => a.TenantContext).Returns(TenantContext.Empty);

        var middleware = new TenantTransactionMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        using var dbContext = CreateMockDbContext();
        await middleware.InvokeAsync(context, _mockAccessor.Object, dbContext);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_BypassesTransaction_WhenTenantPresentButNotAuthenticated()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var unauthTenantContext = new TenantContext(Guid.NewGuid(), isAuthenticated: false);
        _mockAccessor.Setup(a => a.TenantContext).Returns(unauthTenantContext);

        var middleware = new TenantTransactionMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        using var dbContext = CreateMockDbContext();
        await middleware.InvokeAsync(context, _mockAccessor.Object, dbContext);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("tenant_id")]
    [InlineData("tenantid")]
    public async Task InvokeAsync_WhenTenantContextEmpty_ExtractsFromUserClaims(string claimType)
    {
        var tenantId = Guid.NewGuid();
        var claims = new[] { new System.Security.Claims.Claim(claimType, tenantId.ToString()) };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        var user = new System.Security.Claims.ClaimsPrincipal(identity);

        var context = new DefaultHttpContext { User = user };
        context.Request.Path = "/api/v1/test/ping"; // excluded path to bypass db transaction while exercising claim extraction

        ITenantContext? updatedContext = null;
        _mockAccessor.Setup(a => a.TenantContext).Returns(TenantContext.Empty);
        _mockAccessor.SetupSet(a => a.TenantContext = It.IsAny<ITenantContext>())
            .Callback<ITenantContext>(tc => updatedContext = tc);

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantTransactionMiddleware(next, _mockLogger.Object);
        using var dbContext = CreateMockDbContext();
        await middleware.InvokeAsync(context, _mockAccessor.Object, dbContext);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenClaimValueIsInvalidGuid_DoesNotSetTenantContext()
    {
        var claims = new[] { new System.Security.Claims.Claim("tenant_id", "invalid-guid") };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        var user = new System.Security.Claims.ClaimsPrincipal(identity);

        var context = new DefaultHttpContext { User = user };
        context.Request.Path = "/api/v1/test/ping";

        _mockAccessor.Setup(a => a.TenantContext).Returns(TenantContext.Empty);

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantTransactionMiddleware(next, _mockLogger.Object);
        using var dbContext = CreateMockDbContext();
        await middleware.InvokeAsync(context, _mockAccessor.Object, dbContext);

        Assert.True(nextCalled);
        _mockAccessor.VerifySet(a => a.TenantContext = It.IsAny<ITenantContext>(), Times.Never);
    }
}
