using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Catalog;

public partial class CatalogService : ICatalogService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly ILogger<CatalogService> _logger;
    private readonly IPermissionRegistry _permissionRegistry;

    public CatalogService(
        RestaurantOrderDbContext dbContext,
        ILogger<CatalogService> logger,
        IPermissionRegistry? permissionRegistry = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _permissionRegistry = permissionRegistry ?? new PermissionRegistry();
    }

    public async Task<IReadOnlyList<MenuDto>> ListMenusAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var menus = await _dbContext.Menus
            .Where(m => m.TenantId == tenantId && m.BranchId == branchId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .ToListAsync(ct);

        return menus.Select(MapMenu).ToList();
    }

    public async Task<MenuDto> GetMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        return MapMenu(menu);
    }

    public async Task<MenuDto> CreateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateMenuCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var normalizedSlug = command.Slug.Trim().ToLowerInvariant();
        var slugExists = await _dbContext.Menus
            .AnyAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Slug == normalizedSlug, ct);

        if (slugExists)
        {
            throw new DuplicateSlugException($"Menu with slug '{normalizedSlug}' already exists for this branch.");
        }

        var menu = Menu.Create(
            tenantId: tenantId,
            branchId: branchId,
            name: command.Name,
            slug: normalizedSlug,
            description: command.Description,
            sortOrder: command.SortOrder);

        _dbContext.Menus.Add(menu);
        AddAuditEvent(tenantId, SecurityAuditEventType.MenuCreated, actor, branchId, new
        {
            MenuId = menu.Id.Value,
            menu.Name,
            menu.Slug,
            menu.SortOrder,
            Status = menu.Status.ToString()
        });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateSlugException($"Menu with slug '{normalizedSlug}' already exists for this branch.");
        }

        return MapMenu(menu);
    }

    public async Task<MenuDto> UpdateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        UpdateMenuCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        menu.EnsureNotArchived();
        VerifyConcurrencyToken(menu.ConcurrencyToken, command.ConcurrencyToken);

        menu.UpdateDetails(command.Name, command.Description, command.SortOrder);

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuUpdated, actor, branchId, new
        {
            MenuId = menu.Id.Value,
            menu.Name,
            menu.SortOrder
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenu(menu);
    }

    public async Task<MenuDto> ActivateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
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
        VerifyConcurrencyToken(menu.ConcurrencyToken, concurrencyToken);

        menu.Activate();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuActivated, actor, branchId, new
        {
            MenuId = menu.Id.Value,
            Status = menu.Status.ToString()
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenu(menu);
    }

    public async Task<MenuDto> ArchiveMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var menu = await _dbContext.Menus
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.BranchId == branchId && m.Id == menuId, ct)
            ?? throw new ResourceNotFoundException($"Menu '{menuId.Value}' was not found.");

        VerifyConcurrencyToken(menu.ConcurrencyToken, concurrencyToken);

        menu.Archive();

        AddAuditEvent(tenantId, SecurityAuditEventType.MenuArchived, actor, branchId, new
        {
            MenuId = menu.Id.Value,
            Status = menu.Status.ToString()
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapMenu(menu);
    }

    private void EnsureTenantAccess(TenantId tenantId, AuthenticatedPrincipal actor)
    {
        if (actor.Role == AuthRole.SuperAdmin)
        {
            if (!actor.Scope.TenantId.HasValue || actor.Scope.TenantId.Value != tenantId)
            {
                throw new InvalidAuthorizationScopeException("SuperAdmin is not authorized to bypass tenant scope.");
            }
        }

        if (actor.Scope.TenantId.HasValue && actor.Scope.TenantId.Value != tenantId)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }
    }

    private void EnsureBranchAccess(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor)
    {
        EnsureTenantAccess(tenantId, actor);

        if (actor.Role == AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId.Value)
            {
                throw new InvalidAuthorizationScopeException("BranchManager is only authorized to access their assigned branch.");
            }
        }
    }

    private void EnsureCatalogManagePermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.MenuCatalogManage))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to manage catalog.");
        }
    }

    private void EnsurePricingPermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.MenuPricingManage))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to manage pricing.");
        }
    }

    private async Task<Branch> GetBranchWithAccessCheckAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        return await _dbContext.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch '{branchId.Value}' was not found.");
    }

    private static void EnsureBranchAllowsCatalogMutation(Branch branch)
    {
        if (branch.Status == BranchStatus.Closed)
        {
            throw new DomainException($"Cannot modify catalog for closed branch '{branch.Id.Value}'.");
        }

        if (branch.Status == BranchStatus.Suspended)
        {
            throw new DomainException($"Cannot modify catalog for suspended branch '{branch.Id.Value}'.");
        }
    }

    private static void VerifyConcurrencyToken(Guid expectedToken, Guid? providedToken)
    {
        if (!providedToken.HasValue || providedToken.Value == Guid.Empty)
        {
            throw new ConcurrencyPreconditionException("Concurrency token is required.");
        }

        if (providedToken.Value != expectedToken)
        {
            throw new ConcurrencyConflictException(
                $"Stale concurrency token. Provided '{providedToken.Value}', but current token is '{expectedToken}'.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private void AddAuditEvent(
        TenantId tenantId,
        string eventType,
        AuthenticatedPrincipal actor,
        BranchId? branchId,
        object details)
    {
        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: eventType,
            nowUtc: DateTimeOffset.UtcNow,
            userId: actor.SubjectId != Guid.Empty ? new UserId(actor.SubjectId) : null,
            branchId: branchId,
            detailsJson: JsonSerializer.Serialize(details));

        _dbContext.SecurityAuditEvents.Add(auditEvent);
    }

    private static MenuDto MapMenu(Menu menu) => new(
        Id: menu.Id.Value,
        TenantId: menu.TenantId.Value,
        BranchId: menu.BranchId.Value,
        Name: menu.Name,
        Slug: menu.Slug,
        Description: menu.Description,
        Status: menu.Status.ToString(),
        SortOrder: menu.SortOrder,
        CreatedAtUtc: menu.CreatedAtUtc,
        UpdatedAtUtc: menu.UpdatedAtUtc,
        ConcurrencyToken: menu.ConcurrencyToken);

    private static ItemVariantDto MapItemVariant(ItemVariant variant) => new(
        Id: variant.Id.Value,
        TenantId: variant.TenantId.Value,
        BranchId: variant.BranchId.Value,
        MenuId: variant.MenuId.Value,
        MenuItemId: variant.MenuItemId.Value,
        Name: variant.Name,
        Code: variant.Code,
        AbsolutePriceMinorUnits: variant.AbsolutePriceMinorUnits.MinorUnits,
        SortOrder: variant.SortOrder,
        IsDefault: variant.IsDefault,
        IsActive: variant.IsActive,
        CreatedAtUtc: variant.CreatedAtUtc,
        UpdatedAtUtc: variant.UpdatedAtUtc,
        ConcurrencyToken: variant.ConcurrencyToken);
}
