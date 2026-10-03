using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class BranchLifecycleUnitTests
{
    [Fact]
    public void Create_ValidParameters_InstantiatesActiveBranch()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var before = DateTime.UtcNow;

        var branch = Branch.Create(tenantId, brand, "Downtown Branch", "downtown", "Europe/Istanbul", "TRY");
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, branch.Id.Value);
        Assert.Equal(tenantId, branch.TenantId);
        Assert.Equal(brand.Id, branch.BrandId);
        Assert.Equal("Downtown Branch", branch.Name);
        Assert.Equal("downtown", branch.Slug.Value);
        Assert.Equal("Europe/Istanbul", branch.Timezone.Id);
        Assert.Equal("TRY", branch.Currency.Code);
        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.InRange(branch.CreatedAtUtc, before, after);
        Assert.Null(branch.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, branch.ConcurrencyToken);
    }

    [Fact]
    public void Create_BrandTenantMismatch_ThrowsDomainException()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var brandA = Brand.Create(tenantA, "Brand A", "brand-a");

        var ex = Assert.Throws<DomainException>(() =>
            Branch.Create(tenantB, brandA, "Branch B", "branch-b"));

        Assert.Contains("Tenant mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");

        var ex = Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brand, invalidName!, "valid-slug"));

        Assert.Contains("name cannot be null, empty, or whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Invalid/Timezone")]
    [InlineData("Unknown/Zone")]
    [InlineData("")]
    public void Create_InvalidTimezone_ThrowsDomainException(string invalidTz)
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");

        Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brand, "Branch", "branch-slug", timezone: invalidTz));
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("US")]
    [InlineData("XXX")]
    [InlineData("")]
    public void Create_InvalidCurrency_ThrowsDomainException(string invalidCurrency)
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");

        Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brand, "Branch", "branch-slug", currency: invalidCurrency));
    }

    [Fact]
    public void UpdateDetails_ValidValues_UpdatesPropertiesAndRegeneratesConcurrencyToken()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Initial Name", "initial-slug", "Europe/Istanbul", "TRY");
        var initialToken = branch.ConcurrencyToken;

        branch.UpdateDetails("Updated Name", "Europe/London", "GBP");

        Assert.Equal("Updated Name", branch.Name);
        Assert.Equal("Europe/London", branch.Timezone.Id);
        Assert.Equal("GBP", branch.Currency.Code);
        Assert.NotNull(branch.UpdatedAtUtc);
        Assert.NotEqual(initialToken, branch.ConcurrencyToken);
    }

    [Fact]
    public void UpdateDetails_WhenBranchIsClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() =>
            branch.UpdateDetails("New Name", "Europe/Istanbul", "TRY"));

        Assert.Contains("Modifications are not allowed on a closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspend_WhenActive_TransitionsToSuspended()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        var initialToken = branch.ConcurrencyToken;

        branch.Suspend("Renovation");

        Assert.Equal(BranchStatus.Suspended, branch.Status);
        Assert.NotNull(branch.UpdatedAtUtc);
        Assert.NotEqual(initialToken, branch.ConcurrencyToken);
    }

    [Fact]
    public void Suspend_WhenAlreadySuspended_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Suspend();
        var tokenAfterFirst = branch.ConcurrencyToken;

        branch.Suspend();

        Assert.Equal(BranchStatus.Suspended, branch.Status);
        Assert.Equal(tokenAfterFirst, branch.ConcurrencyToken);
    }

    [Fact]
    public void Suspend_WhenClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() => branch.Suspend());
        Assert.Contains("Cannot suspend a closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_WhenSuspended_TransitionsToActive()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Suspend();
        var suspendedToken = branch.ConcurrencyToken;

        branch.Activate();

        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.NotEqual(suspendedToken, branch.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        var initialToken = branch.ConcurrencyToken;

        branch.Activate();

        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.Equal(initialToken, branch.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() => branch.Activate());
        Assert.Contains("Cannot activate a permanently closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenActiveOrSuspended_TransitionsToClosedPermanently()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");

        branch.Close("Lease ended");

        Assert.Equal(BranchStatus.Closed, branch.Status);
        Assert.NotNull(branch.UpdatedAtUtc);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Brand", "brand");
        var branch = Branch.Create(tenantId, brand, "Name", "slug");
        branch.Close();
        var closedToken = branch.ConcurrencyToken;

        branch.Close();

        Assert.Equal(BranchStatus.Closed, branch.Status);
        Assert.Equal(closedToken, branch.ConcurrencyToken);
    }
}
