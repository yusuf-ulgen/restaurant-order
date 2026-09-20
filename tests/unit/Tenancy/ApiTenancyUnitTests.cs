using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Tenancy;
using Xunit;

namespace RestaurantOrder.UnitTests.Tenancy;

public class ApiTenancyUnitTests
{
    [Fact]
    public async Task DefaultTenantContextResolver_AlwaysReturns_EmptyContext()
    {
        var resolver = new DefaultTenantContextResolver();
        var context = await resolver.ResolveAsync();

        Assert.Same(TenantContext.Empty, context);
        Assert.False(context.HasTenant);
        Assert.False(context.IsAuthenticated);
    }

    [Fact]
    public void RequireTenantAttribute_CanBeInstantiated()
    {
        var attr = new RequireTenantAttribute();
        Assert.NotNull(attr);
    }

    [Fact]
    public async Task DevelopmentHeaderResolver_ThrowsInNonDevelopment()
    {
        var mockAccessor = new Mock<IHttpContextAccessor>();
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<DevelopmentHeaderTenantContextResolver>>();

        var resolver = new DevelopmentHeaderTenantContextResolver(
            mockAccessor.Object, mockEnv.Object, mockConfig.Object, mockLogger.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync());
        Assert.Contains("FATAL SECURITY VIOLATION", ex.Message);
    }

    [Fact]
    public async Task DevelopmentHeaderResolver_ReturnsEmpty_WhenOptInDisabled()
    {
        var mockAccessor = new Mock<IHttpContextAccessor>();
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Development");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:AllowDevHeaderOverride"] = "false"
            })
            .Build();

        var mockLogger = new Mock<ILogger<DevelopmentHeaderTenantContextResolver>>();

        var resolver = new DevelopmentHeaderTenantContextResolver(
            mockAccessor.Object, mockEnv.Object, config, mockLogger.Object);

        var result = await resolver.ResolveAsync();
        Assert.Same(TenantContext.Empty, result);
    }

    [Fact]
    public async Task DevelopmentHeaderResolver_ReturnsEmpty_WhenHttpContextOrHeaderMissing()
    {
        var mockAccessor = new Mock<IHttpContextAccessor>();
        mockAccessor.Setup(a => a.HttpContext).Returns((HttpContext)null!);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Development");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:AllowDevHeaderOverride"] = "true"
            })
            .Build();

        var mockLogger = new Mock<ILogger<DevelopmentHeaderTenantContextResolver>>();

        var resolver = new DevelopmentHeaderTenantContextResolver(
            mockAccessor.Object, mockEnv.Object, config, mockLogger.Object);

        var resultNoContext = await resolver.ResolveAsync();
        Assert.Same(TenantContext.Empty, resultNoContext);

        // With context but no header
        var httpContext = new DefaultHttpContext();
        mockAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var resultNoHeader = await resolver.ResolveAsync();
        Assert.Same(TenantContext.Empty, resultNoHeader);

        // With malformed header
        httpContext.Request.Headers["X-Tenant-Id"] = "invalid-guid";
        var resultMalformed = await resolver.ResolveAsync();
        Assert.Same(TenantContext.Empty, resultMalformed);
    }

    [Fact]
    public async Task DevelopmentHeaderResolver_ResolvesValidHeaders()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        httpContext.Request.Headers["X-Branch-Id"] = branchId.ToString();

        var mockAccessor = new Mock<IHttpContextAccessor>();
        mockAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Development");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:AllowDevHeaderOverride"] = "true"
            })
            .Build();

        var mockLogger = new Mock<ILogger<DevelopmentHeaderTenantContextResolver>>();

        var resolver = new DevelopmentHeaderTenantContextResolver(
            mockAccessor.Object, mockEnv.Object, config, mockLogger.Object);

        var result = await resolver.ResolveAsync();
        Assert.True(result.HasTenant);
        Assert.True(result.IsAuthenticated);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(branchId, result.BranchId);
    }

    [Fact]
    public async Task TenantContextMiddleware_GeneratesCorrelationId_AndCleansUpContext()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var mockLogger = new Mock<ILogger<TenantContextMiddleware>>();

        var mockResolver = new Mock<ITenantContextResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext(Guid.NewGuid(), isAuthenticated: true));

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            Assert.True(accessor.TenantContext.HasTenant);
            return Task.CompletedTask;
        };

        var middleware = new TenantContextMiddleware(next, mockLogger.Object);
        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(httpContext, mockResolver.Object, accessor);

        Assert.True(nextCalled);
        Assert.True(httpContext.Response.Headers.ContainsKey(TenantContextMiddleware.CorrelationIdHeader));
        // Context must be cleaned up in finally block
        Assert.Same(TenantContext.Empty, accessor.TenantContext);
    }

    [Fact]
    public async Task TenantContextMiddleware_BlocksAccess_WhenTenantRequiredButMissing()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var mockLogger = new Mock<ILogger<TenantContextMiddleware>>();

        var mockResolver = new Mock<ITenantContextResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantContext.Empty);

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantContextMiddleware(next, mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Attach endpoint metadata requiring tenant
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequireTenantAttribute()),
            "TestEndpoint");
        httpContext.SetEndpoint(endpoint);

        await middleware.InvokeAsync(httpContext, mockResolver.Object, accessor);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);
        Assert.Same(TenantContext.Empty, accessor.TenantContext);
    }
}
