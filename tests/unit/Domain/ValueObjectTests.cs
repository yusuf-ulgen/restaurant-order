using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Domain;

public class ValueObjectTests
{
    [Theory]
    [InlineData("kadikoy")]
    [InlineData("burger-lab")]
    [InlineData("branch-123")]
    [InlineData("a-b")]
    public void Slug_WithValidValue_NormalizesAndStores(string input)
    {
        var slug = new Slug(input);
        Assert.Equal(input, slug.Value);
        Assert.Equal(input, slug.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a")] // Less than 2 chars
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("UPPERCASE")]
    [InlineData("with spaces")]
    [InlineData("with_underscores")]
    [InlineData("special@char")]
    public void Slug_WithInvalidValue_ThrowsDomainException(string? input)
    {
        Assert.Throws<DomainException>(() => new Slug(input!));
    }

    [Theory]
    [InlineData("TRY")]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void Currency_WithRecognizedIsoCode_CreatesSuccessfully(string code)
    {
        var currency = new Currency(code);
        Assert.Equal(code, currency.Code);
        Assert.Equal(code, currency.ToString());
    }

    [Fact]
    public void Currency_Default_ReturnsTRY()
    {
        var currency = Currency.Default;
        Assert.Equal("TRY", currency.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("try")] // Lowercase
    [InlineData("US")]  // 2 chars
    [InlineData("USDD")] // 4 chars
    [InlineData("XYZ")]  // Unrecognized
    [InlineData("123")]  // Digits
    public void Currency_WithInvalidCode_ThrowsDomainException(string? code)
    {
        Assert.Throws<DomainException>(() => new Currency(code!));
    }

    [Theory]
    [InlineData("Europe/Istanbul")]
    [InlineData("UTC")]
    [InlineData("America/New_York")]
    [InlineData("Europe/London")]
    public void Timezone_WithValidIanaId_CreatesSuccessfully(string id)
    {
        var timezone = new Timezone(id);
        Assert.Equal(id, timezone.Id);
        Assert.Equal(id, timezone.ToString());
    }

    [Fact]
    public void Timezone_Default_ReturnsEuropeIstanbul()
    {
        var timezone = Timezone.Default;
        Assert.Equal("Europe/Istanbul", timezone.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Not/A_Real_Timezone")]
    [InlineData("InvalidZone")]
    public void Timezone_WithInvalidId_ThrowsDomainException(string? id)
    {
        Assert.Throws<DomainException>(() => new Timezone(id!));
    }

    [Fact]
    public void StronglyTypedIds_New_CreatesUuidV7()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branchId = BranchId.New();

        Assert.NotEqual(Guid.Empty, tenantId.Value);
        Assert.NotEqual(Guid.Empty, brandId.Value);
        Assert.NotEqual(Guid.Empty, branchId.Value);

        // UUIDv7 version nibble verification (version is 7 in bits 48-51, big-endian)
        var bytes = tenantId.Value.ToByteArray();
        // In .NET Guid byte array: byte 7 holds version in high 4 bits on little-endian
        // Guid.Version property exists in .NET 9+
        Assert.Equal(7, tenantId.Value.Version);
        Assert.Equal(7, brandId.Value.Version);
        Assert.Equal(7, branchId.Value.Version);
    }

    [Fact]
    public void StronglyTypedIds_FromEmptyGuid_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => TenantId.From(Guid.Empty));
        Assert.Throws<DomainException>(() => BrandId.From(Guid.Empty));
        Assert.Throws<DomainException>(() => BranchId.From(Guid.Empty));
    }
}
