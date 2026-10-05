using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogAvailabilityEndpointsUnitTests
{
    private readonly Mock<ICatalogService> _mockService = new();
    private readonly Mock<IJwtClaimPrincipalParser> _mockParser = new();
    private readonly IPermissionRegistry _permissionRegistry = new PermissionRegistry();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _menuId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _variantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticatedPrincipal _actor;

    public CatalogAvailabilityEndpointsUnitTests()
    {
        _tenantContext = new TenantContext(_tenantId, isAuthenticated: true);
        _actor = new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: PrincipalType.Staff,
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(new TenantId(_tenantId)),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        _mockParser.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(_actor);
    }

    private BranchItemAvailabilityDto MakeAvailability(Guid? itemId = null, Guid? variantId = null, bool isAvailable = false, Guid? token = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: _tenantId,
            BranchId: _branchId,
            MenuItemId: itemId ?? _itemId,
            ItemVariantId: variantId,
            IsAvailable: isAvailable,
            ReasonCode: isAvailable ? "Restocked" : "SoldOut",
            Note: "Test note",
            ExpectedAvailableAtUtc: null,
            ChangedByUserId: _actor.SubjectId,
            ChangedAtUtc: DateTime.UtcNow,
            ConcurrencyToken: token ?? Guid.NewGuid());

    [Fact]
    public void AvailabilityResult_SetsETagHeaderAndReturnsOk()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var availability = MakeAvailability(token: token);

        var result = CatalogEndpoints.AvailabilityResult(httpContext, availability);
        Assert.IsType<Ok<BranchItemAvailabilityDto>>(result);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task ListBranchAvailabilitiesHandler_ReturnsOk_WithAvailabilities()
    {
        var list = new List<BranchItemAvailabilityDto> { MakeAvailability() };
        _mockService.Setup(s => s.ListBranchAvailabilitiesAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var ctx = new DefaultHttpContext();
        var result = await CatalogEndpoints.ListBranchAvailabilitiesHandler(
            _branchId, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var okResult = Assert.IsAssignableFrom<Ok<IReadOnlyList<BranchItemAvailabilityDto>>>(result);
        Assert.Single(okResult.Value!);
    }

    [Fact]
    public async Task Quick86ItemHandler_ExtractsIfMatchHeader_AndReturnsOkWithETag()
    {
        var token = Guid.NewGuid();
        var availability = MakeAvailability(token: token);
        _mockService.Setup(s => s.Quick86ItemAsync(
                new TenantId(_tenantId),
                new BranchId(_branchId),
                new MenuId(_menuId),
                new MenuItemId(_itemId),
                It.Is<Quick86ItemCommand>(c => c.ConcurrencyToken == token && c.ReasonCode == "SoldOut"),
                _actor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfMatch = $"\"{token:D}\"";

        var cmd = new Quick86ItemCommand("SoldOut", "Out of stock today");
        var result = await CatalogEndpoints.Quick86ItemHandler(
            _branchId, _menuId, _itemId, cmd, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var okResult = Assert.IsAssignableFrom<Ok<BranchItemAvailabilityDto>>(result);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag.ToString());
        Assert.False(okResult.Value!.IsAvailable);
    }

    [Fact]
    public async Task Quick86ItemHandler_MissingConcurrencyToken_ReturnsPreconditionFailed()
    {
        _mockService.Setup(s => s.Quick86ItemAsync(
                It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<MenuId>(), It.IsAny<MenuItemId>(),
                It.IsAny<Quick86ItemCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyPreconditionException("Concurrency token is required."));

        var ctx = new DefaultHttpContext();
        var cmd = new Quick86ItemCommand("SoldOut");
        var result = await CatalogEndpoints.Quick86ItemHandler(
            _branchId, _menuId, _itemId, cmd, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
    }

    [Fact]
    public async Task Quick86ItemHandler_StaleConcurrencyToken_ReturnsConflict()
    {
        _mockService.Setup(s => s.Quick86ItemAsync(
                It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<MenuId>(), It.IsAny<MenuItemId>(),
                It.IsAny<Quick86ItemCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Stale token"));

        var ctx = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var cmd = new Quick86ItemCommand("SoldOut", ConcurrencyToken: token);
        var result = await CatalogEndpoints.Quick86ItemHandler(
            _branchId, _menuId, _itemId, cmd, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task RestockItemHandler_ReturnsOk_WithETag()
    {
        var token = Guid.NewGuid();
        var availability = MakeAvailability(isAvailable: true, token: token);
        _mockService.Setup(s => s.RestockItemAsync(
                new TenantId(_tenantId),
                new BranchId(_branchId),
                new MenuId(_menuId),
                new MenuItemId(_itemId),
                It.IsAny<RestockItemCommand>(),
                _actor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var ctx = new DefaultHttpContext();
        var cmd = new RestockItemCommand("Restocked fresh batch", token);
        var result = await CatalogEndpoints.RestockItemHandler(
            _branchId, _menuId, _itemId, cmd, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var okResult = Assert.IsAssignableFrom<Ok<BranchItemAvailabilityDto>>(result);
        Assert.True(okResult.Value!.IsAvailable);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Quick86VariantHandler_And_RestockVariantHandler_WorkAsExpected()
    {
        var token = Guid.NewGuid();
        var unavailableVariant = MakeAvailability(variantId: _variantId, isAvailable: false, token: token);
        _mockService.Setup(s => s.Quick86VariantAsync(
                new TenantId(_tenantId),
                new BranchId(_branchId),
                new MenuId(_menuId),
                new MenuItemId(_itemId),
                new ItemVariantId(_variantId),
                It.IsAny<Quick86VariantCommand>(),
                _actor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unavailableVariant);

        var ctx = new DefaultHttpContext();
        var cmd86 = new Quick86VariantCommand("IngredientUnavailable", "No syrup", ConcurrencyToken: token);
        var res86 = await CatalogEndpoints.Quick86VariantHandler(
            _branchId, _menuId, _itemId, _variantId, cmd86, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var ok86 = Assert.IsAssignableFrom<Ok<BranchItemAvailabilityDto>>(res86);
        Assert.False(ok86.Value!.IsAvailable);
        Assert.Equal(_variantId, ok86.Value.ItemVariantId);

        var restockedVariant = MakeAvailability(variantId: _variantId, isAvailable: true, token: Guid.NewGuid());
        _mockService.Setup(s => s.RestockVariantAsync(
                new TenantId(_tenantId),
                new BranchId(_branchId),
                new MenuId(_menuId),
                new MenuItemId(_itemId),
                new ItemVariantId(_variantId),
                It.IsAny<RestockVariantCommand>(),
                _actor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(restockedVariant);

        var ctxRestock = new DefaultHttpContext();
        var cmdRestock = new RestockVariantCommand("Syrup restocked", token);
        var resRestock = await CatalogEndpoints.RestockVariantHandler(
            _branchId, _menuId, _itemId, _variantId, cmdRestock, ctxRestock, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var okRestock = Assert.IsAssignableFrom<Ok<BranchItemAvailabilityDto>>(resRestock);
        Assert.True(okRestock.Value!.IsAvailable);
    }

    [Fact]
    public async Task GetRuntimeMenuHandler_ReturnsOk_WithRuntimeMenuModel()
    {
        var model = new RuntimeMenuReadModel(
            BranchId: _branchId,
            Currency: "TRY",
            Menus: new List<RuntimeMenuDto>
            {
                new(
                    Id: _menuId,
                    Name: "Lunch Menu",
                    Slug: "lunch-menu",
                    Description: "Daily specials",
                    SortOrder: 1,
                    Categories: Array.Empty<RuntimeMenuCategoryDto>())
            });

        _mockService.Setup(s => s.GetRuntimeMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var ctx = new DefaultHttpContext();
        var result = await CatalogEndpoints.GetRuntimeMenuHandler(
            _branchId, ctx, _tenantContext, _mockParser.Object, _mockService.Object, CancellationToken.None);

        var okResult = Assert.IsAssignableFrom<Ok<RuntimeMenuReadModel>>(result);
        Assert.Equal("TRY", okResult.Value!.Currency);
        Assert.Single(okResult.Value.Menus);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Full)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Full)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Full)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void MenuInventoryQuick86_Permission_AdheresToRbacSpecifications(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _permissionRegistry.GetGrantType(role, Permissions.MenuInventoryQuick86);
        Assert.Equal(expectedGrant, grant);
    }
}
