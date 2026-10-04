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
    public async Task<IReadOnlyList<MenuCategoryDto>> ListCategoriesAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuWithAccessCheckAsync(tenantId, branchId, menuId, actor, ct);

        var categories = await _dbContext.MenuCategories
            .Where(c => c.TenantId == tenantId && c.BranchId == branchId && c.MenuId == menuId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return categories.Select(MapMenuCategory).ToList();
    }

    public async Task<MenuCategoryDto> GetCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetMenuWithAccessCheckAsync(tenantId, branchId, menuId, actor, ct);

        var category = await _dbContext.MenuCategories
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.BranchId == branchId &&
                c.MenuId == menuId &&
                c.Id == categoryId, ct)
            ?? throw new ResourceNotFoundException($"Category '{categoryId.Value}' was not found.");

        return MapMenuCategory(category);
    }

    public async Task<MenuCategoryDto> CreateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        CreateMenuCategoryCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var normalizedSlug = command.Slug.Trim().ToLowerInvariant();
        var slugExists = await _dbContext.MenuCategories
            .AnyAsync(c => c.TenantId == tenantId && c.MenuId == menuId && c.Slug == normalizedSlug, ct);

        if (slugExists)
        {
            throw new DuplicateSlugException($"Category with slug '{normalizedSlug}' already exists for this menu.");
        }

        var category = MenuCategory.Create(
            tenantId: tenantId,
            branchId: branchId,
            menuId: menuId,
            name: command.Name,
            slug: normalizedSlug,
            description: command.Description,
            sortOrder: command.SortOrder);

        _dbContext.MenuCategories.Add(category);
        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCategoryCreated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = category.Id.Value,
            category.Name,
            category.Slug,
            category.SortOrder,
            category.IsActive
        });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateSlugException($"Category with slug '{normalizedSlug}' already exists for this menu.");
        }

        return MapMenuCategory(category);
    }

    public async Task<MenuCategoryDto> UpdateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        UpdateMenuCategoryCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var category = await _dbContext.MenuCategories
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.BranchId == branchId &&
                c.MenuId == menuId &&
                c.Id == categoryId, ct)
            ?? throw new ResourceNotFoundException($"Category '{categoryId.Value}' was not found.");

        VerifyConcurrencyToken(category.ConcurrencyToken, command.ConcurrencyToken);

        category.UpdateDetails(command.Name, command.Description, command.SortOrder);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCategoryUpdated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = category.Id.Value,
            category.Name,
            category.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuCategory(category);
    }

    public async Task<MenuCategoryDto> ActivateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var category = await _dbContext.MenuCategories
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.BranchId == branchId &&
                c.MenuId == menuId &&
                c.Id == categoryId, ct)
            ?? throw new ResourceNotFoundException($"Category '{categoryId.Value}' was not found.");

        VerifyConcurrencyToken(category.ConcurrencyToken, concurrencyToken);

        category.Activate();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCategoryActivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = category.Id.Value,
            category.IsActive
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuCategory(category);
    }

    public async Task<MenuCategoryDto> DeactivateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var category = await _dbContext.MenuCategories
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.BranchId == branchId &&
                c.MenuId == menuId &&
                c.Id == categoryId, ct)
            ?? throw new ResourceNotFoundException($"Category '{categoryId.Value}' was not found.");

        VerifyConcurrencyToken(category.ConcurrencyToken, concurrencyToken);

        category.Deactivate();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCategoryDeactivated, actor, branchId, new
        {
            MenuId = menuId.Value,
            CategoryId = category.Id.Value,
            category.IsActive
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenuCategory(category);
    }

    public async Task<IReadOnlyList<MenuCategoryDto>> ReorderCategoriesAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        ReorderCategoriesCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();

        var categories = await _dbContext.MenuCategories
            .Where(c => c.TenantId == tenantId && c.BranchId == branchId && c.MenuId == menuId)
            .ToListAsync(ct);

        if (command.Items == null || command.Items.Count != categories.Count)
        {
            throw new DomainException($"Reorder list must contain all {categories.Count} categories for this menu.");
        }

        var categoryMap = categories.ToDictionary(c => c.Id.Value);
        var seenIds = new HashSet<Guid>();

        foreach (var item in command.Items)
        {
            if (!seenIds.Add(item.Id))
            {
                throw new DomainException($"Duplicate category ID '{item.Id}' in reorder list.");
            }

            if (!categoryMap.TryGetValue(item.Id, out var category))
            {
                throw new DomainException($"Category '{item.Id}' does not belong to this menu.");
            }

            VerifyConcurrencyToken(category.ConcurrencyToken, item.ConcurrencyToken);
        }

        foreach (var item in command.Items)
        {
            var category = categoryMap[item.Id];
            category.UpdateSortOrder(item.SortOrder);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCategoriesReordered, actor, branchId, new
        {
            MenuId = menuId.Value,
            OrderedCount = command.Items.Count
        });

        await _dbContext.SaveChangesAsync(ct);
        return categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(MapMenuCategory).ToList();
    }

    private async Task<Menu> GetMenuWithAccessCheckAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        return await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");
    }

    private static MenuCategoryDto MapMenuCategory(MenuCategory category) => new(
        Id: category.Id.Value,
        TenantId: category.TenantId.Value,
        BranchId: category.BranchId.Value,
        MenuId: category.MenuId.Value,
        Name: category.Name,
        Slug: category.Slug,
        Description: category.Description,
        SortOrder: category.SortOrder,
        IsActive: category.IsActive,
        CreatedAtUtc: category.CreatedAtUtc,
        UpdatedAtUtc: category.UpdatedAtUtc,
        ConcurrencyToken: category.ConcurrencyToken);
}
