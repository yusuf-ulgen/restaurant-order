using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Domain;

public class TenantTests
{
    [Fact]
    public void Create_WithValidParameters_CreatesActiveTenantWithUtcTimestamps()
    {
        var before = DateTime.UtcNow;
        var tenant = Tenant.Create("Burger Lab", "burger-lab");
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, tenant.Id.Value);
        Assert.Equal("Burger Lab", tenant.Name);
        Assert.Equal("burger-lab", tenant.Slug.Value);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.InRange(tenant.CreatedAtUtc, before, after);
        Assert.Null(tenant.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, tenant.ConcurrencyToken);
    }

    [Fact]
    public void Create_WithCustomId_PreservesProvidedId()
    {
        var customId = TenantId.New();
        var tenant = Tenant.Create("Artisan Pizza", "artisan-pizza", customId);

        Assert.Equal(customId, tenant.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithInvalidName_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() => Tenant.Create(invalidName!, "valid-slug"));
        Assert.Contains("name cannot be null, empty, or whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Invalid Slug")]
    [InlineData("slug_with_underscores")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    [InlineData("UPPERCASE")]
    public void Create_WithInvalidSlug_ThrowsDomainException(string invalidSlug)
    {
        Assert.Throws<DomainException>(() => Tenant.Create("Valid Name", invalidSlug));
    }

    [Fact]
    public void UpdateName_WithValidName_UpdatesNameAndTouchesTimestamp()
    {
        var tenant = Tenant.Create("Initial Name", "initial-slug");
        var originalToken = tenant.ConcurrencyToken;

        tenant.UpdateName("Updated Name");

        Assert.Equal("Updated Name", tenant.Name);
        Assert.NotNull(tenant.UpdatedAtUtc);
        Assert.NotEqual(originalToken, tenant.ConcurrencyToken);
    }

    [Fact]
    public void UpdateName_WhenClosed_ThrowsDomainException()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Close();

        var ex = Assert.Throws<DomainException>(() => tenant.UpdateName("New Name"));
        Assert.Contains("not allowed on a closed tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspend_WhenActive_TransitionsToSuspended()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");

        tenant.Suspend("Billing overdue");

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.NotNull(tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Suspend_WhenAlreadySuspended_IsIdempotent()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Suspend();
        var token = tenant.ConcurrencyToken;

        tenant.Suspend();

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(token, tenant.ConcurrencyToken);
    }

    [Fact]
    public void Suspend_WhenClosed_ThrowsDomainException()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Close();

        var ex = Assert.Throws<DomainException>(() => tenant.Suspend());
        Assert.Contains("Cannot suspend a closed tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_WhenSuspended_TransitionsToActive()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Suspend();

        tenant.Activate();

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.NotNull(tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        var token = tenant.ConcurrencyToken;

        tenant.Activate();

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(token, tenant.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenClosed_ThrowsDomainException()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Close();

        var ex = Assert.Throws<DomainException>(() => tenant.Activate());
        Assert.Contains("Cannot activate a permanently closed tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenActiveOrSuspended_TransitionsToClosedPermanently()
    {
        var activeTenant = Tenant.Create("Active Tenant", "active-tenant");
        activeTenant.Close("Contract ended");
        Assert.Equal(TenantStatus.Closed, activeTenant.Status);

        var suspendedTenant = Tenant.Create("Suspended Tenant", "suspended-tenant");
        suspendedTenant.Suspend();
        suspendedTenant.Close();
        Assert.Equal(TenantStatus.Closed, suspendedTenant.Status);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_IsIdempotent()
    {
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        tenant.Close();
        var token = tenant.ConcurrencyToken;

        tenant.Close();

        Assert.Equal(TenantStatus.Closed, tenant.Status);
        Assert.Equal(token, tenant.ConcurrencyToken);
    }
}
