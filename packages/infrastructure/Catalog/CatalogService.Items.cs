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
    public async Task<IReadOnlyList<MenuItemDto>> ListItemsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId? categoryId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuWithAccessCheckAsync(tenantId, branchId, menuId, actor, ct);

        var query = _dbContext.MenuItems
            .Include(i => i.Variants)
            .Where(i => i.TenantId == tenantId && i.BranchId == branchId && i.MenuId == menuId);

        if (categoryId.HasValue)
        {
            query = query.Where(i => i.CategoryId == categoryId.Value);
        }

        var items = await query
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .ToListAsync(ct);

        return items.Select(MapMenuItem).ToList();
    }

    public async Task<MenuItemDto> GetItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuWithAccessCheckAsync(tenantId, branchId, menuId, actor, ct);

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> CreateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        CreateMenuItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        EnsurePricingPermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var categoryId = new MenuCategoryId(command.CategoryId);
        var categoryExists = await _dbContext.MenuCategories
            .AnyAsync(c => c.TenantId == tenantId && c.BranchId == branchId && c.MenuId == menuId && c.Id == categoryId, ct);

        if (!categoryExists)
        {
            throw new ResourceNotFoundException($"Category '{command.CategoryId}' was not found in this menu.");
        }

        var normalizedSlug = command.Slug.Trim().ToLowerInvariant();
        var slugExists = await _dbContext.MenuItems
            .AnyAsync(i => i.TenantId == tenantId && i.MenuId == menuId && i.Slug == normalizedSlug, ct);

        if (slugExists)
        {
            throw new DuplicateSlugException($"MenuItem with slug '{normalizedSlug}' already exists for this menu.");
        }

        var basePrice = PriceAmount.FromMinorUnits(command.BasePriceMinorUnits);
        var item = MenuItem.Create(
            tenantId: tenantId,
            branchId: branchId,
            menuId: menuId,
            categoryId: categoryId,
            name: command.Name,
            slug: normalizedSlug,
            basePrice: basePrice,
            shortDescription: command.ShortDescription,
            fullDescription: command.FullDescription,
            imageUrl: command.ImageUrl,
            sortOrder: command.SortOrder);

        _dbContext.MenuItems.Add(item);
        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemCreated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = command.CategoryId,
            ItemId = item.Id.Value,
            item.Name,
            item.Slug,
            BasePriceMinorUnits = item.BasePriceMinorUnits.MinorUnits,
            item.SortOrder,
            item.IsActive
        });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateSlugException($"MenuItem with slug '{normalizedSlug}' already exists for this menu.");
        }

        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> UpdateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);

        var categoryId = new MenuCategoryId(command.CategoryId);
        if (item.CategoryId != categoryId)
        {
            var categoryExists = await _dbContext.MenuCategories
                .AnyAsync(c => c.TenantId == tenantId && c.BranchId == branchId && c.MenuId == menuId && c.Id == categoryId, ct);
            if (!categoryExists)
            {
                throw new ResourceNotFoundException($"Category '{command.CategoryId}' was not found in this menu.");
            }
        }

        if (command.BasePriceMinorUnits.HasValue)
        {
            var newPrice = PriceAmount.FromMinorUnits(command.BasePriceMinorUnits.Value);
            if (item.BasePriceMinorUnits != newPrice)
            {
                EnsurePricingPermission(actor);
                var oldPrice = item.BasePriceMinorUnits;
                item.UpdateBasePrice(newPrice);

                AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemPriceUpdated, actor, branchId, new
                {
                    MenuId = menuId.Value,
                    ItemId = item.Id.Value,
                    OldPriceMinorUnits = oldPrice.MinorUnits,
                    NewPriceMinorUnits = newPrice.MinorUnits
                });
            }
        }

        item.UpdateDetails(
            name: command.Name,
            categoryId: categoryId,
            shortDescription: command.ShortDescription,
            fullDescription: command.FullDescription,
            imageUrl: command.ImageUrl,
            sortOrder: command.SortOrder);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = categoryId.Value,
            ItemId = item.Id.Value,
            item.Name,
            item.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> UpdateItemPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsurePricingPermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        VerifyConcurrencyToken(item.ConcurrencyToken, command.ConcurrencyToken);

        var oldPrice = item.BasePriceMinorUnits;
        var newPrice = PriceAmount.FromMinorUnits(command.BasePriceMinorUnits);
        item.UpdateBasePrice(newPrice);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemPriceUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = item.Id.Value,
            OldPriceMinorUnits = oldPrice.MinorUnits,
            NewPriceMinorUnits = newPrice.MinorUnits
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> ActivateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        VerifyConcurrencyToken(item.ConcurrencyToken, concurrencyToken);

        item.Activate();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemActivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = item.Id.Value,
            item.IsActive
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<MenuItemDto> DeactivateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var item = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.BranchId == branchId &&
                i.MenuId == menuId &&
                i.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"MenuItem '{itemId.Value}' was not found.");

        VerifyConcurrencyToken(item.ConcurrencyToken, concurrencyToken);

        item.Deactivate();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemDeactivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            ItemId = item.Id.Value,
            item.IsActive
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuItem(item);
    }

    public async Task<IReadOnlyList<MenuItemDto>> ReorderItemsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        ReorderMenuItemsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var items = await _dbContext.MenuItems
            .Include(i => i.Variants)
            .Where(i => i.TenantId == tenantId && i.BranchId == branchId && i.MenuId == menuId && i.CategoryId == categoryId)
            .ToListAsync(ct);

        if (command.Items == null || command.Items.Count != items.Count)
        {
            throw new DomainException($"Reorder list must contain all {items.Count} items for this category.");
        }

        var itemMap = items.ToDictionary(i => i.Id.Value);
        var seenIds = new HashSet<Guid>();

        foreach (var item in command.Items)
        {
            if (!seenIds.Add(item.Id))
            {
                throw new DomainException($"Duplicate item ID '{item.Id}' in reorder list.");
            }

            if (!itemMap.TryGetValue(item.Id, out var existingItem))
            {
                throw new DomainException($"Item '{item.Id}' does not belong to this category.");
            }

            VerifyConcurrencyToken(existingItem.ConcurrencyToken, item.ConcurrencyToken);
        }

        foreach (var item in command.Items)
        {
            itemMap[item.Id].UpdateSortOrder(item.SortOrder);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuItemsReordered, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = categoryId.Value,
            OrderedCount = command.Items.Count
        });

        await _dbContext.SaveChangesAsync(ct);
        return items.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(MapMenuItem).ToList();
    }

    private static MenuItemDto MapMenuItem(MenuItem item) => new(
        Id: item.Id.Value,
        TenantId: item.TenantId.Value,
        BranchId: item.BranchId.Value,
        MenuId: item.MenuId.Value,
        CategoryId: item.CategoryId.Value,
        Name: item.Name,
        Slug: item.Slug,
        ShortDescription: item.ShortDescription,
        FullDescription: item.FullDescription,
        ImageUrl: item.ImageUrl,
        BasePriceMinorUnits: item.BasePriceMinorUnits.MinorUnits,
        SortOrder: item.SortOrder,
        IsActive: item.IsActive,
        CreatedAtUtc: item.CreatedAtUtc,
        UpdatedAtUtc: item.UpdatedAtUtc,
        ConcurrencyToken: item.ConcurrencyToken,
        Variants: item.Variants?.Select(MapItemVariant).ToList());
}
