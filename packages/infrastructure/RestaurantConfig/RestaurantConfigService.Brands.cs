using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    public async Task<IReadOnlyList<BrandDto>> ListBrandsAsync(TenantId tenantId, CancellationToken ct = default)
    {
        var brands = await _dbContext.Brands
            .Where(b => b.TenantId == tenantId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        return brands.Select(MapBrand).ToList();
    }

    public async Task<BrandDto?> GetBrandByIdAsync(TenantId tenantId, BrandId brandId, CancellationToken ct = default)
    {
        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);

        return brand != null ? MapBrand(brand) : null;
    }

    public async Task<BrandDto> CreateBrandAsync(
        TenantId tenantId,
        CreateBrandCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var slugVo = new Slug(command.Slug);

        var slugExists = await _dbContext.Brands
            .AnyAsync(b => b.TenantId == tenantId && b.Slug == slugVo, ct);

        if (slugExists)
        {
            throw new DuplicateSlugException($"A brand with slug '{command.Slug}' already exists in this tenant organization.");
        }

        var brand = Brand.Create(tenantId, command.Name, command.Slug);
        _dbContext.Brands.Add(brand);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BrandCreated,
            actor,
            details: new { BrandId = brand.Id.Value, brand.Name, Slug = brand.Slug.Value });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateSlugException($"A brand with slug '{command.Slug}' already exists in this tenant organization.");
        }

        return MapBrand(brand);
    }

    public async Task<BrandDto> UpdateBrandAsync(
        TenantId tenantId,
        BrandId brandId,
        UpdateBrandCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);

        if (brand == null)
        {
            throw new ResourceNotFoundException($"Brand '{brandId.Value}' not found.");
        }

        VerifyConcurrencyToken(brand.ConcurrencyToken, command.ConcurrencyToken);

        brand.UpdateName(command.Name);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BrandUpdated,
            actor,
            details: new { BrandId = brand.Id.Value, command.Name });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this brand.");
        }

        return MapBrand(brand);
    }

    public async Task<BrandDto> ActivateBrandAsync(
        TenantId tenantId,
        BrandId brandId,
        BrandStateChangeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);

        if (brand == null)
        {
            throw new ResourceNotFoundException($"Brand '{brandId.Value}' not found.");
        }

        VerifyConcurrencyToken(brand.ConcurrencyToken, command.ConcurrencyToken);

        brand.Activate();

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BrandActivated,
            actor,
            details: new { BrandId = brand.Id.Value });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this brand.");
        }

        return MapBrand(brand);
    }

    public async Task<BrandDto> DeactivateBrandAsync(
        TenantId tenantId,
        BrandId brandId,
        BrandStateChangeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);

        if (brand == null)
        {
            throw new ResourceNotFoundException($"Brand '{brandId.Value}' not found.");
        }

        VerifyConcurrencyToken(brand.ConcurrencyToken, command.ConcurrencyToken);

        brand.Deactivate();

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BrandDeactivated,
            actor,
            details: new { BrandId = brand.Id.Value });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this brand.");
        }

        return MapBrand(brand);
    }
}
