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

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class PreparationStationsEndpointsUnitTests
{
    private readonly Mock<IRestaurantConfigService> _mockService = new();
    private readonly Mock<IJwtClaimPrincipalParser> _mockParser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticatedPrincipal _actor;

    public PreparationStationsEndpointsUnitTests()
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

    private PreparationStationDto MakeStation(Guid? id = null, Guid? token = null) =>
        new(id ?? Guid.NewGuid(), _tenantId, _branchId, "GRILL", "Izgara", "Kitchen", 1, true,
            token ?? Guid.NewGuid(), DateTime.UtcNow, null);

    private StationRuntimeDto MakeRuntime(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "GRILL", "Izgara", "Kitchen", 1, true);

    [Fact]
    public async Task ListAndRuntimePreparationStationsHandler_SuccessAndException_Branches()
    {
        var station = MakeStation();
        var stations = new List<PreparationStationDto> { station };

        _mockService.Setup(s => s.ListPreparationStationsAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stations);

        var ctx = new DefaultHttpContext();
        var okResult = await RestaurantConfigEndpoints.ListPreparationStationsHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        var okObj = Assert.IsAssignableFrom<Ok<IReadOnlyList<PreparationStationDto>>>(okResult);
        Assert.Single(okObj.Value!);

        // Exception branch on list
        _mockService.Setup(s => s.ListPreparationStationsAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Failure listing stations"));
        var errResult = await RestaurantConfigEndpoints.ListPreparationStationsHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(errResult).StatusCode);

        // Runtime handler success (returns IReadOnlyList<StationRuntimeDto>)
        var runtimeStations = new List<StationRuntimeDto> { MakeRuntime() };
        _mockService.Setup(s => s.GetPreparationStationRuntimeAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runtimeStations);
        var runtimeResult = await RestaurantConfigEndpoints.GetPreparationStationRuntimeHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<IReadOnlyList<StationRuntimeDto>>>(runtimeResult);

        // Runtime handler exception
        _mockService.Setup(s => s.GetPreparationStationRuntimeAsync(
                new TenantId(_tenantId), new BranchId(_branchId), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Branch not found"));
        var runtimeErr = await RestaurantConfigEndpoints.GetPreparationStationRuntimeHandler(
            _branchId, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(runtimeErr).StatusCode);
    }

    [Fact]
    public async Task CreatePreparationStationHandler_SuccessAndException_Branches()
    {
        var token = Guid.NewGuid();
        var created = MakeStation(token: token);

        _mockService.Setup(s => s.CreatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<CreatePreparationStationCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var ctx = new DefaultHttpContext();
        var req = new CreatePreparationStationApiRequest("BAR", "Bar Station", "Bar", 2);

        var result = await RestaurantConfigEndpoints.CreatePreparationStationHandler(
            _branchId, req, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        var createdResult = Assert.IsAssignableFrom<Created<PreparationStationDto>>(result);
        Assert.Equal(created, createdResult.Value);
        Assert.Equal($"\"{token:D}\"", ctx.Response.Headers.ETag);

        // Exception branch
        _mockService.Setup(s => s.CreatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<CreatePreparationStationCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateCodeException("Station code exists"));

        var errResult = await RestaurantConfigEndpoints.CreatePreparationStationHandler(
            _branchId, req, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(errResult).StatusCode);
    }

    [Fact]
    public async Task UpdatePreparationStationHandler_TokenValidationAndExecution_Branches()
    {
        var stationId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // 1. Missing token -> 412
        var ctx1 = new DefaultHttpContext();
        var reqNoToken = new UpdatePreparationStationApiRequest("Cold Prep", "Kitchen", ConcurrencyToken: null);
        var r1 = await RestaurantConfigEndpoints.UpdatePreparationStationHandler(
            _branchId, stationId, reqNoToken, _tenantContext, _mockService.Object, ctx1, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r1).StatusCode);

        // 2. Success with token
        var updated = MakeStation(id: stationId, token: token);
        _mockService.Setup(s => s.UpdatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                It.IsAny<UpdatePreparationStationCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var ctx2 = new DefaultHttpContext();
        var reqToken = new UpdatePreparationStationApiRequest("Cold Prep", "Kitchen", ConcurrencyToken: token);
        var r2 = await RestaurantConfigEndpoints.UpdatePreparationStationHandler(
            _branchId, stationId, reqToken, _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        var okObj = Assert.IsAssignableFrom<Ok<PreparationStationDto>>(r2);
        Assert.Equal(updated, okObj.Value);
        Assert.Equal($"\"{token:D}\"", ctx2.Response.Headers.ETag);

        // 3. Exception branch -> 404
        _mockService.Setup(s => s.UpdatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                It.IsAny<UpdatePreparationStationCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Station not found"));

        var ctx3 = new DefaultHttpContext();
        var r3 = await RestaurantConfigEndpoints.UpdatePreparationStationHandler(
            _branchId, stationId, reqToken, _tenantContext, _mockService.Object, ctx3, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(r3).StatusCode);
    }

    [Fact]
    public async Task ReorderPreparationStationsHandler_AllValidationBranches()
    {
        var ctx = new DefaultHttpContext();

        // 1. Items null but OrderedStationIds present -> 412
        var req1 = new ReorderPreparationStationsApiRequest(Items: null, OrderedStationIds: new[] { Guid.NewGuid() });
        var r1 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req1, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r1).StatusCode);

        // 2. Both null -> 400
        var req2 = new ReorderPreparationStationsApiRequest(Items: null, OrderedStationIds: null);
        var r2 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req2, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ProblemHttpResult>(r2).StatusCode);

        // 3. Items contain null ConcurrencyToken -> 412
        var req3 = new ReorderPreparationStationsApiRequest(Items: new[] { new ReorderItemApiRequest(Guid.NewGuid(), null) });
        var r3 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req3, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r3).StatusCode);

        // 4. Items contain empty Guid ConcurrencyToken -> 412
        var req4 = new ReorderPreparationStationsApiRequest(Items: new[] { new ReorderItemApiRequest(Guid.NewGuid(), Guid.Empty) });
        var r4 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req4, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(r4).StatusCode);

        // 5. Success
        var validToken = Guid.NewGuid();
        var station = MakeStation(token: validToken);
        var reordered = new List<PreparationStationDto> { station };
        _mockService.Setup(s => s.ReorderPreparationStationsAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<ReorderPreparationStationsCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reordered);

        var req5 = new ReorderPreparationStationsApiRequest(Items: new[] { new ReorderItemApiRequest(station.Id, validToken) });
        var r5 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req5, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<IReadOnlyList<PreparationStationDto>>>(r5);

        // 6. Exception branch -> 409
        _mockService.Setup(s => s.ReorderPreparationStationsAsync(
                new TenantId(_tenantId), new BranchId(_branchId),
                It.IsAny<ReorderPreparationStationsCommand>(), _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Conflict during reorder"));

        var r6 = await RestaurantConfigEndpoints.ReorderPreparationStationsHandler(
            _branchId, req5, _tenantContext, _mockService.Object, ctx, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ProblemHttpResult>(r6).StatusCode);
    }

    [Fact]
    public async Task ActivateAndDeactivatePreparationStationHandler_TokenAndException_Branches()
    {
        var stationId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // Activate: missing token -> 412
        var ctx1 = new DefaultHttpContext();
        var rActNoToken = await RestaurantConfigEndpoints.ActivatePreparationStationHandler(
            _branchId, stationId, null, _tenantContext, _mockService.Object, ctx1, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rActNoToken).StatusCode);

        // Activate: success
        var activeStation = MakeStation(id: stationId, token: token);
        _mockService.Setup(s => s.ActivatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeStation);

        var ctx2 = new DefaultHttpContext();
        var rActSuccess = await RestaurantConfigEndpoints.ActivatePreparationStationHandler(
            _branchId, stationId, new PreparationStationStateApiRequest(token), _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<PreparationStationDto>>(rActSuccess);
        Assert.Equal($"\"{token:D}\"", ctx2.Response.Headers.ETag);

        // Activate: exception -> 404
        _mockService.Setup(s => s.ActivatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Not found"));
        var rActErr = await RestaurantConfigEndpoints.ActivatePreparationStationHandler(
            _branchId, stationId, new PreparationStationStateApiRequest(token), _tenantContext, _mockService.Object, ctx2, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rActErr).StatusCode);

        // Deactivate: missing token -> 412
        var ctx3 = new DefaultHttpContext();
        var rDeactNoToken = await RestaurantConfigEndpoints.DeactivatePreparationStationHandler(
            _branchId, stationId, null, _tenantContext, _mockService.Object, ctx3, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<ProblemHttpResult>(rDeactNoToken).StatusCode);

        // Deactivate: success
        var inactiveStation = MakeStation(id: stationId, token: token);
        _mockService.Setup(s => s.DeactivatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveStation);

        var ctx4 = new DefaultHttpContext();
        var rDeactSuccess = await RestaurantConfigEndpoints.DeactivatePreparationStationHandler(
            _branchId, stationId, new PreparationStationStateApiRequest(token), _tenantContext, _mockService.Object, ctx4, _mockParser.Object, CancellationToken.None);
        Assert.IsAssignableFrom<Ok<PreparationStationDto>>(rDeactSuccess);
        Assert.Equal($"\"{token:D}\"", ctx4.Response.Headers.ETag);

        // Deactivate: exception -> 404
        _mockService.Setup(s => s.DeactivatePreparationStationAsync(
                new TenantId(_tenantId), new BranchId(_branchId), new PreparationStationId(stationId),
                token, _actor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Not found"));
        var rDeactErr = await RestaurantConfigEndpoints.DeactivatePreparationStationHandler(
            _branchId, stationId, new PreparationStationStateApiRequest(token), _tenantContext, _mockService.Object, ctx4, _mockParser.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ProblemHttpResult>(rDeactErr).StatusCode);
    }
}
