using Microsoft.EntityFrameworkCore;
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
    public async Task<BranchItemAvailabilityDto> Quick86VariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Quick86VariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureQuick86Permission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var (item, menu) = await GetItemAndMenuAsync(tenantId, branchId, menuId, itemId, ct);
        EnsureItemAllowsAvailabilityChange(item, menu);
        await EnsurePreparationStationScopeAsync(item, branchId, tenantId, actor, ct);

        var variant = await _dbContext.ItemVariants
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.BranchId == branchId && v.MenuId == menuId && v.MenuItemId == itemId && v.Id == variantId, ct)
            ?? throw new ResourceNotFoundException($"Variant '{variantId.Value}' was not found.");

        if (!variant.IsActive)
        {
            throw new DomainException("Cannot change availability of an inactive variant.");
        }

        var reasonCode = ParseReasonCode(command.ReasonCode);
        if (reasonCode == AvailabilityReasonCode.Restocked)
        {
            throw new DomainException("ReasonCode 'Restocked' cannot be used for Quick 86. Use the restock endpoint.");
        }

        var existing = await _dbContext.BranchItemAvailabilities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BranchId == branchId && a.MenuItemId == itemId && a.ItemVariantId == variantId, ct);

        BranchItemAvailability record;
        if (existing is not null)
        {
            VerifyConcurrencyToken(existing.ConcurrencyToken, command.ConcurrencyToken);
            existing.MarkUnavailable(reasonCode, command.Note, command.ExpectedAvailableAtUtc, new UserId(actor.SubjectId));
            record = existing;
        }
        else
        {
            VerifyConcurrencyToken(variant.ConcurrencyToken, command.ConcurrencyToken);
            record = BranchItemAvailability.CreateVariantUnavailable(
                tenantId,
                branchId,
                itemId,
                variantId,
                reasonCode,
                command.Note,
                command.ExpectedAvailableAtUtc,
                new UserId(actor.SubjectId));
            _dbContext.BranchItemAvailabilities.Add(record);
        }

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.ItemVariantAvailabilityChanged,
            actor,
            branchId,
            new
            {
                MenuItemId = itemId.Value,
                ItemVariantId = variantId.Value,
                IsAvailable = false,
                ReasonCode = reasonCode.ToString(),
                command.Note,
                command.ExpectedAvailableAtUtc
            });

        var eventData = new CatalogAvailabilityChangedEvent(
            TenantId: tenantId.Value,
            BranchId: branchId.Value,
            MenuItemId: itemId.Value,
            ItemVariantId: variantId.Value,
            IsAvailable: false,
            ReasonCode: reasonCode.ToString(),
            Note: record.Note,
            ExpectedAvailableAtUtc: record.ExpectedAvailableAtUtc,
            OccurredAtUtc: record.ChangedAtUtc);

        EnqueueAvailabilityOutboxMessage(
            tenantId,
            branchId,
            variantId.Value.ToString(),
            "CatalogVariantQuick86",
            eventData,
            $"variant-86-{tenantId.Value}-{branchId.Value}-{variantId.Value}-{command.ConcurrencyToken:D}");

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

        return MapAvailability(record);
    }

    public async Task<BranchItemAvailabilityDto> RestockVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        RestockVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureQuick86Permission(actor);
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsCatalogMutation(branch);

        var (item, menu) = await GetItemAndMenuAsync(tenantId, branchId, menuId, itemId, ct);
        EnsureItemAllowsAvailabilityChange(item, menu);
        await EnsurePreparationStationScopeAsync(item, branchId, tenantId, actor, ct);

        var variant = await _dbContext.ItemVariants
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.BranchId == branchId && v.MenuId == menuId && v.MenuItemId == itemId && v.Id == variantId, ct)
            ?? throw new ResourceNotFoundException($"ItemVariant '{variantId.Value}' was not found.");

        if (!variant.IsActive)
        {
            throw new DomainException("Cannot change availability of an inactive variant.");
        }

        var existing = await _dbContext.BranchItemAvailabilities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BranchId == branchId && a.MenuItemId == itemId && a.ItemVariantId == variantId, ct);

        BranchItemAvailability record;
        if (existing is not null)
        {
            VerifyConcurrencyToken(existing.ConcurrencyToken, command.ConcurrencyToken);
            existing.Restock(command.Note, new UserId(actor.SubjectId));
            record = existing;
        }
        else
        {
            VerifyConcurrencyToken(variant.ConcurrencyToken, command.ConcurrencyToken);
            record = BranchItemAvailability.CreateVariantUnavailable(
                tenantId,
                branchId,
                itemId,
                variantId,
                AvailabilityReasonCode.Manual,
                null,
                null,
                new UserId(actor.SubjectId));
            record.Restock(command.Note, new UserId(actor.SubjectId));
            _dbContext.BranchItemAvailabilities.Add(record);
        }

        AddAuditEvent(
            tenantId,
            SecurityAuditEventType.ItemVariantRestocked,
            actor,
            branchId,
            new
            {
                MenuItemId = itemId.Value,
                ItemVariantId = variantId.Value,
                IsAvailable = true,
                ReasonCode = AvailabilityReasonCode.Restocked.ToString(),
                command.Note
            });

        var restockEventData = new CatalogAvailabilityChangedEvent(
            TenantId: tenantId.Value,
            BranchId: branchId.Value,
            MenuItemId: itemId.Value,
            ItemVariantId: variantId.Value,
            IsAvailable: true,
            ReasonCode: AvailabilityReasonCode.Restocked.ToString(),
            Note: record.Note,
            ExpectedAvailableAtUtc: null,
            OccurredAtUtc: record.ChangedAtUtc);

        EnqueueAvailabilityOutboxMessage(
            tenantId,
            branchId,
            variantId.Value.ToString(),
            "CatalogVariantRestocked",
            restockEventData,
            $"variant-restock-{tenantId.Value}-{branchId.Value}-{variantId.Value}-{command.ConcurrencyToken:D}");

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

        return MapAvailability(record);
    }
}
