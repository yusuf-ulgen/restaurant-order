using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class BrandLifecycleUnitTests
{
    [Fact]
    public void Create_ValidParameters_InstantiatesActiveBrand()
    {
        var tenantId = TenantId.New();
        var before = DateTime.UtcNow;

        var brand = Brand.Create(tenantId, "Artisan Burgers", "artisan-burgers");
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, brand.Id.Value);
        Assert.Equal(tenantId, brand.TenantId);
        Assert.Equal("Artisan Burgers", brand.Name);
        Assert.Equal("artisan-burgers", brand.Slug.Value);
        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.InRange(brand.CreatedAtUtc, before, after);
        Assert.Null(brand.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, brand.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var tenantId = TenantId.New();
        var ex = Assert.Throws<DomainException>(() => Brand.Create(tenantId, invalidName!, "valid-slug"));
        Assert.Contains("name cannot be null, empty, or whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid slug")]
    [InlineData("slug_with_underscore")]
    [InlineData("-slug")]
    [InlineData("slug-")]
    [InlineData("UPPERCASE")]
    [InlineData("a")]
    public void Create_InvalidSlug_ThrowsDomainException(string invalidSlug)
    {
        var tenantId = TenantId.New();
        Assert.Throws<DomainException>(() => Brand.Create(tenantId, "Valid Brand", invalidSlug));
    }

    [Fact]
    public void UpdateName_ValidName_UpdatesNameAndRegeneratesConcurrencyToken()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Initial Brand", "initial-brand");
        var initialToken = brand.ConcurrencyToken;

        brand.UpdateName("Updated Brand");

        Assert.Equal("Updated Brand", brand.Name);
        Assert.NotNull(brand.UpdatedAtUtc);
        Assert.NotEqual(initialToken, brand.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UpdateName_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Initial Brand", "initial-brand");

        var ex = Assert.Throws<DomainException>(() => brand.UpdateName(invalidName!));
        Assert.Contains("name cannot be null, empty, or whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deactivate_WhenActive_TransitionsToInactive()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Active Brand", "active-brand");
        var initialToken = brand.ConcurrencyToken;

        brand.Deactivate();

        Assert.Equal(BrandStatus.Inactive, brand.Status);
        Assert.NotNull(brand.UpdatedAtUtc);
        Assert.NotEqual(initialToken, brand.ConcurrencyToken);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        brand.Deactivate();
        var tokenAfterFirst = brand.ConcurrencyToken;
        var updatedAtAfterFirst = brand.UpdatedAtUtc;

        brand.Deactivate();

        Assert.Equal(BrandStatus.Inactive, brand.Status);
        Assert.Equal(tokenAfterFirst, brand.ConcurrencyToken);
        Assert.Equal(updatedAtAfterFirst, brand.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenInactive_TransitionsToActive()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        brand.Deactivate();
        var tokenWhileInactive = brand.ConcurrencyToken;

        brand.Activate();

        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.NotEqual(tokenWhileInactive, brand.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var initialToken = brand.ConcurrencyToken;

        brand.Activate();

        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.Equal(initialToken, brand.ConcurrencyToken);
        Assert.Null(brand.UpdatedAtUtc);
    }
}
