using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    public async Task<IReadOnlyList<BranchDto>> ListBranchesAsync(
        TenantId tenantId,
        AuthenticatedPrincipal actor,
        BrandId? filterBrandId = null,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var query = _dbContext.Branches.Where(br => br.TenantId == tenantId);

        if (actor.Role == AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue)
            {
                return Array.Empty<BranchDto>();
            }

            var assignedBranchId = new BranchId(actor.Scope.BranchId.Value);
            query = query.Where(br => br.Id == assignedBranchId);
        }

        if (filterBrandId != null)
        {
            query = query.Where(br => br.BrandId == filterBrandId);
        }

        var branches = await query.OrderBy(br => br.Name).ToListAsync(ct);
        return branches.Select(MapBranch).ToList();
    }

    public async Task<BranchDto?> GetBranchByIdAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        if (actor.Role == AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId.Value)
            {
                throw new InvalidAuthorizationScopeException("BranchManager is only authorized to access their assigned branch.");
            }
        }

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        return branch != null ? MapBranch(branch) : null;
    }

    public async Task<BranchDto> CreateBranchAsync(
        TenantId tenantId,
        CreateBranchCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var brandId = new BrandId(command.BrandId);
        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);

        if (brand == null)
        {
            throw new ResourceNotFoundException($"Brand '{command.BrandId}' does not exist in this tenant.");
        }

        var slugVo = new Slug(command.Slug);
        var slugExists = await _dbContext.Branches
            .AnyAsync(br => br.TenantId == tenantId && br.Slug == slugVo, ct);

        if (slugExists)
        {
            throw new DuplicateSlugException($"A branch with slug '{command.Slug}' already exists in this tenant organization.");
        }

        var branch = Branch.Create(
            tenantId,
            brand,
            command.Name,
            command.Slug,
            command.Timezone ?? Timezone.DefaultId,
            command.Currency ?? Currency.DefaultCode);

        _dbContext.Branches.Add(branch);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BranchCreated,
            actor,
            branch.Id,
            details: new { BranchId = branch.Id.Value, command.Name, Slug = branch.Slug.Value, command.BrandId });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateSlugException($"A branch with slug '{command.Slug}' already exists in this tenant organization.");
        }

        return MapBranch(branch);
    }

    public async Task<BranchDto> UpdateBranchAsync(
        TenantId tenantId,
        BranchId branchId,
        UpdateBranchCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        if (branch == null)
        {
            throw new ResourceNotFoundException($"Branch '{branchId.Value}' not found.");
        }

        VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);

        branch.UpdateDetails(command.Name, command.Timezone, command.Currency);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BranchUpdated,
            actor,
            branch.Id,
            details: new { BranchId = branch.Id.Value, command.Name, command.Timezone, command.Currency });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this branch.");
        }

        return MapBranch(branch);
    }

    public async Task<BranchDto> ActivateBranchAsync(
        TenantId tenantId,
        BranchId branchId,
        BranchStateChangeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        if (branch == null)
        {
            throw new ResourceNotFoundException($"Branch '{branchId.Value}' not found.");
        }

        VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);

        branch.Activate();

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BranchActivated,
            actor,
            branch.Id,
            details: new { BranchId = branch.Id.Value, command.Reason });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this branch.");
        }

        return MapBranch(branch);
    }

    public async Task<BranchDto> SuspendBranchAsync(
        TenantId tenantId,
        BranchId branchId,
        BranchStateChangeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        if (branch == null)
        {
            throw new ResourceNotFoundException($"Branch '{branchId.Value}' not found.");
        }

        VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);

        branch.Suspend(command.Reason);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BranchSuspended,
            actor,
            branch.Id,
            details: new { BranchId = branch.Id.Value, command.Reason });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this branch.");
        }

        return MapBranch(branch);
    }

    public async Task<BranchDto> CloseBranchAsync(
        TenantId tenantId,
        BranchId branchId,
        BranchStateChangeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        if (branch == null)
        {
            throw new ResourceNotFoundException($"Branch '{branchId.Value}' not found.");
        }

        VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);

        branch.Close(command.Reason);

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.BranchClosed,
            actor,
            branch.Id,
            details: new { BranchId = branch.Id.Value, command.Reason });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("A concurrent update was detected for this branch.");
        }

        return MapBranch(branch);
    }
}
