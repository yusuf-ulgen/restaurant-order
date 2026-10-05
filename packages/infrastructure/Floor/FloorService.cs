using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Floor;

public partial class FloorService : IFloorService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly ILogger<FloorService> _logger;
    private readonly IPermissionRegistry _permissionRegistry;

    public FloorService(
        RestaurantOrderDbContext dbContext,
        ILogger<FloorService> logger,
        IPermissionRegistry? permissionRegistry = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _permissionRegistry = permissionRegistry ?? new PermissionRegistry();
    }

    public async Task<IReadOnlyList<RestaurantTableDto>> ListTablesAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? diningAreaId,
        bool? isActive,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureFloorStatusViewPermission(actor);

        var query = _dbContext.RestaurantTables
            .Where(t => t.TenantId == tenantId && t.BranchId == branchId);

        if (diningAreaId.HasValue && diningAreaId.Value != Guid.Empty)
        {
            var areaId = new DiningAreaId(diningAreaId.Value);
            query = query.Where(t => t.DiningAreaId == areaId);
        }

        if (isActive.HasValue)
        {
            query = query.Where(t => t.IsActive == isActive.Value);
        }

        var tables = await query
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        return tables.Select(MapTable).ToList();
    }

    public async Task<RestaurantTableDto> GetTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureFloorStatusViewPermission(actor);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        return MapTable(table);
    }

    public async Task<RestaurantTableDto> CreateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateTableRequest request,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureTablesManagePermission(actor);
        EnsureBranchAllowsFloorMutation(branch);

        if (request.BranchId.HasValue && request.BranchId.Value != branchId.Value)
        {
            throw new DomainException("Payload BranchId does not match route BranchId.");
        }

        var diningAreaId = new DiningAreaId(request.DiningAreaId);
        var diningArea = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == diningAreaId, ct)
            ?? throw new ResourceNotFoundException($"DiningArea '{request.DiningAreaId}' was not found in branch '{branchId.Value}'.");

        if (!diningArea.IsActive)
        {
            throw new DomainException("Cannot add a table to an inactive DiningArea.");
        }

        var exists = await _dbContext.RestaurantTables
            .AnyAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.TableNumber == request.TableNumber.Trim(), ct);

        if (exists)
        {
            throw new DuplicateCodeException($"A table with number '{request.TableNumber}' already exists in this branch.");
        }

        var shape = Enum.TryParse<TableShape>(request.Shape, true, out var parsedShape) ? parsedShape : TableShape.Square;

        var table = RestaurantTable.Create(
            tenantId,
            branchId,
            diningAreaId,
            request.TableNumber,
            request.Name,
            request.Capacity,
            request.PositionX,
            request.PositionY,
            request.Width,
            request.Height,
            request.RotationDegrees,
            shape);

        try
        {
            await ExecuteInTenantTransactionAsync(tenantId, async () =>
            {
                _dbContext.RestaurantTables.Add(table);
                AddAuditEvent(tenantId, SecurityAuditEventType.RestaurantTableCreated, actor, branchId, new
                {
                    tableId = table.Id.Value,
                    tableNumber = table.TableNumber,
                    diningAreaId = table.DiningAreaId.Value,
                    capacity = table.Capacity
                });

                await _dbContext.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateCodeException($"A table with number '{request.TableNumber}' already exists in this branch.");
        }

        return MapTable(table);
    }

    public async Task<RestaurantTableDto> UpdateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        UpdateTableRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureTablesManagePermission(actor);
        EnsureBranchAllowsFloorMutation(branch);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(table.ConcurrencyToken, concurrencyToken ?? request.ConcurrencyToken);

        var diningAreaId = new DiningAreaId(request.DiningAreaId);
        var diningArea = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == diningAreaId, ct)
            ?? throw new ResourceNotFoundException($"DiningArea '{request.DiningAreaId}' was not found in branch '{branchId.Value}'.");

        if (!diningArea.IsActive)
        {
            throw new DomainException("Cannot assign a table to an inactive DiningArea.");
        }

        var trimmedNumber = request.TableNumber.Trim();
        var duplicateExists = await _dbContext.RestaurantTables
            .AnyAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.TableNumber == trimmedNumber && t.Id != tableId, ct);

        if (duplicateExists)
        {
            throw new DuplicateCodeException($"A table with number '{request.TableNumber}' already exists in this branch.");
        }

        table.Update(request.TableNumber, request.Name, request.Capacity, diningAreaId);

        try
        {
            await ExecuteInTenantTransactionAsync(tenantId, async () =>
            {
                AddAuditEvent(tenantId, SecurityAuditEventType.RestaurantTableUpdated, actor, branchId, new
                {
                    tableId = table.Id.Value,
                    tableNumber = table.TableNumber,
                    diningAreaId = table.DiningAreaId.Value,
                    capacity = table.Capacity
                });

                await _dbContext.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateCodeException($"A table with number '{request.TableNumber}' already exists in this branch.");
        }

        return MapTable(table);
    }

    public async Task<RestaurantTableDto> UpdateTableLayoutAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        UpdateTableLayoutRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureTablesManagePermission(actor);
        EnsureBranchAllowsFloorMutation(branch);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(table.ConcurrencyToken, concurrencyToken ?? request.ConcurrencyToken);

        if (!Enum.TryParse<TableShape>(request.Shape, true, out var shape))
        {
            throw new DomainException($"Invalid TableShape value: '{request.Shape}'.");
        }

        table.UpdateLayout(request.PositionX, request.PositionY, request.Width, request.Height, request.RotationDegrees, shape);

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.RestaurantTableLayoutUpdated, actor, branchId, new
            {
                tableId = table.Id.Value,
                positionX = table.PositionX,
                positionY = table.PositionY,
                width = table.Width,
                height = table.Height,
                rotation = table.RotationDegrees,
                shape = table.Shape.ToString()
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapTable(table);
    }

    public async Task<RestaurantTableDto> ActivateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureTablesManagePermission(actor);
        EnsureBranchAllowsFloorMutation(branch);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(table.ConcurrencyToken, concurrencyToken);

        table.Activate();

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.RestaurantTableActivated, actor, branchId, new
            {
                tableId = table.Id.Value,
                tableNumber = table.TableNumber
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapTable(table);
    }

    public async Task<RestaurantTableDto> DeactivateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureTablesManagePermission(actor);
        EnsureBranchAllowsFloorMutation(branch);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(table.ConcurrencyToken, concurrencyToken);

        var hasActiveSession = await _dbContext.DiningSessions
            .AnyAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.TableId == tableId && s.Status != DiningSessionStatus.Closed, ct);

        if (hasActiveSession)
        {
            throw new DomainException("Cannot deactivate table while an active or open dining session is in progress.");
        }

        table.Deactivate();

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.RestaurantTableDeactivated, actor, branchId, new
            {
                tableId = table.Id.Value,
                tableNumber = table.TableNumber
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapTable(table);
    }

    private static RestaurantTableDto MapTable(RestaurantTable t) =>
        new(
            t.Id.Value,
            t.TenantId.Value,
            t.BranchId.Value,
            t.DiningAreaId.Value,
            t.TableNumber,
            t.Name,
            t.Capacity,
            t.PositionX,
            t.PositionY,
            t.Width,
            t.Height,
            t.RotationDegrees,
            t.Shape.ToString(),
            t.IsActive,
            t.QrVersion,
            t.ConcurrencyToken,
            t.CreatedAtUtc,
            t.UpdatedAtUtc);
}
