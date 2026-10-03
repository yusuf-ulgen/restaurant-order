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

        for (var i = 0; i < command.OrderedAreaIds.Count; i++)
        {
            var areaId = command.OrderedAreaIds[i];
            var area = areas.FirstOrDefault(a => a.Id.Value == areaId);
            if (area != null)
            {
                area.SetSortOrder(i);
            }
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreasReordered, actor, branchId,
            new { OrderedCount = command.OrderedAreaIds.Count });

        await _dbContext.SaveChangesAsync(ct);
        return areas.OrderBy(a => a.SortOrder).Select(MapDiningArea).ToList();
    }

    public async Task<DiningAreaDto> ActivateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId areaId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var area = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == areaId, ct)
            ?? throw new ResourceNotFoundException($"Dining area '{areaId.Value}' was not found.");

        area.Activate();
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaActivated, actor, branchId, new { AreaId = area.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    public async Task<DiningAreaDto> DeactivateDiningAreaAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId areaId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var area = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == areaId, ct)
            ?? throw new ResourceNotFoundException($"Dining area '{areaId.Value}' was not found.");

        area.Deactivate();
        AddAuditEvent(tenantId, SecurityAuditEventType.DiningAreaDeactivated, actor, branchId, new { AreaId = area.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapDiningArea(area);
    }

    // ==========================================
    // Preparation Stations Operations
    // ==========================================

    public async Task<IReadOnlyList<PreparationStationDto>> ListPreparationStationsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var stations = await _dbContext.PreparationStations
            .Where(ps => ps.TenantId == tenantId && ps.BranchId == branchId)
            .OrderBy(ps => ps.SortOrder)
            .ThenBy(ps => ps.DisplayName)
            .ToListAsync(ct);

        return stations.Select(MapPreparationStation).ToList();
    }

    public async Task<IReadOnlyList<StationRuntimeDto>> GetPreparationStationRuntimeAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureTenantAccess(tenantId, actor);

        var query = _dbContext.PreparationStations
            .Where(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.IsActive);

        if (actor.Role == AuthRole.Kitchen)
        {
            query = query.Where(ps => ps.StationType == PreparationStationType.Kitchen);
        }
        else if (actor.Role == AuthRole.Bar)
        {
            query = query.Where(ps => ps.StationType == PreparationStationType.Bar);
        }
        else if (actor.Role != AuthRole.RestaurantAdmin && actor.Role != AuthRole.BranchManager)
        {
            throw new InvalidAuthorizationScopeException("Role is not authorized to read station runtime models.");
        }

        var stations = await query
            .OrderBy(ps => ps.SortOrder)
            .Select(ps => new StationRuntimeDto(
                ps.Id.Value,
                ps.Code,
                ps.DisplayName,
                ps.StationType.ToString(),
                ps.SortOrder,
                ps.IsActive))
            .ToListAsync(ct);

        return stations;
    }

    public async Task<PreparationStationDto> CreatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        CreatePreparationStationCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var normalizedCode = command.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        var codeExists = await _dbContext.PreparationStations
            .AnyAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Code == normalizedCode, ct);

        if (codeExists)
        {
            throw new DuplicateCodeException($"A preparation station with code '{normalizedCode}' already exists in this branch.");
        }

        if (!Enum.TryParse<PreparationStationType>(command.StationType, true, out var stationType))
        {
            throw new DomainException($"Invalid station type '{command.StationType}'.");
        }

        var station = PreparationStation.Create(
            tenantId,
            branchId,
            command.Code ?? string.Empty,
            command.DisplayName,
            stationType,
            command.SortOrder);

        _dbContext.PreparationStations.Add(station);
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationCreated, actor, branchId,
            new { StationId = station.Id.Value, station.DisplayName, station.Code, StationType = stationType.ToString() });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
    }

    public async Task<PreparationStationDto> UpdatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        PreparationStationId stationId,
        UpdatePreparationStationCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Id == stationId, ct)
            ?? throw new ResourceNotFoundException($"Preparation station '{stationId.Value}' was not found.");

        VerifyConcurrencyToken(station.ConcurrencyToken, command.ConcurrencyToken);

        if (!Enum.TryParse<PreparationStationType>(command.StationType, true, out var stationType))
        {
            throw new DomainException($"Invalid station type '{command.StationType}'.");
        }

        station.Update(command.DisplayName, stationType);
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationUpdated, actor, branchId,
            new { StationId = station.Id.Value, station.DisplayName, StationType = stationType.ToString() });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
    }

    public async Task<IReadOnlyList<PreparationStationDto>> ReorderPreparationStationsAsync(
        TenantId tenantId,
        BranchId branchId,
        ReorderPreparationStationsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var stations = await _dbContext.PreparationStations
            .Where(ps => ps.TenantId == tenantId && ps.BranchId == branchId)
            .ToListAsync(ct);

        for (var i = 0; i < command.OrderedStationIds.Count; i++)
        {
            var stationId = command.OrderedStationIds[i];
            var station = stations.FirstOrDefault(s => s.Id.Value == stationId);
            if (station != null)
            {
                station.SetSortOrder(i);
            }
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationsReordered, actor, branchId,
            new { OrderedCount = command.OrderedStationIds.Count });

        await _dbContext.SaveChangesAsync(ct);
        return stations.OrderBy(s => s.SortOrder).Select(MapPreparationStation).ToList();
    }

    public async Task<PreparationStationDto> ActivatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        PreparationStationId stationId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Id == stationId, ct)
            ?? throw new ResourceNotFoundException($"Preparation station '{stationId.Value}' was not found.");

        station.Activate();
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationActivated, actor, branchId, new { StationId = station.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
    }

    public async Task<PreparationStationDto> DeactivatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        PreparationStationId stationId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Id == stationId, ct)
            ?? throw new ResourceNotFoundException($"Preparation station '{stationId.Value}' was not found.");

        station.Deactivate();
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationDeactivated, actor, branchId, new { StationId = station.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
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

    private static PreparationStationDto MapPreparationStation(PreparationStation ps) => new(
        ps.Id.Value,
        ps.TenantId.Value,
        ps.BranchId.Value,
        ps.Code,
        ps.DisplayName,
        ps.StationType.ToString(),
        ps.SortOrder,
        ps.IsActive,
        ps.ConcurrencyToken,
        ps.CreatedAtUtc,
        ps.UpdatedAtUtc);
}
