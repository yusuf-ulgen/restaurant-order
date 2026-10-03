using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    // ==========================================
    // Dining Areas Operations
    // ==========================================

    public async Task<IReadOnlyList<DiningAreaDto>> ListDiningAreasAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var areas = await _dbContext.DiningAreas
            .Where(da => da.TenantId == tenantId && da.BranchId == branchId)
            .OrderBy(da => da.SortOrder)
            .ThenBy(da => da.Name)
            .ToListAsync(ct);

        return areas.Select(MapDiningArea).ToList();
    }

    public async Task<DiningAreaDto> CreateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateDiningAreaCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var normalizedCode = command.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        var codeExists = await _dbContext.DiningAreas
            .AnyAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Code == normalizedCode, ct);

        if (codeExists)
        {
            throw new DuplicateCodeException($"A dining area with code '{normalizedCode}' already exists in this branch.");
        }

        if (!Enum.TryParse<DiningAreaType>(command.AreaType, true, out var areaType))
        {
            throw new DomainException($"Invalid dining area type '{command.AreaType}'.");
        }

        var area = DiningArea.Create(
            tenantId,
            branchId,
            command.Name,
            command.Code ?? string.Empty,
            areaType,
            command.SortOrder);

        _dbContext.DiningAreas.Add(area);
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaCreated, actor, branchId,
            new { AreaId = area.Id.Value, area.Name, area.Code, AreaType = areaType.ToString() });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    public async Task<DiningAreaDto> UpdateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId areaId,
        UpdateDiningAreaCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var area = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == areaId, ct)
            ?? throw new ResourceNotFoundException($"Dining area '{areaId.Value}' was not found.");

        VerifyConcurrencyToken(area.ConcurrencyToken, command.ConcurrencyToken);

        if (!Enum.TryParse<DiningAreaType>(command.AreaType, true, out var areaType))
        {
            throw new DomainException($"Invalid dining area type '{command.AreaType}'.");
        }

        area.Update(command.Name, areaType);
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaUpdated, actor, branchId,
            new { AreaId = area.Id.Value, area.Name, AreaType = areaType.ToString() });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    public async Task<IReadOnlyList<DiningAreaDto>> ReorderDiningAreasAsync(
        TenantId tenantId,
        BranchId branchId,
        ReorderDiningAreasCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var areas = await _dbContext.DiningAreas
            .Where(da => da.TenantId == tenantId && da.BranchId == branchId)
            .ToListAsync(ct);

        if (command.Items == null || command.Items.Count != areas.Count)
        {
            throw new DomainException($"Reorder list must contain all {areas.Count} dining areas for this branch.");
        }

        var areaMap = areas.ToDictionary(a => a.Id.Value);
        var seenIds = new HashSet<Guid>();
        foreach (var item in command.Items)
        {
            if (!seenIds.Add(item.Id))
            {
                throw new DomainException($"Duplicate dining area ID '{item.Id}' in reorder list.");
            }

            if (!areaMap.TryGetValue(item.Id, out var area))
            {
                throw new DomainException($"Dining area '{item.Id}' does not belong to this branch.");
            }

            VerifyConcurrencyToken(area.ConcurrencyToken, item.ConcurrencyToken);
        }

        for (var i = 0; i < command.Items.Count; i++)
        {
            var area = areaMap[command.Items[i].Id];
            area.SetSortOrder(i);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreasReordered, actor, branchId,
            new { OrderedCount = command.Items.Count });

        await _dbContext.SaveChangesAsync(ct);
        return areas.OrderBy(a => a.SortOrder).Select(MapDiningArea).ToList();
    }

    public async Task<DiningAreaDto> ActivateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId areaId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var area = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == areaId, ct)
            ?? throw new ResourceNotFoundException($"Dining area '{areaId.Value}' was not found.");

        VerifyConcurrencyToken(area.ConcurrencyToken, concurrencyToken);

        area.Activate();
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaActivated, actor, branchId, new { AreaId = area.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    public async Task<DiningAreaDto> DeactivateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId areaId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var area = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == areaId, ct)
            ?? throw new ResourceNotFoundException($"Dining area '{areaId.Value}' was not found.");

        VerifyConcurrencyToken(area.ConcurrencyToken, concurrencyToken);

        area.Deactivate();
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaDeactivated, actor, branchId, new { AreaId = area.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    private static DiningAreaDto MapDiningArea(DiningArea da) => new(
        da.Id.Value,
        da.TenantId.Value,
        da.BranchId.Value,
        da.Name,
        da.Code,
        da.AreaType.ToString(),
        da.SortOrder,
        da.IsActive,
        da.ConcurrencyToken,
        da.CreatedAtUtc,
        da.UpdatedAtUtc);
}
