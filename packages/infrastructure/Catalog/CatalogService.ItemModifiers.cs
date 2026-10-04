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
    public async Task<MenuItemDto> UpdateItemMetadataAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemMetadataCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);

        var spicyLevel = new SpicyLevel(command.SpicyLevel);
        var dietaryTags = ParseDietaryTags(command.DietaryTags);
        var allergenTags = ParseAllergenTags(command.AllergenTags);

        item.UpdateMetadata(dietaryTags, allergenTags, spicyLevel);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemMetadataUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = itemId.Value,
            SpicyLevel = spicyLevel.Value,
            DietaryTags = dietaryTags.Select(t => t.ToString()).ToList(),
            AllergenTags = allergenTags.Select(a => a.ToString()).ToList()
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> AssignModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AssignModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);

        var groupId = new ModifierGroupId(command.ModifierGroupId);
        var modifierGroup = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == groupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{command.ModifierGroupId}' was not found in branch '{branchId.Value}'.");

        item.AssignModifierGroup(modifierGroup, command.SortOrder);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemModifierGroupAssigned, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = itemId.Value,
            ModifierGroupId = groupId.Value,
            command.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> RemoveModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        VerifyConcurrencyToken(item.ConcurrencyToken, concurrencyToken);

        item.RemoveModifierGroup(modifierGroupId);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemModifierGroupRemoved, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = itemId.Value,
            ModifierGroupId = modifierGroupId.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> ReorderMenuItemModifierGroupsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ReorderMenuItemModifierGroupsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);

        var orderings = command.Items
            .Select(i => (GroupId: new ModifierGroupId(i.ModifierGroupId), SortOrder: i.SortOrder))
            .ToList();

        item.ReorderModifierGroups(orderings);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemModifierGroupsReordered, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = itemId.Value,
            Count = command.Items.Count
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }
}
