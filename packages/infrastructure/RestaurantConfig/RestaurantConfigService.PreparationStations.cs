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
    // Preparation Stations Operations
    // ==========================================

    public async Task<IReadOnlyList<PreparationStationDto>> ListPreparationStationsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

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

        if (command.Items == null || command.Items.Count != stations.Count)
        {
            throw new DomainException($"Reorder list must contain all {stations.Count} preparation stations for this branch.");
        }

        var stationMap = stations.ToDictionary(s => s.Id.Value);
        var seenIds = new HashSet<Guid>();
        foreach (var item in command.Items)
        {
            if (!seenIds.Add(item.Id))
            {
                throw new DomainException($"Duplicate preparation station ID '{item.Id}' in reorder list.");
            }

            if (!stationMap.TryGetValue(item.Id, out var station))
            {
                throw new DomainException($"Preparation station '{item.Id}' does not belong to this branch.");
            }

            VerifyConcurrencyToken(station.ConcurrencyToken, item.ConcurrencyToken);
        }

        for (var i = 0; i < command.Items.Count; i++)
        {
            var station = stationMap[command.Items[i].Id];
            station.SetSortOrder(i);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationsReordered, actor, branchId,
            new { OrderedCount = command.Items.Count });

        await _dbContext.SaveChangesAsync(ct);
        return stations.OrderBy(s => s.SortOrder).Select(MapPreparationStation).ToList();
    }

    public async Task<PreparationStationDto> ActivatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        PreparationStationId stationId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Id == stationId, ct)
            ?? throw new ResourceNotFoundException($"Preparation station '{stationId.Value}' was not found.");

        VerifyConcurrencyToken(station.ConcurrencyToken, concurrencyToken);

        station.Activate();
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationActivated, actor, branchId, new { StationId = station.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
    }

    public async Task<PreparationStationDto> DeactivatePreparationStationAsync(
        TenantId tenantId,
        BranchId branchId,
        PreparationStationId stationId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(ps => ps.TenantId == tenantId && ps.BranchId == branchId && ps.Id == stationId, ct)
            ?? throw new ResourceNotFoundException($"Preparation station '{stationId.Value}' was not found.");

        VerifyConcurrencyToken(station.ConcurrencyToken, concurrencyToken);

        station.Deactivate();
        AddAuditEvent(tenantId, SecurityAuditEventType.PreparationStationDeactivated, actor, branchId, new { StationId = station.Id.Value });

        await _dbContext.SaveChangesAsync(ct);
        return MapPreparationStation(station);
    }

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
