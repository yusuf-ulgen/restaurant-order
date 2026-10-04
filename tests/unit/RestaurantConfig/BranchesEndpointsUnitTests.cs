using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class BranchesEndpointsUnitTests
{
    private readonly Mock<IRestaurantConfigService> _mockService = new();
    private readonly Mock<IJwtClaimPrincipalParser> _mockParser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticatedPrincipal _adminActor;
    private readonly AuthenticatedPrincipal _waiterActor;

    public BranchesEndpointsUnitTests()
    {
        _tenantContext = new TenantContext(_tenantId, isAuthenticated: true);
        _adminActor = new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: PrincipalType.Staff,
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(new TenantId(_tenantId)),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        _waiterActor = new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: PrincipalType.Staff,
            role: AuthRole.Waiter,
            scope: AuthorizationScope.ForBranch(new TenantId(_tenantId), new BranchId(_branchId)),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        _mockParser.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(_adminActor);
    }

    // BranchDto(Id, TenantId, BrandId, Name, Slug, Timezone, Currency, Status, CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken)
    private BranchDto MakeBranch(Guid? id = null, Guid? brandId = null, Guid? token = null) =>
        new(id ?? _branchId, _tenantId, brandId ?? Guid.NewGuid(),
            "Kadikoy", "kadikoy", "Europe/Istanbul", "TRY", "Active",
            DateTime.UtcNow, null, token ?? Guid.NewGuid());

    [Fact]
    public async Task ListBranchesHandler_RolePermissionAndFiltering_Branches()
    {
        var ctx = new DefaultHttpContext();

        // 1. Forbidden role (e.g. Waiter) -> 403
        var mockWaiterParser = new Mock<IJwtClaimPrincipalParser>();
        mockWaiterParser.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(_waiterActor);

        var rForbidden = await RestaurantConfigEndpoints.ListBranchesHandler(
            null, _tenantContext, ctx, mockWaiterParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ProblemHttpResult>(rForbidden).StatusCode);

        // 2. Success with brandId filter
        var brandId = Guid.NewGuid();
        var branches = new List<BranchDto> { MakeBranch(brandId: brandId) };

        _mockService.Setup(s => s.ListBranchesAsync(
                new TenantId(_tenantId), _adminActor,
                It.Is<BrandId?>(b => b!.Value.Value == brandId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(branches);

        var rFiltered = await RestaurantConfigEndpoints.ListBranchesHandler(
            brandId, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        var okFiltered = Assert.IsAssignableFrom<Ok<IReadOnlyList<BranchDto>>>(rFiltered);
        Assert.Single(okFiltered.Value!);

        // 3. Success without brandId filter
        _mockService.Setup(s => s.ListBranchesAsync(
                new TenantId(_tenantId), _adminActor, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branches);

        var rAll = await RestaurantConfigEndpoints.ListBranchesHandler(
            null, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<IReadOnlyList<BranchDto>>>(rAll);
    }

    [Fact]
    public async Task GetBranchHandler_AllBranches()
    {
        var ctx = new DefaultHttpContext();

        // 1. Forbidden role -> 403
        var mockWaiterParser = new Mock<IJwtClaimPrincipalParser>();
        mockWaiterParser.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(_waiterActor);

        var rForbidden = await RestaurantConfigEndpoints.GetBranchHandler(
            _branchId, _tenantContext, ctx, mockWaiterParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ProblemHttpResult>(rForbidden).StatusCode);

        // 2. Branch not found -> 404
        _mockService.Setup(s => s.GetBranchByIdAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BranchDto?)null);

        var rNotFound = await RestaurantConfigEndpoints.GetBranchHandler(
            _branchId, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rNotFound).StatusCode);

        // 3. Success -> 200 OK with ETag
        var token = Guid.NewGuid();
        var branch = MakeBranch(id: _branchId, token: token);
        _mockService.Setup(s => s.GetBranchByIdAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var rSuccess = await RestaurantConfigEndpoints.GetBranchHandler(
            _branchId, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<BranchDto>>(rSuccess);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag);

        // 4. InvalidAuthorizationScopeException -> 403
        _mockService.Setup(s => s.GetBranchByIdAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _adminActor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("Cross tenant branch access denied"));

        var rScopeErr = await RestaurantConfigEndpoints.GetBranchHandler(
            _branchId, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ProblemHttpResult>(rScopeErr).StatusCode);
    }

    [Fact]
    public async Task CreateBranchHandler_SuccessAndException_Branches()
    {
        var token = Guid.NewGuid();
        var branch = MakeBranch(token: token);

        _mockService.Setup(s => s.CreateBranchAsync(
                new TenantId(_tenantId), It.IsAny<CreateBranchCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var ctx = new DefaultHttpContext();
        var req = new CreateBranchApiRequest(branch.BrandId, "Besiktas", "besiktas", "Europe/Istanbul", "TRY");

        var result = await RestaurantConfigEndpoints.CreateBranchHandler(
            req, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        var created = Assert.IsAssignableFrom<Created<BranchDto>>(result);
        Assert.Equal(branch, created.Value);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag);

        // Exception branch -> 409
        _mockService.Setup(s => s.CreateBranchAsync(
                new TenantId(_tenantId), It.IsAny<CreateBranchCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateSlugException("Slug in use"));

        var errResult = await RestaurantConfigEndpoints.CreateBranchHandler(
            req, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(errResult).StatusCode);
    }

    [Fact]
    public async Task UpdateBranchHandler_TokenAndException_Branches()
    {
        var token = Guid.NewGuid();
        var ctx = new DefaultHttpContext();

        // 1. Missing token -> 412
        var reqNoToken = new UpdateBranchApiRequest("Moda", "Europe/Istanbul", "TRY", ConcurrencyToken: null);
        var rNoToken = await RestaurantConfigEndpoints.UpdateBranchHandler(
            _branchId, reqNoToken, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rNoToken).StatusCode);

        // 2. Success with token
        var updated = MakeBranch(id: _branchId, token: token);
        _mockService.Setup(s => s.UpdateBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<UpdateBranchCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var reqToken = new UpdateBranchApiRequest("Moda", "Europe/Istanbul", "TRY", ConcurrencyToken: token);
        var rSuccess = await RestaurantConfigEndpoints.UpdateBranchHandler(
            _branchId, reqToken, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<BranchDto>>(rSuccess);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag);

        // 3. Exception branch -> 404
        _mockService.Setup(s => s.UpdateBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<UpdateBranchCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Branch missing"));

        var rErr = await RestaurantConfigEndpoints.UpdateBranchHandler(
            _branchId, reqToken, _tenantContext, ctx, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rErr).StatusCode);
    }

    [Fact]
    public async Task StateChangeHandlers_TokenAndException_Branches()
    {
        var token = Guid.NewGuid();
        var branch = MakeBranch(id: _branchId, token: token);

        // Activate: missing token -> 412
        var ctx1 = new DefaultHttpContext();
        var rActNoToken = await RestaurantConfigEndpoints.ActivateBranchHandler(
            _branchId, null, _tenantContext, ctx1, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rActNoToken).StatusCode);

        // Activate: success
        _mockService.Setup(s => s.ActivateBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<BranchStateChangeCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        var rActSuccess = await RestaurantConfigEndpoints.ActivateBranchHandler(
            _branchId, new BranchStateApiRequest(ConcurrencyToken: token), _tenantContext, ctx1, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<BranchDto>>(rActSuccess);

        // Suspend: missing token -> 412
        var ctx2 = new DefaultHttpContext();
        var rSuspNoToken = await RestaurantConfigEndpoints.SuspendBranchHandler(
            _branchId, null, _tenantContext, ctx2, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rSuspNoToken).StatusCode);

        // Suspend: success
        _mockService.Setup(s => s.SuspendBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<BranchStateChangeCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        var rSuspSuccess = await RestaurantConfigEndpoints.SuspendBranchHandler(
            _branchId, new BranchStateApiRequest(ConcurrencyToken: token), _tenantContext, ctx2, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<BranchDto>>(rSuspSuccess);

        // Close: missing token -> 412
        var ctx3 = new DefaultHttpContext();
        var rCloseNoToken = await RestaurantConfigEndpoints.CloseBranchHandler(
            _branchId, null, _tenantContext, ctx3, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rCloseNoToken).StatusCode);

        // Close: success
        _mockService.Setup(s => s.CloseBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<BranchStateChangeCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        var rCloseSuccess = await RestaurantConfigEndpoints.CloseBranchHandler(
            _branchId, new BranchStateApiRequest(ConcurrencyToken: token), _tenantContext, ctx3, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<BranchDto>>(rCloseSuccess);

        // Close: exception -> 409
        _mockService.Setup(s => s.CloseBranchAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<BranchStateChangeCommand>(), _adminActor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("State conflict"));
        var rCloseErr = await RestaurantConfigEndpoints.CloseBranchHandler(
            _branchId, new BranchStateApiRequest(ConcurrencyToken: token), _tenantContext, ctx3, _mockParser.Object, _mockService.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(rCloseErr).StatusCode);
    }
}
