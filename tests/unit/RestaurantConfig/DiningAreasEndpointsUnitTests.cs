using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;
// RestaurantConfigExceptions (ConcurrencyConflictException, DuplicateCodeException, etc.) live in
// RestaurantOrder.Application.RestaurantConfig and are already brought in by the using above.

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class DiningAreasEndpointsUnitTests
{
    private readonly Mock<IRestaurantConfigService> _mockService = new();
    private readonly Mock<IJwtClaimPrincipalParser> _mockParser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticatedPrincipal _actor;

    public DiningAreasEndpointsUnitTests()
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

    private DiningAreaDto MakeArea(Guid? id = null, Guid? token = null) =>
        new(id ?? Guid.NewGuid(), _tenantId, _branchId, "Salon", "SLN", "Indoor", 1, true,
            token ?? Guid.NewGuid(), DateTime.UtcNow, null);

    [Fact]
    public async Task ListDiningAreasHandler_SuccessAndException_Branches()
    {
        var areas = new List<DiningAreaDto> { MakeArea() };

        _mockService.Setup(s => s.ListDiningAreasAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(areas);

        var ctx = new DefaultHttpContext();
        var okResult = await RestaurantConfigEndpoints.ListDiningAreasHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        var okObj = Assert.IsAssignableFrom<Ok<IReadOnlyList<DiningAreaDto>>>(okResult);
        Assert.Single(okObj.Value!);

        // Exception branch
        _mockService.Setup(s => s.ListDiningAreasAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Invalid area query"));

        var errorResult = await RestaurantConfigEndpoints.ListDiningAreasHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(errorResult).StatusCode);
    }

    [Fact]
    public async Task CreateDiningAreaHandler_SuccessAndException_Branches()
    {
        var token = Guid.NewGuid();
        var created = MakeArea(token: token);

        _mockService.Setup(s => s.CreateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<CreateDiningAreaCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var ctx = new DefaultHttpContext();
        var req = new CreateDiningAreaApiRequest("Teras", "TRS", "Outdoor", 2);

        var result = await RestaurantConfigEndpoints.CreateDiningAreaHandler(
            _branchId, req, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        var createdResult = Assert.IsAssignableFrom<Created<DiningAreaDto>>(result);
        Assert.Equal(created, createdResult.Value);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag);

        // Exception branch
        _mockService.Setup(s => s.CreateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<CreateDiningAreaCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateCodeException("Code already exists"));

        var errResult = await RestaurantConfigEndpoints.CreateDiningAreaHandler(
            _branchId, req, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(errResult).StatusCode);
    }

    [Fact]
    public async Task UpdateDiningAreaHandler_TokenValidationAndExecution_Branches()
    {
        var areaId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // 1. Missing token -> 412
        var ctx1 = new DefaultHttpContext();
        var reqNoToken = new UpdateDiningAreaApiRequest("Bahce", "Garden", ConcurrencyToken: null);
        var r1 = await RestaurantConfigEndpoints.UpdateDiningAreaHandler(
            _branchId, areaId, reqNoToken, _tenantContext, _mockService.Object, ctx1, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r1).StatusCode);

        // 2. Success with token
        var updated = MakeArea(id: areaId, token: token);
        _mockService.Setup(s => s.UpdateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                It.IsAny<UpdateDiningAreaCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var ctx2 = new DefaultHttpContext();
        var reqToken = new UpdateDiningAreaApiRequest("Bahce", "Garden", ConcurrencyToken: token);
        var r2 = await RestaurantConfigEndpoints.UpdateDiningAreaHandler(
            _branchId, areaId, reqToken, _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<DiningAreaDto>>(r2);
        Assert.Equal($"\"{token:D}\"", ctx2.Response.Headers.ETag);

        // 3. Exception branch -> 404
        _mockService.Setup(s => s.UpdateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                It.IsAny<UpdateDiningAreaCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Area not found"));

        var ctx3 = new DefaultHttpContext();
        var r3 = await RestaurantConfigEndpoints.UpdateDiningAreaHandler(
            _branchId, areaId, reqToken, _tenantContext, _mockService.Object, ctx3, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(r3).StatusCode);
    }

    [Fact]
    public async Task ReorderDiningAreasHandler_AllValidationBranches()
    {
        var ctx = new DefaultHttpContext();

        // 1. Items null but OrderedAreaIds present -> 412
        var r1 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: null, OrderedAreaIds: new[] { Guid.NewGuid() }),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r1).StatusCode);

        // 2. Both null -> 400
        var r2 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: null, OrderedAreaIds: null),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(r2).StatusCode);

        // 3. Items contain null ConcurrencyToken -> 412
        var r3 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: new[] { new ReorderItemApiRequest(Guid.NewGuid(), null) }),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r3).StatusCode);

        // 4. Items contain Guid.Empty ConcurrencyToken -> 412
        var r4 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: new[] { new ReorderItemApiRequest(Guid.NewGuid(), Guid.Empty) }),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r4).StatusCode);

        // 5. Success
        var validToken = Guid.NewGuid();
        var area = MakeArea(token: validToken);
        _mockService.Setup(s => s.ReorderDiningAreasAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<ReorderDiningAreasCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiningAreaDto> { area });

        var r5 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: new[] { new ReorderItemApiRequest(area.Id, validToken) }),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<IReadOnlyList<DiningAreaDto>>>(r5);

        // 6. Exception branch -> 409
        _mockService.Setup(s => s.ReorderDiningAreasAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<ReorderDiningAreasCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Conflict during reorder"));

        var r6 = await RestaurantConfigEndpoints.ReorderDiningAreasHandler(
            _branchId, new ReorderDiningAreasApiRequest(Items: new[] { new ReorderItemApiRequest(area.Id, validToken) }),
            _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(r6).StatusCode);
    }

    [Fact]
    public async Task ActivateAndDeactivateDiningAreaHandler_TokenAndException_Branches()
    {
        var areaId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // Activate: missing token -> 412
        var ctx1 = new DefaultHttpContext();
        var rActNoToken = await RestaurantConfigEndpoints.ActivateDiningAreaHandler(
            _branchId, areaId, null, _tenantContext, _mockService.Object, ctx1, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rActNoToken).StatusCode);

        // Activate: success
        var activeArea = MakeArea(id: areaId, token: token);
        _mockService.Setup(s => s.ActivateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeArea);

        var ctx2 = new DefaultHttpContext();
        var rActSuccess = await RestaurantConfigEndpoints.ActivateDiningAreaHandler(
            _branchId, areaId, new DiningAreaStateApiRequest(token), _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<DiningAreaDto>>(rActSuccess);
        Assert.Equal($"\"{token:D}\"", ctx2.Response.Headers.ETag);

        // Activate: exception -> 404
        _mockService.Setup(s => s.ActivateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Not found"));
        var rActErr = await RestaurantConfigEndpoints.ActivateDiningAreaHandler(
            _branchId, areaId, new DiningAreaStateApiRequest(token), _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rActErr).StatusCode);

        // Deactivate: missing token -> 412
        var ctx3 = new DefaultHttpContext();
        var rDeactNoToken = await RestaurantConfigEndpoints.DeactivateDiningAreaHandler(
            _branchId, areaId, null, _tenantContext, _mockService.Object, ctx3, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rDeactNoToken).StatusCode);

        // Deactivate: success
        var inactiveArea = MakeArea(id: areaId, token: token);
        _mockService.Setup(s => s.DeactivateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveArea);

        var ctx4 = new DefaultHttpContext();
        var rDeactSuccess = await RestaurantConfigEndpoints.DeactivateDiningAreaHandler(
            _branchId, areaId, new DiningAreaStateApiRequest(token), _tenantContext, _mockService.Object, ctx4, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<DiningAreaDto>>(rDeactSuccess);
        Assert.Equal($"\"{token:D}\"", ctx4.Response.Headers.ETag);

        // Deactivate: exception -> 404
        _mockService.Setup(s => s.DeactivateDiningAreaAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new DiningAreaId(areaId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Not found"));
        var rDeactErr = await RestaurantConfigEndpoints.DeactivateDiningAreaHandler(
            _branchId, areaId, new DiningAreaStateApiRequest(token), _tenantContext, _mockService.Object, ctx4, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rDeactErr).StatusCode);
    }
}
