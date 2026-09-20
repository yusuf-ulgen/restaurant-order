using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Domain;

public class BranchTests
{
    [Fact]
    public void Create_WithValidParameters_CreatesActiveBranchWithDefaults()
    {
        var tenantId = TenantId.New();
        var brand = Brand.Create(tenantId, "Original Brand", "original-brand");
        var before = DateTime.UtcNow;

        var branch = Branch.Create(tenantId, brand, "Kadıköy Şube", "kadikoy");
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, branch.Id.Value);
        Assert.Equal(tenantId, branch.TenantId);
        Assert.Equal(brand.Id, branch.BrandId);
        Assert.Equal("Kadıköy Şube", branch.Name);
        Assert.Equal("kadikoy", branch.Slug.Value);
        Assert.Equal("Europe/Istanbul", branch.Timezone.Id);
        Assert.Equal("TRY", branch.Currency.Code);
        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.InRange(branch.CreatedAtUtc, before, after);
        Assert.Null(branch.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, branch.ConcurrencyToken);
    }

    [Fact]
    public void Create_WithExplicitTimezoneAndCurrency_PreservesValues()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var branch = Branch.Create(
            tenantId,
            brandId,
            "London Branch",
            "london-soho",
            "Europe/London",
            "GBP");

        Assert.Equal("Europe/London", branch.Timezone.Id);
        Assert.Equal("GBP", branch.Currency.Code);
    }

    [Fact]
    public void Create_WhenTenantDoesNotMatchBrandTenant_ThrowsDomainException()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var brandOfTenantA = Brand.Create(tenantA, "Brand A", "brand-a");

        var ex = Assert.Throws<DomainException>(() =>
            Branch.Create(tenantB, brandOfTenantA, "Branch B", "branch-b"));

        Assert.Contains("Tenant mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithInvalidName_ThrowsDomainException(string? invalidName)
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var ex = Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brandId, invalidName!, "valid-slug"));

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
        var brandId = BrandId.New();

        Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brandId, "Valid Name", invalidSlug));
    }

    [Theory]
    [InlineData("Invalid/Timezone")]
    [InlineData("Not_A_Timezone")]
    [InlineData("")]
    public void Create_WithUnsupportedTimezone_ThrowsDomainException(string invalidTimezone)
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brandId, "Valid Name", "valid-slug", invalidTimezone));
    }

    [Theory]
    [InlineData("try")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("XYZ")]
    [InlineData("")]
    public void Create_WithInvalidCurrency_ThrowsDomainException(string invalidCurrency)
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        Assert.Throws<DomainException>(() =>
            Branch.Create(tenantId, brandId, "Valid Name", "valid-slug", "Europe/Istanbul", invalidCurrency));
    }

    [Fact]
    public void UpdateDetails_WithValidData_UpdatesBranchAndTouchesTimestamp()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Old Name", "old-name");
        var originalToken = branch.ConcurrencyToken;

        branch.UpdateDetails("New Name", "UTC", "USD");

        Assert.Equal("New Name", branch.Name);
        Assert.Equal("UTC", branch.Timezone.Id);
        Assert.Equal("USD", branch.Currency.Code);
        Assert.NotNull(branch.UpdatedAtUtc);
        Assert.NotEqual(originalToken, branch.ConcurrencyToken);
    }

    [Fact]
    public void UpdateDetails_WhenClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() =>
            branch.UpdateDetails("New Name", "UTC", "USD"));

        Assert.Contains("not allowed on a closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspend_WhenActive_TransitionsToSuspended()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");

        branch.Suspend("Kitchen renovation");

        Assert.Equal(BranchStatus.Suspended, branch.Status);
        Assert.NotNull(branch.UpdatedAtUtc);
    }

    [Fact]
    public void Suspend_WhenAlreadySuspended_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Suspend();
        var token = branch.ConcurrencyToken;

        branch.Suspend();

        Assert.Equal(BranchStatus.Suspended, branch.Status);
        Assert.Equal(token, branch.ConcurrencyToken);
    }

    [Fact]
    public void Suspend_WhenClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() => branch.Suspend());
        Assert.Contains("Cannot suspend a closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_WhenSuspended_TransitionsToActive()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Suspend();

        branch.Activate();

        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.NotNull(branch.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        var token = branch.ConcurrencyToken;

        branch.Activate();

        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.Equal(token, branch.ConcurrencyToken);
    }

    [Fact]
    public void Activate_WhenClosed_ThrowsDomainException()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() => branch.Activate());
        Assert.Contains("Cannot activate a permanently closed branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_WhenActiveOrSuspended_TransitionsToClosedPermanently()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var activeBranch = Branch.Create(tenantId, brandId, "Active Branch", "active-branch");
        activeBranch.Close("Lease expired");
        Assert.Equal(BranchStatus.Closed, activeBranch.Status);

        var suspendedBranch = Branch.Create(tenantId, brandId, "Suspended Branch", "suspended-branch");
        suspendedBranch.Suspend();
        suspendedBranch.Close();
        Assert.Equal(BranchStatus.Closed, suspendedBranch.Status);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_IsIdempotent()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branch = Branch.Create(tenantId, brandId, "Test Branch", "test-branch");
        branch.Close();
        var token = branch.ConcurrencyToken;

        branch.Close();

        Assert.Equal(BranchStatus.Closed, branch.Status);
        Assert.Equal(token, branch.ConcurrencyToken);
    }
}
