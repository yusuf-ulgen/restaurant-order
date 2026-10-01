using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Domain;

public class BrandTests
{
    [Fact]
    public void Create_WithValidParameters_CreatesActiveBrandAttachedToTenant()
    {
        var tenantId = TenantId.New();
        var before = DateTime.UtcNow;

        var brand = Brand.Create(tenantId, "Smash Burgers", "smash-burgers");
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, brand.Id.Value);
        Assert.Equal(tenantId, brand.TenantId);
        Assert.Equal("Smash Burgers", brand.Name);
        Assert.Equal("smash-burgers", brand.Slug.Value);
        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.InRange(brand.CreatedAtUtc, before, after);
        Assert.Null(brand.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, brand.ConcurrencyToken);
    }

    [Fact]
    public void Create_WithCustomId_PreservesProvidedId()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var brand = Brand.Create(tenantId, "Smash Burgers", "smash-burgers", brandId);

        Assert.Equal(brandId, brand.Id);
        Assert.Equal(tenantId, brand.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithInvalidName_ThrowsDomainException(string? invalidName)
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
    public void Create_WithInvalidSlug_ThrowsDomainException(string invalidSlug)
    {
        var tenantId = TenantId.New();

        Assert.Throws<DomainException>(() => Brand.Create(tenantId, "Valid Brand", invalidSlug));
    }

    [Fact]
    public void UpdateName_WithValidName_UpdatesNameAndTouchesTimestamp()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Initial Brand", "initial-brand");
        var originalToken = brand.ConcurrencyToken;

        brand.UpdateName("Updated Brand");

        Assert.Equal("Updated Brand", brand.Name);
        Assert.NotNull(brand.UpdatedAtUtc);
        Assert.NotEqual(originalToken, brand.ConcurrencyToken);
    }

    [Fact]
    public void Deactivate_WhenActive_TransitionsToInactive()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand");

        brand.Deactivate();

        Assert.Equal(BrandStatus.Inactive, brand.Status);
        Assert.NotNull(brand.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand");
        brand.Deactivate();
        var token = brand.ConcurrencyToken;

        brand.Deactivate();

        Assert.Equal(BrandStatus.Inactive, brand.Status);
        Assert.Equal(token, brand.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenInactive_TransitionsToActive()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand");
        brand.Deactivate();

        brand.Activate();

        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.NotNull(brand.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand");
        var token = brand.ConcurrencyToken;

        brand.Activate();

        Assert.Equal(BrandStatus.Active, brand.Status);
        Assert.Equal(token, brand.ConcurrencyToken);
    }

    [Fact]
    public void TenantId_CannotBeModifiedAfterCreation()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand");

        // TenantId property has private setter and no public mutation method
        var prop = typeof(Brand).GetProperty(nameof(Brand.TenantId));
        Assert.NotNull(prop);
        Assert.False(prop.SetMethod?.IsPublic ?? true);
        Assert.Equal(tenantId, brand.TenantId);
    }
}
