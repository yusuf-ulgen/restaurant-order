using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Catalog;

public partial class CatalogService
{
    public async Task<IReadOnlyList<BranchItemAvailabilityDto>> ListBranchAvailabilitiesAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var list = await _dbContext.BranchItemAvailabilities
            .Where(a => a.TenantId == tenantId && a.BranchId == branchId)
            .OrderBy(a => a.MenuItemId)
            .ThenBy(a => a.ItemVariantId)
            .ToListAsync(ct);

        return list.Select(MapAvailability).ToList();
    }

    public async Task<BranchItemAvailabilityDto> Quick86ItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Quick86ItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureQuick86Permission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var (item, menu) = await GetItemAndMenuAsync(tenantId, branchId, menuId, itemId, ct);
        EnsureItemAllowsAvailabilityChange(item, menu);
        await EnsurePreparationStationScopeAsync(item, branchId, tenantId, actor, ct);

        var reasonCode = ParseReasonCode(command.ReasonCode);
        if (reasonCode == AvailabilityReasonCode.Restocked)
        {
            throw new DomainException("ReasonCode 'Restocked' cannot be used for Quick 86. Use the restock endpoint.");
        }

        var existing = await _dbContext.BranchItemAvailabilities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BranchId == branchId && a.MenuItemId == itemId && a.ItemVariantId == null, ct);

        BranchItemAvailability record;
        if (existing is not null)
        {
            VerifyConcurrencyToken(existing.ConcurrencyToken, command.ConcurrencyToken);
            existing.MarkUnavailable(reasonCode, command.Note, command.ExpectedAvailableAtUtc, new UserId(actor.SubjectId));
            record = existing;
        }
        else
        {
            VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);
            record = BranchItemAvailability.CreateItemUnavailable(
                tenantId,
                branchId,
                itemId,
                reasonCode,
                command.Note,
                command.ExpectedAvailableAtUtc,
                new UserId(actor.SubjectId));
            _dbContext.BranchItemAvailabilities.Add(record);
        }

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.ItemAvailabilityChanged,
            actor,
            branchId,
            new
            {
                MenuItemId = itemId.Value,
                IsAvailable = false,
                ReasonCode = reasonCode.ToString(),
                command.Note,
                command.ExpectedAvailableAtUtc
            });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent availability modification was detected.");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConcurrencyConflictException("A concurrent availability modification was detected.");
        }

        await _eventPublisher.PublishAvailabilityChangedAsync(
            new CatalogAvailabilityChangedEvent(
                TenantId: tenantId.Value,
                BranchId: branchId.Value,
                MenuItemId: itemId.Value,
                ItemVariantId: null,
                IsAvailable: false,
                ReasonCode: reasonCode.ToString(),
                Note: record.Note,
                ExpectedAvailableAtUtc: record.ExpectedAvailableAtUtc,
                OccurredAtUtc: record.ChangedAtUtc),
            ct);

        return MapAvailability(record);
    }

    public async Task<BranchItemAvailabilityDto> RestockItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        RestockItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureQuick86Permission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var (item, menu) = await GetItemAndMenuAsync(tenantId, branchId, menuId, itemId, ct);
        EnsureItemAllowsAvailabilityChange(item, menu);
        await EnsurePreparationStationScopeAsync(item, branchId, tenantId, actor, ct);

        var existing = await _dbContext.BranchItemAvailabilities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BranchId == branchId && a.MenuItemId == itemId && a.ItemVariantId == null, ct);

        BranchItemAvailability record;
        if (existing is not null)
        {
            VerifyConcurrencyToken(existing.ConcurrencyToken, command.ConcurrencyToken);
            existing.Restock(command.Note, new UserId(actor.SubjectId));
            record = existing;
        }
        else
        {
            VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);
            record = BranchItemAvailability.CreateItemUnavailable(
                tenantId,
                branchId,
                itemId,
                AvailabilityReasonCode.Manual,
                null,
                null,
                new UserId(actor.SubjectId));
            record.Restock(command.Note, new UserId(actor.SubjectId));
            _dbContext.BranchItemAvailabilities.Add(record);
        }

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.ItemRestocked,
            actor,
            branchId,
            new
            {
                MenuItemId = itemId.Value,
                IsAvailable = true,
                ReasonCode = AvailabilityReasonCode.Restocked.ToString(),
                command.Note
            });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent availability modification was detected.");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConcurrencyConflictException("A concurrent availability modification was detected.");
        }

        await _eventPublisher.PublishAvailabilityChangedAsync(
            new CatalogAvailabilityChangedEvent(
                TenantId: tenantId.Value,
                BranchId: branchId.Value,
                MenuItemId: itemId.Value,
                ItemVariantId: null,
                IsAvailable: true,
                ReasonCode: AvailabilityReasonCode.Restocked.ToString(),
                Note: record.Note,
                ExpectedAvailableAtUtc: null,
                OccurredAtUtc: record.ChangedAtUtc),
            ct);

        return MapAvailability(record);
    }

    private void EnsureQuick86Permission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.MenuInventoryQuick86))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to modify inventory availability (Quick 86).");
        }
    }

    private async Task<(MenuItem Item, Menu Menu)> GetItemAndMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        CancellationToken ct)
    {
        var item = await _dbContext.MenuItems
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.BranchId == branchId && i.MenuId == menuId && i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        return (item, menu);
    }

    private static void EnsureItemAllowsAvailabilityChange(MenuItem item, Menu menu)
    {
        if (menu.Status == MenuStatus.Archived)
        {
            throw new DomainException("Cannot change availability of an item in an archived menu.");
        }

        if (!item.IsActive)
        {
            throw new DomainException("Cannot change availability of an inactive item.");
        }
    }

    private async Task EnsurePreparationStationScopeAsync(
        MenuItem item,
        BranchId branchId,
        TenantId tenantId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        if (actor.Role == AuthRole.Kitchen)
        {
            if (!item.PreparationStationId.HasValue)
            {
                throw new InvalidAuthorizationScopeException("Kitchen staff can only modify availability of items assigned to a Kitchen preparation station.");
            }

            var station = await _dbContext.PreparationStations
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == item.PreparationStationId.Value, ct);

            if (station == null || station.StationType != PreparationStationType.Kitchen)
            {
                throw new InvalidAuthorizationScopeException("Kitchen staff can only modify availability of items assigned to a Kitchen preparation station.");
            }
        }
        else if (actor.Role == AuthRole.Bar)
        {
            if (!item.PreparationStationId.HasValue)
            {
                throw new InvalidAuthorizationScopeException("Bar staff can only modify availability of items assigned to a Bar preparation station.");
            }

            var station = await _dbContext.PreparationStations
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == item.PreparationStationId.Value, ct);

            if (station == null || station.StationType != PreparationStationType.Bar)
            {
                throw new InvalidAuthorizationScopeException("Bar staff can only modify availability of items assigned to a Bar preparation station.");
            }
        }
    }

    private static AvailabilityReasonCode ParseReasonCode(string reasonCodeStr)
    {
        if (Enum.TryParse<AvailabilityReasonCode>(reasonCodeStr, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new DomainException($"Invalid AvailabilityReasonCode '{reasonCodeStr}'.");
    }

    private static BranchItemAvailabilityDto MapAvailability(BranchItemAvailability a)
    {
        return new BranchItemAvailabilityDto(
            Id: a.Id.Value,
            TenantId: a.TenantId.Value,
            BranchId: a.BranchId.Value,
            MenuItemId: a.MenuItemId.Value,
            ItemVariantId: a.ItemVariantId?.Value,
            IsAvailable: a.IsAvailable,
            ReasonCode: a.ReasonCode.ToString(),
            Note: a.Note,
            ExpectedAvailableAtUtc: a.ExpectedAvailableAtUtc,
            ChangedByUserId: a.ChangedByUserId.Value,
            ChangedAtUtc: a.ChangedAtUtc,
            ConcurrencyToken: a.ConcurrencyToken);
    }

    private async Task<PreparationStationId?> ValidateStationAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? stationGuid,
        CancellationToken ct)
    {
        if (!stationGuid.HasValue)
        {
            return null;
        }

        var station = await _dbContext.PreparationStations
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == stationGuid.Value, ct)
            ?? throw new DomainException($"Preparation station '{stationGuid.Value}' does not belong to branch '{branchId.Value}'.");

        return station.Id;
    }
}
