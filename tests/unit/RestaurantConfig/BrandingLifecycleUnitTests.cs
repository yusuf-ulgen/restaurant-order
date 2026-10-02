using RestaurantOrder.Domain.Branding;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class BrandingLifecycleUnitTests
{
    [Theory]
    [InlineData("#ffffff", "#ffffff")]
    [InlineData("#FFF", "#ffffff")]
    [InlineData("#1a2b3c", "#1a2b3c")]
    [InlineData("#000000", "#000000")]
    [InlineData("#000", "#000000")]
    [InlineData("#F0A", "#ff00aa")]
    public void ColorHex_ValidHex_NormalizesAndPasses(string input, string expected)
    {
        var color = new ColorHex(input);
        Assert.Equal(expected, color.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("rgb(0,0,0)")]
    [InlineData("rgba(0,0,0,0)")]
    [InlineData("var(--primary)")]
    [InlineData("#12345678")] // 8-digit alpha disallowed
    [InlineData("#12")]
    [InlineData("#1234")]
    [InlineData("123456")] // missing leading #
    [InlineData("<script>")]
    public void ColorHex_InvalidHex_ThrowsDomainException(string input)
    {
        Assert.Throws<DomainException>(() => new ColorHex(input));
    }

    [Theory]
    [InlineData("/assets/logos/brand.png")]
    [InlineData("/static/img/favicon.ico")]
    [InlineData("https://cdn.example.com/logo.svg")]
    [InlineData("https://images.example.com/path/to/img.png?v=123")]
    public void AssetUrl_ValidSafeUrl_Passes(string input)
    {
        var asset = new AssetUrl(input);
        Assert.Equal(input, asset.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://insecure.example.com/logo.png")] // Insecure HTTP
    [InlineData("javascript:alert(1)")] // XSS vector
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==")] // Data URI scheme
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("/path/with/../traversal/logo.png")] // Traversal
    [InlineData("/path/with\\backslash")]
    public void AssetUrl_InvalidOrUnsafeUrl_ThrowsDomainException(string input)
    {
        Assert.Throws<DomainException>(() => new AssetUrl(input));
    }

    [Theory]
    [InlineData("Brand <b>Name</b>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Safe name with & ampersand")] // & is allowed, but < and > are forbidden
    public void BrandAppearance_HtmlTagsInTextFields_ThrowsDomainException(string testString)
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        if (testString.Contains('<') || testString.Contains('>'))
        {
            Assert.Throws<DomainException>(() => BrandAppearance.Create(
                tenantId: tenantId,
                brandId: brandId,
                displayName: testString));
        }
    }

    [Fact]
    public void BrandAppearance_CreateValid_InstantiatesDefaults()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var appearance = BrandAppearance.Create(
            tenantId: tenantId,
            brandId: brandId,
            displayName: "Artisan Burger Brand");

        appearance.UpdateDetails(
            displayName: "Artisan Burger Brand",
            logoUrl: "https://cdn.example.com/brand-logo.png",
            faviconUrl: null,
            primaryColor: "#112233",
            primaryHoverColor: null,
            secondaryColor: null,
            accentColor: null,
            surfaceColor: null,
            backgroundColor: null,
            footerText: null,
            defaultShellTitle: null,
            defaultShellSubtitle: null);

        Assert.Equal("Artisan Burger Brand", appearance.DisplayName);
        Assert.Equal("https://cdn.example.com/brand-logo.png", appearance.LogoUrl?.Value);
        Assert.Equal("#112233", appearance.PrimaryColor.Value);
        Assert.Equal(BrandAppearance.DefaultSecondaryColor.Value, appearance.SecondaryColor.Value);
        Assert.NotEqual(Guid.Empty, appearance.ConcurrencyToken);
        Assert.NotNull(appearance.UpdatedAtUtc);
    }

    [Fact]
    public void BrandAppearance_Update_UpdatesFieldsAndRegeneratesConcurrencyToken()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();

        var appearance = BrandAppearance.Create(
            tenantId: tenantId,
            brandId: brandId,
            displayName: "Initial Brand");

        var initialToken = appearance.ConcurrencyToken;

        appearance.UpdateDetails(
            displayName: "Updated Brand Name",
            logoUrl: null,
            faviconUrl: null,
            primaryColor: "#FF0055",
            primaryHoverColor: null,
            secondaryColor: null,
            accentColor: null,
            surfaceColor: null,
            backgroundColor: null,
            footerText: null,
            defaultShellTitle: null,
            defaultShellSubtitle: null);

        Assert.Equal("Updated Brand Name", appearance.DisplayName);
        Assert.Equal("#ff0055", appearance.PrimaryColor.Value);
        Assert.NotNull(appearance.UpdatedAtUtc);
        Assert.NotEqual(initialToken, appearance.ConcurrencyToken);
    }

    [Fact]
    public void BranchThemeOverride_CreateAndUpdate_ValidatesAndRegeneratesToken()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();

        var branchOverride = BranchThemeOverride.Create(
            tenantId: tenantId,
            branchId: branchId);

        branchOverride.UpdateDetails(
            displayName: "Kadıköy Branch",
            logoUrl: null,
            headerSubtitle: "Best artisan burgers in Moda",
            footerBranchInfo: null);

        Assert.Equal("Kadıköy Branch", branchOverride.DisplayName);
        Assert.Equal("Best artisan burgers in Moda", branchOverride.HeaderSubtitle);
        Assert.NotEqual(Guid.Empty, branchOverride.ConcurrencyToken);

        var initialToken = branchOverride.ConcurrencyToken;

        branchOverride.UpdateDetails(
            displayName: "Kadıköy Express",
            logoUrl: null,
            headerSubtitle: "Moda Caddesi No: 42",
            footerBranchInfo: "Kadikoy / Istanbul");

        Assert.Equal("Kadıköy Express", branchOverride.DisplayName);
        Assert.Equal("Moda Caddesi No: 42", branchOverride.HeaderSubtitle);
        Assert.Equal("Kadikoy / Istanbul", branchOverride.FooterBranchInfo);
        Assert.NotEqual(initialToken, branchOverride.ConcurrencyToken);
    }
}
