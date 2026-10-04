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
    public async Task<IReadOnlyList<ItemVariantDto>> ListVariantsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuItemWithAccessCheckAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variants = await _dbContext.ItemVariants
            .Where(v => v.TenantId == tenantId && v.MenuItemId == itemId)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Name)
            .ToListAsync(ct);

        return variants.Select(MapItemVariant).ToList();
    }

    public async Task<ItemVariantDto> GetVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuItemWithAccessCheckAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variant = await _dbContext.ItemVariants
            .FirstOrDefaultAsync(v =>
                v.TenantId == tenantId &&
                v.MenuItemId == itemId &&
                v.Id == variantId, ct)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        return MapItemVariant(variant);
    }

    public async Task<ItemVariantDto> CreateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        CreateItemVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        EnsurePricingPermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        if (item.Variants.Any(v => v.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Variant with code '{normalizedCode}' already exists for this item.");
        }

        var price = PriceAmount.FromMinorUnits(command.AbsolutePriceMinorUnits);

        if (command.IsDefault)
        {
            foreach (var other in item.Variants.Where(v => v.IsDefault))
            {
                other.SetDefault(false);
            }
        }

        var variant = item.AddVariant(
            command.Name,
            normalizedCode,
            price,
            command.SortOrder,
            command.IsDefault);

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantCreated, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            VariantId = variant.Id.Value,
            variant.Name,
            variant.Code,
            AbsolutePriceMinorUnits = variant.AbsolutePriceMinorUnits.MinorUnits,
            variant.IsDefault,
            variant.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapItemVariant(variant);
    }

    public async Task<ItemVariantDto> UpdateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        UpdateItemVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variant = item.Variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        VerifyConcurrencyToken(variant.ConcurrencyToken, command.ConcurrencyToken);

        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        if (item.Variants.Any(v => v.Id != variantId && v.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Variant with code '{normalizedCode}' already exists for this item.");
        }

        if (command.AbsolutePriceMinorUnits.HasValue)
        {
            var newPrice = PriceAmount.FromMinorUnits(command.AbsolutePriceMinorUnits.Value);
            if (variant.AbsolutePriceMinorUnits != newPrice)
            {
                EnsurePricingPermission(actor);
                var oldPrice = variant.AbsolutePriceMinorUnits;
                variant.UpdatePrice(newPrice);

                AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantPriceUpdated, actor, branchId, new
                {
                    MenuId = menuId.Value,
                    MenuItemId = itemId.Value,
                    VariantId = variant.Id.Value,
                    OldPriceMinorUnits = oldPrice.MinorUnits,
                    NewPriceMinorUnits = newPrice.MinorUnits
                });
            }
        }

        if (command.IsDefault && !variant.IsDefault)
        {
            foreach (var other in item.Variants.Where(v => v.Id != variantId && v.IsDefault))
            {
                other.SetDefault(false);
            }
            variant.SetDefault(true);
        }
        else if (!command.IsDefault && variant.IsDefault)
        {
            variant.SetDefault(false);
        }

        variant.UpdateDetails(command.Name, normalizedCode, command.SortOrder, variant.IsDefault);
        item.EnsureSingleActiveDefaultVariant();

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            VariantId = variant.Id.Value,
            variant.Name,
            variant.Code,
            variant.IsDefault,
            variant.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapItemVariant(variant);
    }

    public async Task<ItemVariantDto> UpdateVariantPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        UpdateItemVariantPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsurePricingPermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variant = item.Variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        VerifyConcurrencyToken(variant.ConcurrencyToken, command.ConcurrencyToken);

        var oldPrice = variant.AbsolutePriceMinorUnits;
        var newPrice = PriceAmount.FromMinorUnits(command.AbsolutePriceMinorUnits);
        variant.UpdatePrice(newPrice);

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantPriceUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            VariantId = variant.Id.Value,
            OldPriceMinorUnits = oldPrice.MinorUnits,
            NewPriceMinorUnits = newPrice.MinorUnits
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapItemVariant(variant);
    }

    public async Task<ItemVariantDto> ActivateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variant = item.Variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        VerifyConcurrencyToken(variant.ConcurrencyToken, concurrencyToken);

        if (variant.IsDefault)
        {
            foreach (var other in item.Variants.Where(v => v.Id != variantId && v.IsDefault && v.IsActive))
            {
                other.SetDefault(false);
            }
        }

        variant.Activate();
        item.EnsureSingleActiveDefaultVariant();

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantActivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            VariantId = variant.Id.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapItemVariant(variant);
    }

    public async Task<ItemVariantDto> DeactivateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variant = item.Variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        VerifyConcurrencyToken(variant.ConcurrencyToken, concurrencyToken);

        variant.Deactivate();

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantDeactivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            VariantId = variant.Id.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapItemVariant(variant);
    }

    public async Task<IReadOnlyList<ItemVariantDto>> ReorderVariantsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ReorderItemVariantsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        var (branch, menu, item) = await GetMenuItemAndMenuForMutationAsync(tenantId, branchId, menuId, itemId, actor, ct);

        var variants = item.Variants.ToList();

        if (command.Items == null || command.Items.Count != variants.Count)
        {
            throw new DomainException($"Reorder list must contain all {variants.Count} variants for this item.");
        }

        var variantMap = variants.ToDictionary(v => v.Id.Value);
        var seenIds = new HashSet<Guid>();

        foreach (var vItem in command.Items)
        {
            if (!seenIds.Add(vItem.Id))
            {
                throw new DomainException($"Duplicate variant ID '{vItem.Id}' in reorder list.");
            }

            if (!variantMap.TryGetValue(vItem.Id, out var existingVariant))
            {
                throw new DomainException($"Variant '{vItem.Id}' does not belong to this item.");
            }

            VerifyConcurrencyToken(existingVariant.ConcurrencyToken, vItem.ConcurrencyToken);
        }

        foreach (var vItem in command.Items)
        {
            variantMap[vItem.Id].UpdateSortOrder(vItem.SortOrder);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.ItemVariantsReordered, actor, branchId, new
        {
            MenuId = menuId.Value,
            MenuItemId = itemId.Value,
            OrderedCount = command.Items.Count
        });

        await _dbContext.SaveChangesAsync(ct);
        return variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Name).Select(MapItemVariant).ToList();
    }

    private async Task<(Branch Branch, Menu Menu, MenuItem Item)> GetMenuItemAndMenuForMutationAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();
        return (branch, menu, item);
    }

    private async Task<MenuItem> GetMenuItemWithAccessCheckAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuWithAccessCheckAsync(tenantId, branchId, menuId, actor, ct);

        return await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");
    }
}
