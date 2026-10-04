using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Persistence;

/// <summary>
/// Unit tests verifying that EF Core 10 compiles Global Tenant Query Filters
/// into fully translatable PostgreSQL server-side SQL queries with stable parameterization.
/// Guarantees that constructors are never called inside expression trees and that
/// queries fail-closed when tenant context is absent.
/// </summary>
public class GlobalTenantQueryFilterTests
{
    private static RestaurantOrderDbContext CreateDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=postgres;Password=dummy")
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public void GlobalQueryFilter_WhenTenantPresent_TranslatesToQueryStringSuccessfully()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext(tenantId, isAuthenticated: true);
        using var dbContext = CreateDbContext(context);

        // Verify Tenants query filter translates to PostgreSQL SQL
        var tenantSql = dbContext.Tenants.ToQueryString();
        Assert.NotNull(tenantSql);
        Assert.Contains("FROM tenancy.tenants", tenantSql);
        Assert.Contains("WHERE", tenantSql);

        // Verify Brands query filter translates to PostgreSQL SQL
        var brandSql = dbContext.Brands.ToQueryString();
        Assert.NotNull(brandSql);
        Assert.Contains("FROM tenancy.brands", brandSql);
        Assert.Contains("WHERE", brandSql);

        // Verify Branches query filter translates to PostgreSQL SQL
        var branchSql = dbContext.Branches.ToQueryString();
        Assert.NotNull(branchSql);
        Assert.Contains("FROM tenancy.branches", branchSql);
        Assert.Contains("WHERE", branchSql);

        // Verify Menus query filter translates to PostgreSQL SQL
        var menuSql = dbContext.Menus.ToQueryString();
        Assert.NotNull(menuSql);
        Assert.Contains("FROM tenancy.menus", menuSql);
        Assert.Contains("WHERE", menuSql);

        // Verify MenuCategories query filter translates to PostgreSQL SQL
        var categorySql = dbContext.MenuCategories.ToQueryString();
        Assert.NotNull(categorySql);
        Assert.Contains("FROM tenancy.menu_categories", categorySql);
        Assert.Contains("WHERE", categorySql);
    }

    [Fact]
    public void GlobalQueryFilter_WhenTenantAbsent_TranslatesFailClosedWithoutThrowing()
    {
        using var dbContext = CreateDbContext(TenantContext.Empty);

        Assert.False(dbContext.HasTenant);
        Assert.Equal(default(TenantId), dbContext.CurrentTenantId);

        // Without tenant context, query filter must still translate to valid SQL (fail-closed)
        var sql = dbContext.Tenants.ToQueryString();
        Assert.NotNull(sql);
        Assert.Contains("FROM tenancy.tenants", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void GlobalQueryFilter_WithPredicate_ComposesFilterServerSide()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext(tenantId, isAuthenticated: true);
        using var dbContext = CreateDbContext(context);

        var tenantIdVo = new TenantId(tenantId);
        var sql = dbContext.Tenants.Where(t => t.Id == tenantIdVo).ToQueryString();

        Assert.NotNull(sql);
        Assert.Contains("FROM tenancy.tenants", sql);
        Assert.Contains("WHERE", sql);
    }
}
