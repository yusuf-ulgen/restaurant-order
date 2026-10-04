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

        group.UpdateDetails(command.Name, command.MinSelections, command.MaxSelections, command.SortOrder);

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
            .Include(mg => mg.Options)
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
            .Include(mg => mg.Options)
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

    public async Task<IReadOnlyList<ModifierOptionDto>> ListModifierOptionsAsync(
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

        return group.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Name).Select(MapModifierOption).ToList();
    }

    public async Task<ModifierOptionDto> GetModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        var group = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        var option = group.Options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new ResourceNotFoundException($"ModifierOption '{optionId.Value}' was not found.");

        return MapModifierOption(option);
    }

    public async Task<ModifierOptionDto> CreateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        CreateModifierOptionCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureCatalogManagePermission(actor);
        if (command.PriceDeltaMinorUnits != 0)
        {
            EnsurePricingPermission(actor);
        }

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        var priceDelta = PriceAmount.FromMinorUnits(command.PriceDeltaMinorUnits);
        var option = group.AddOption(command.Name, priceDelta, command.SortOrder, command.IsDefault);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionCreated, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            OptionId = option.Id.Value,
            option.Name,
            PriceDeltaMinorUnits = priceDelta.MinorUnits,
            option.SortOrder,
            option.IsDefault
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierOption(option);
    }

    public async Task<ModifierOptionDto> UpdateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        UpdateModifierOptionCommand command,
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

        var option = group.Options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new ResourceNotFoundException($"ModifierOption '{optionId.Value}' was not found.");

        VerifyConcurrencyToken(option.ConcurrencyToken, command.ConcurrencyToken);

        group.UpdateOptionDetails(optionId, command.Name, command.SortOrder, command.IsDefault);

        if (command.PriceDeltaMinorUnits.HasValue)
        {
            EnsurePricingPermission(actor);
            var newPrice = PriceAmount.FromMinorUnits(command.PriceDeltaMinorUnits.Value);
            group.UpdateOptionPrice(optionId, newPrice);
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionUpdated, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            OptionId = optionId.Value,
            option.Name,
            PriceDeltaMinorUnits = option.PriceDeltaMinorUnits.MinorUnits,
            option.SortOrder,
            option.IsDefault
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierOption(option);
    }

    public async Task<ModifierOptionDto> UpdateModifierOptionPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        UpdateModifierOptionPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsurePricingPermission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var group = await _dbContext.ModifierGroups
            .Include(mg => mg.Options)
            .FirstOrDefaultAsync(mg => mg.TenantId == tenantId && mg.BranchId == branchId && mg.Id == modifierGroupId, ct)
            ?? throw new ResourceNotFoundException($"ModifierGroup '{modifierGroupId.Value}' was not found.");

        var option = group.Options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new ResourceNotFoundException($"ModifierOption '{optionId.Value}' was not found.");

        VerifyConcurrencyToken(option.ConcurrencyToken, command.ConcurrencyToken);

        var newPrice = PriceAmount.FromMinorUnits(command.PriceDeltaMinorUnits);
        group.UpdateOptionPrice(optionId, newPrice);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionPriceUpdated, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            OptionId = optionId.Value,
            PriceDeltaMinorUnits = newPrice.MinorUnits
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierOption(option);
    }

    public async Task<ModifierOptionDto> ActivateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        Guid? concurrencyToken,
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

        var option = group.Options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new ResourceNotFoundException($"ModifierOption '{optionId.Value}' was not found.");

        VerifyConcurrencyToken(option.ConcurrencyToken, concurrencyToken);

        group.ActivateOption(optionId);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionActivated, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            OptionId = optionId.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierOption(option);
    }

    public async Task<ModifierOptionDto> DeactivateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        Guid? concurrencyToken,
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

        var option = group.Options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new ResourceNotFoundException($"ModifierOption '{optionId.Value}' was not found.");

        VerifyConcurrencyToken(option.ConcurrencyToken, concurrencyToken);

        group.DeactivateOption(optionId);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionDeactivated, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            OptionId = optionId.Value
        });

        await _dbContext.SaveChangesAsync(ct);
        return MapModifierOption(option);
    }

    public async Task<IReadOnlyList<ModifierOptionDto>> ReorderModifierOptionsAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ReorderModifierOptionsCommand command,
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

        var orderings = command.Items
            .Select(i => (OptionId: new ModifierOptionId(i.Id), SortOrder: i.SortOrder))
            .ToList();

        group.ReorderOptions(orderings);

        AddAuditEvent(tenantId, SecurityAuditEventType.ModifierOptionsReordered, actor, branchId, new
        {
            ModifierGroupId = modifierGroupId.Value,
            Count = command.Items.Count
        });

        await _dbContext.SaveChangesAsync(ct);
        return group.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Name).Select(MapModifierOption).ToList();
    }
}
