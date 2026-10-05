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
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogEndpointsHandlerUnitTests
{
    private readonly Mock<ICatalogService> _mockService = new();
    private readonly Mock<IJwtClaimPrincipalParser> _mockParser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _menuId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticatedPrincipal _actor;

    public CatalogEndpointsHandlerUnitTests()
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

    private MenuDto MakeMenu(Guid? id = null, Guid? token = null) =>
        new(id ?? _menuId, _tenantId, _branchId, "Main Menu", "main-menu", "Description", "Draft", 1,
            DateTime.UtcNow, null, token ?? Guid.NewGuid());

    private MenuCategoryDto MakeCategory(Guid? id = null, Guid? token = null) =>
        new(id ?? Guid.NewGuid(), _tenantId, _branchId, _menuId, "Starters", "starters", "Mezze", 1, true,
            DateTime.UtcNow, null, token ?? Guid.NewGuid());

    [Fact]
    public async Task ListMenusHandler_ReturnsOk_WithMenus()
    {
        var menus = new List<MenuDto> { MakeMenu() };
        _mockService.Setup(s => s.ListMenusAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menus);

        var ctx = new DefaultHttpContext();
        var result = await CatalogEndpoints.ListMenusHandler(
            _branchId, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        var okResult = Assert.IsAssignableFrom<Ok<IReadOnlyList<MenuDto>>>(result);
        Assert.Single(okResult.Value!);
    }

    [Fact]
    public async Task GetMenuHandler_ReturnsOkWithETag_WhenFound()
    {
        var menu = MakeMenu();
        _mockService.Setup(s => s.GetMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);

        var ctx = new DefaultHttpContext();
        var result = await CatalogEndpoints.GetMenuHandler(
            _branchId, _menuId, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        Assert.IsAssignableFrom<Ok<MenuDto>>(result);
        Assert.Equal($"\"{menu.ConcurrencyToken:D}\"", ctx.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task GetMenuHandler_ReturnsNotFound_WhenMissing()
    {
        _mockService.Setup(s => s.GetMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Menu not found"));

        var ctx = new DefaultHttpContext();
        var result = await CatalogEndpoints.GetMenuHandler(
            _branchId, _menuId, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        var prob = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, prob.StatusCode);
    }

    [Fact]
    public async Task CreateMenuHandler_ReturnsCreated_WithETag()
    {
        var menu = MakeMenu();
        _mockService.Setup(s => s.CreateMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), It.IsAny<CreateMenuCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);

        var ctx = new DefaultHttpContext();
        var req = new CreateMenuApiRequest("Main Menu", "main-menu", "Description", 1);
        var result = await CatalogEndpoints.CreateMenuHandler(
            _branchId, req, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        var created = Assert.IsAssignableFrom<Created<MenuDto>>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal($"\"{menu.ConcurrencyToken:D}\"", ctx.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task UpdateMenuHandler_ExtractsIfMatchHeader_AndReturnsOk()
    {
        var token = Guid.NewGuid();
        var updated = MakeMenu(token: Guid.NewGuid());

        _mockService.Setup(s => s.UpdateMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId),
                It.Is<UpdateMenuCommand>(c => c.ConcurrencyToken == token), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfMatch = $"\"{token:D}\"";

        var req = new UpdateMenuApiRequest("Updated Menu", null, 2, ConcurrencyToken: null);
        var result = await CatalogEndpoints.UpdateMenuHandler(
            _branchId, _menuId, req, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        Assert.IsAssignableFrom<Ok<MenuDto>>(result);
    }

    [Fact]
    public async Task ActivateMenuHandler_CallsService_ReturnsOk()
    {
        var token = Guid.NewGuid();
        var active = MakeMenu(token: Guid.NewGuid());

        _mockService.Setup(s => s.ActivateMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);

        var ctx = new DefaultHttpContext();
        var req = new MenuStateApiRequest(token);
        var result = await CatalogEndpoints.ActivateMenuHandler(
            _branchId, _menuId, req, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        Assert.IsAssignableFrom<Ok<MenuDto>>(result);
    }

    [Fact]
    public async Task ArchiveMenuHandler_CallsService_ReturnsOk()
    {
        var token = Guid.NewGuid();
        var archived = MakeMenu(token: Guid.NewGuid());

        _mockService.Setup(s => s.ArchiveMenuAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archived);

        var ctx = new DefaultHttpContext();
        var req = new MenuStateApiRequest(token);
        var result = await CatalogEndpoints.ArchiveMenuHandler(
            _branchId, _menuId, req, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        Assert.IsAssignableFrom<Ok<MenuDto>>(result);
    }

    [Fact]
    public async Task CategoryEndpoints_CrudHandlers_WorkCorrectly()
    {
        var categoryId = Guid.NewGuid();
        var category = MakeCategory(categoryId);

        // List
        _mockService.Setup(s => s.ListCategoriesAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MenuCategoryDto> { category });

        var ctx1 = new DefaultHttpContext();
        var listRes = await CatalogEndpoints.ListCategoriesHandler(
            _branchId, _menuId, ctx1, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<IReadOnlyList<MenuCategoryDto>>>(listRes);

        // Get
        _mockService.Setup(s => s.GetCategoryAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), new MenuCategoryId(categoryId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var ctx2 = new DefaultHttpContext();
        var getRes = await CatalogEndpoints.GetCategoryHandler(
            _branchId, _menuId, categoryId, ctx2, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<MenuCategoryDto>>(getRes);

        // Create
        _mockService.Setup(s => s.CreateCategoryAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), It.IsAny<CreateMenuCategoryCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var ctx3 = new DefaultHttpContext();
        var createReq = new CreateMenuCategoryApiRequest("Starters", "starters", null, 0);
        var createRes = await CatalogEndpoints.CreateCategoryHandler(
            _branchId, _menuId, createReq, ctx3, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Created<MenuCategoryDto>>(createRes);

        // Activate & Deactivate
        _mockService.Setup(s => s.ActivateCategoryAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), new MenuCategoryId(categoryId), It.IsAny<Guid?>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        var actRes = await CatalogEndpoints.ActivateCategoryHandler(
            _branchId, _menuId, categoryId, new MenuCategoryStateApiRequest(category.ConcurrencyToken), new DefaultHttpContext(), _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<MenuCategoryDto>>(actRes);

        _mockService.Setup(s => s.DeactivateCategoryAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new MenuId(_menuId), new MenuCategoryId(categoryId), It.IsAny<Guid?>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        var deactRes = await CatalogEndpoints.DeactivateCategoryHandler(
            _branchId, _menuId, categoryId, new MenuCategoryStateApiRequest(category.ConcurrencyToken), new DefaultHttpContext(), _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<MenuCategoryDto>>(deactRes);
    }

    [Fact]
    public async Task ReorderCategoriesHandler_MissingItemToken_Returns412PreconditionFailed()
    {
        var ctx = new DefaultHttpContext();
        var req = new ReorderCategoriesApiRequest(
        [
            new CategoryReorderItemApiRequest(Guid.NewGuid(), 1, ConcurrencyToken: null) // Missing token!
        ]);

        var result = await CatalogEndpoints.ReorderCategoriesHandler(
            _branchId, _menuId, req, ctx, _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        var prob = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, prob.StatusCode);
    }

    [Fact]
    public async Task ReorderCategoriesHandler_EmptyItems_ReturnsBadRequest()
    {
        var result = await CatalogEndpoints.ReorderCategoriesHandler(
            _branchId, _menuId, new ReorderCategoriesApiRequest([]), new DefaultHttpContext(),
            _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task ReorderItemsHandler_RejectsEmptyAndMissingTokens()
    {
        var emptyResult = await CatalogEndpoints.ReorderItemsHandler(
            _branchId, _menuId, Guid.NewGuid(), new ReorderMenuItemsApiRequest([]), new DefaultHttpContext(),
            _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(emptyResult).StatusCode);

        var missingTokenResult = await CatalogEndpoints.ReorderItemsHandler(
            _branchId, _menuId, Guid.NewGuid(), new ReorderMenuItemsApiRequest(
                [new ItemReorderItemApiRequest(Guid.NewGuid(), 1)]), new DefaultHttpContext(),
            _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed,
            Assert.IsAssignableFrom<ProblemHttpResult>(missingTokenResult).StatusCode);
    }

    [Fact]
    public async Task ReorderVariantsHandler_RejectsEmptyAndMissingTokens()
    {
        var itemId = Guid.NewGuid();
        var emptyResult = await CatalogEndpoints.ReorderVariantsHandler(
            _branchId, _menuId, itemId, new ReorderItemVariantsApiRequest([]), new DefaultHttpContext(),
            _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(emptyResult).StatusCode);

        var missingTokenResult = await CatalogEndpoints.ReorderVariantsHandler(
            _branchId, _menuId, itemId, new ReorderItemVariantsApiRequest(
                [new VariantReorderItemApiRequest(Guid.NewGuid(), 1)]), new DefaultHttpContext(),
            _mockService.Object, _tenantContext, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed,
            Assert.IsAssignableFrom<ProblemHttpResult>(missingTokenResult).StatusCode);
    }
}
