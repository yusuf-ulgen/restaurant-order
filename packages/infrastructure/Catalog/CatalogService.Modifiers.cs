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
    public async Task<IReadOnlyList<ModifierGroupDto>> ListModifierGroupsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var groups = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .Where(mg => mg.TenantId == tenantId && mg.BranchId == branchId)
            .OrderBy(mg => mg.SortOrder)
            .ThenBy(mg => mg.Name)
            .ToListAsync(ct);

        return groups.Select(MapModifierGroup).ToList();
    }

    public async Task<ModifierGroupDto> GetModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var group = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        return MapModifierGroup(group);
    }

    public async Task<ModifierGroupDto> CreateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = ModifierGroup.Create(
            tenantId,
            branchId,
            command.Name,
            command.MinSelections,
            command.MaxSelections,
            command.SortOrder);

        _dbContext.ModifierGroups.Add(group);
        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierGroupCreated, actor, branchId, new
        {
            ModifierGroupId = group.Id.Value,
            group.Name,
            group.MinSelections,
            group.MaxSelections,
            group.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierGroup(group);
    }

    public async Task<ModifierGroupDto> UpdateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        UpdateModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        VerifyConcurrencyToken(group.ConcurrencyToken, command.ConcurrencyToken);

        var isAssigned = await _dbContext.MenuItemModifierGroupAssignments
            .AnyAsync(a => a.TenantId == tenantId && a.BranchId == branchId && a.ModifierGroupId == modifierGroupId, ct);

        group.UpdateDetails(command.Name, command.MinSelections, command.MaxSelections, command.SortOrder, isAssigned);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierGroupUpdated, actor, branchId, new
        {
            ModifierGroupId = group.Id.Value,
            group.Name,
            group.MinSelections,
            group.MaxSelections,
            group.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierGroup(group);
    }

    public async Task<ModifierGroupDto> ActivateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = await _dbContext.ModifierGroups
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        VerifyConcurrencyToken(group.ConcurrencyToken, concurrencyToken);

        group.Activate();

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierGroupActivated, actor, branchId, new
        {
            ModifierGroupId = group.Id.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierGroup(group);
    }

    public async Task<ModifierGroupDto> DeactivateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = await _dbContext.ModifierGroups
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        VerifyConcurrencyToken(group.ConcurrencyToken, concurrencyToken);

        group.Deactivate();

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierGroupDeactivated, actor, branchId, new
        {
            ModifierGroupId = group.Id.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierGroup(group);
    }
}
