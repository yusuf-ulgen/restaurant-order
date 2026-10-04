using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogItemEndpointsUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Fact]
    public void ItemResult_SetsETagHeaderAndReturnsOkOrCreated()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var item = new MenuItemDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            MenuId: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            Name: "Cheeseburger",
            Slug: "cheeseburger",
            ShortDescription: "Delicious burger",
            FullDescription: null,
            ImageUrl: "/images/burger.jpg",
            BasePriceMinorUnits: 15000,
            SortOrder: 0,
            IsActive: true,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.ItemResult(httpContext, item, StatusCodes.Status200OK);
        Assert.IsType<Ok<MenuItemDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.ItemResult(createdContext, item, StatusCodes.Status201Created);
        Assert.IsType<Created<MenuItemDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void VariantResult_SetsETagHeaderAndReturnsOkOrCreated()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var variant = new ItemVariantDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            MenuId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            Name: "Double Patty",
            Code: "DBL-PATTY",
            AbsolutePriceMinorUnits: 22000,
            SortOrder: 1,
            IsDefault: true,
            IsActive: true,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.VariantResult(httpContext, variant, StatusCodes.Status200OK);
        Assert.IsType<Ok<ItemVariantDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.VariantResult(createdContext, variant, StatusCodes.Status201Created);
        Assert.IsType<Created<ItemVariantDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void MenuPricingManage_Permission_AdheresToRbacSpecifications(
        AuthRole role,
        PermissionGrantType expectedPricingGrant)
    {
        var pricingGrant = _registry.GetGrantType(role, Permissions.MenuPricingManage);
        Assert.Equal(expectedPricingGrant, pricingGrant);
    }
}
