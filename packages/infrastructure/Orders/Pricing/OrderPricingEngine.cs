using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders.Pricing;

/// <summary>
/// Authoritative implementation of the order pricing engine using EF Core and PostgreSQL.
/// Validates dining session state, branch order gates, active catalog hierarchy,
/// inventory stock availability (86), and modifier bounds before calculating minor-unit financials.
/// </summary>
public sealed class OrderPricingEngine : IOrderPricingEngine
{
    private readonly RestaurantOrderDbContext _dbContext;

    public OrderPricingEngine(RestaurantOrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PricedOrderResult> PriceAndValidateOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        IReadOnlyList<ClientOrderItemDraft> items,
        string? clientPricingPolicyFingerprint = null,
        long? clientExpectedTotalMinorUnits = null,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new DomainException("Cannot price an empty order. An order must contain at least one item.");
        }

        // 1. Verify Dining Session
        var session = await _dbContext.DiningSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, cancellationToken);

        if (session == null || session.Status != DiningSessionStatus.Active)
        {
            throw OrderPricingException.SessionNotActive(sessionId.Value);
        }

        // 2. Load Branch, Settings, and Resolve Effective Pricing Policy
        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branchId, cancellationToken);

        if (branch == null)
        {
            throw new DomainException($"Branch '{branchId.Value}' was not found.");
        }

        var settings = await _dbContext.BranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId, cancellationToken);

        var effectivePolicy = EffectiveBranchPricingPolicy.Create(branch, settings);

        if (!effectivePolicy.IsOrderTakingEnabled)
        {
            throw OrderPricingException.OrderTakingDisabled();
        }

        if (string.IsNullOrWhiteSpace(clientPricingPolicyFingerprint))
        {
            throw new DomainException("Client pricing policy fingerprint is required.");
        }

        var normalizedClientFingerprint = clientPricingPolicyFingerprint.Trim().ToLowerInvariant();
        if (normalizedClientFingerprint.Length != 64 || !normalizedClientFingerprint.All(Uri.IsHexDigit))
        {
            throw new DomainException("Invalid pricing policy fingerprint format.");
        }

        if (!string.Equals(normalizedClientFingerprint, effectivePolicy.PricingPolicyFingerprint, StringComparison.Ordinal))
        {
            throw OrderPricingException.PricingPolicyChanged(normalizedClientFingerprint, effectivePolicy.PricingPolicyFingerprint);
        }

        // 3. Load and Validate Active Catalog Hierarchy
        var menuItemIds = items.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _dbContext.MenuItems
            .AsNoTracking()
            .Include(m => m.Variants)
            .Include(m => m.ModifierGroupAssignments)
            .Where(m => m.TenantId == tenantId && m.BranchId == branchId && menuItemIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        var menuIds = menuItems.Select(m => m.MenuId).Distinct().ToList();
        var activeMenus = await _dbContext.Menus
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.BranchId == branchId && menuIds.Contains(m.Id) && m.Status == MenuStatus.Active)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var categoryIds = menuItems.Select(m => m.CategoryId).Distinct().ToList();
        var activeCategories = await _dbContext.MenuCategories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.BranchId == branchId && categoryIds.Contains(c.Id) && c.IsActive)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        // 4. Load Out-of-stock (Quick 86) records
        var unavailableRecords = await _dbContext.BranchItemAvailabilities
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.BranchId == branchId && menuItemIds.Contains(a.MenuItemId) && !a.IsAvailable)
            .ToListAsync(cancellationToken);

        // 5. Load Referenced Modifier Groups and Options (both assigned to items and selected by client)
        var assignedModifierGroupIds = menuItems
            .SelectMany(m => m.ModifierGroupAssignments)
            .Select(a => a.ModifierGroupId);

        var selectedModifierGroupIds = items
            .Where(i => i.SelectedModifiers != null)
            .SelectMany(i => i.SelectedModifiers!)
            .Select(m => new ModifierGroupId(m.ModifierGroupId));

        var allModifierGroupIds = assignedModifierGroupIds
            .Concat(selectedModifierGroupIds)
            .Distinct()
            .ToList();

        var modifierGroups = await _dbContext.ModifierGroups
            .AsNoTracking()
            .Include(g => g.Options)
            .Where(g => g.TenantId == tenantId && g.BranchId == branchId && allModifierGroupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, cancellationToken);

        var pricedLineItems = new List<PricedOrderLineItem>();

        foreach (var draft in items)
        {
            var menuItem = menuItems.FirstOrDefault(m => m.Id == draft.MenuItemId);
            if (menuItem == null || !menuItem.IsActive)
            {
                throw OrderPricingException.ItemNotActive(draft.MenuItemId);
            }

            if (!activeMenus.ContainsKey(menuItem.MenuId))
            {
                throw OrderPricingException.ItemNotActive(draft.MenuItemId);
            }

            if (!activeCategories.ContainsKey(menuItem.CategoryId))
            {
                throw OrderPricingException.ItemNotActive(draft.MenuItemId);
            }

            // Check item-level 86
            if (unavailableRecords.Any(a => a.MenuItemId == menuItem.Id && a.ItemVariantId == null))
            {
                throw OrderPricingException.ItemOutOfStock(menuItem.Id);
            }

            // Variant validation
            ItemVariant? selectedVariant = null;
            long itemBasePriceMinorUnits;

            if (draft.ItemVariantId.HasValue)
            {
                selectedVariant = menuItem.Variants.FirstOrDefault(v => v.Id == draft.ItemVariantId.Value);
                if (selectedVariant == null)
                {
                    throw OrderPricingException.InvalidVariant(menuItem.Id, draft.ItemVariantId.Value);
                }

                if (!selectedVariant.IsActive)
                {
                    throw OrderPricingException.ItemOutOfStock(menuItem.Id, selectedVariant.Id);
                }

                if (unavailableRecords.Any(a => a.MenuItemId == menuItem.Id && a.ItemVariantId == selectedVariant.Id))
                {
                    throw OrderPricingException.ItemOutOfStock(menuItem.Id, selectedVariant.Id);
                }

                itemBasePriceMinorUnits = selectedVariant.AbsolutePriceMinorUnits.MinorUnits;
            }
            else
            {
                itemBasePriceMinorUnits = menuItem.BasePriceMinorUnits.MinorUnits;
            }

            // Modifier group bounds and option selections
            var lineModifiers = new List<OrderItemModifier>();
            long modifierSum = 0;

            if (draft.SelectedModifiers != null && draft.SelectedModifiers.Count > 0)
            {
                // Check duplicate modifier option selection
                var optionIdSet = new HashSet<Guid>();
                foreach (var mod in draft.SelectedModifiers)
                {
                    if (!optionIdSet.Add(mod.ModifierOptionId))
                    {
                        throw OrderPricingException.DuplicateModifier(mod.ModifierOptionId, menuItem.Id);
                    }
                }

                // Group-level validation
                var groupedSelections = draft.SelectedModifiers
                    .GroupBy(m => new ModifierGroupId(m.ModifierGroupId))
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var (groupId, selections) in groupedSelections)
                {
                    var isAssigned = menuItem.ModifierGroupAssignments.Any(a => a.ModifierGroupId == groupId);
                    if (!isAssigned)
                    {
                        throw OrderPricingException.InvalidModifierSelection($"Modifier grubu '{groupId.Value}' bu ürüne atanmamış.", menuItem.Id);
                    }

                    if (!modifierGroups.TryGetValue(groupId, out var group) || !group.IsActive)
                    {
                        throw OrderPricingException.InvalidModifierSelection($"Modifier grubu '{groupId.Value}' aktif değil.", menuItem.Id);
                    }

                    if (selections.Count < group.MinSelections || selections.Count > group.MaxSelections)
                    {
                        throw OrderPricingException.InvalidModifierSelection(
                            $"'{group.Name}' grubu için seçim sayısı ({selections.Count}) izin verilen sınırların ({group.MinSelections}-{group.MaxSelections}) dışında.",
                            menuItem.Id);
                    }

                    foreach (var sel in selections)
                    {
                        var opt = group.Options.FirstOrDefault(o => o.Id.Value == sel.ModifierOptionId);
                        if (opt == null || !opt.IsActive)
                        {
                            throw OrderPricingException.InvalidModifierSelection($"Modifier seçeneği '{sel.ModifierOptionId}' bulunamadı veya aktif değil.", menuItem.Id);
                        }

                        var priceDelta = opt.PriceDeltaMinorUnits.MinorUnits;
                        checked
                        {
                            modifierSum += priceDelta;
                        }

                        lineModifiers.Add(OrderItemModifier.Create(
                            OrderItemId.New(),
                            group.Id.Value,
                            group.Name,
                            opt.Id.Value,
                            opt.Name,
                            priceDelta));
                    }
                }
            }

            // Check min selections for mandatory assigned modifier groups not included in draft
            foreach (var assignment in menuItem.ModifierGroupAssignments)
            {
                if (modifierGroups.TryGetValue(assignment.ModifierGroupId, out var group) && group.IsActive && group.MinSelections > 0)
                {
                    var selectedCount = draft.SelectedModifiers?.Count(m => m.ModifierGroupId == group.Id.Value) ?? 0;
                    if (selectedCount < group.MinSelections)
                    {
                        throw OrderPricingException.InvalidModifierSelection(
                            $"Zorunlu '{group.Name}' grubu için en az {group.MinSelections} seçim yapılmalıdır.",
                            menuItem.Id);
                    }
                }
            }

            // Detect Client Price Manipulation / Price Change Race
            long effectiveUnitPrice;
            checked
            {
                effectiveUnitPrice = itemBasePriceMinorUnits + modifierSum;
            }

            if (draft.ClientExpectedUnitPriceMinorUnits.HasValue && draft.ClientExpectedUnitPriceMinorUnits.Value != effectiveUnitPrice)
            {
                throw OrderPricingException.PriceChanged(
                    menuItem.Id,
                    draft.ItemVariantId,
                    draft.ClientExpectedUnitPriceMinorUnits.Value,
                    effectiveUnitPrice);
            }

            // Calculate item financials
            var (lineTotal, netSubtotal, tax) = OrderPricingCalculator.CalculateLineItemFinancials(
                itemBasePriceMinorUnits,
                modifierSum,
                draft.Quantity,
                effectivePolicy.DefaultTaxRateBps,
                effectivePolicy.PricesIncludeTax);

            pricedLineItems.Add(new PricedOrderLineItem(
                menuItem.Id,
                selectedVariant?.Id,
                menuItem.Name,
                selectedVariant?.Name,
                draft.Quantity,
                itemBasePriceMinorUnits,
                modifierSum,
                lineTotal,
                netSubtotal,
                tax,
                effectivePolicy.DefaultTaxRateBps,
                menuItem.PreparationStationId?.Value,
                draft.CustomerNote,
                lineModifiers));
        }

        // Calculate order totals
        long totalNetSubtotal = 0;
        long totalTax = 0;
        foreach (var line in pricedLineItems)
        {
            checked
            {
                totalNetSubtotal += line.NetSubtotalMinorUnits;
                totalTax += line.TaxMinorUnits;
            }
        }

        var (serviceCharge, grandTotal) = OrderPricingCalculator.CalculateOrderTotals(
            totalNetSubtotal,
            totalTax,
            effectivePolicy.IsServiceChargeEnabled,
            effectivePolicy.ServiceChargeRateBps);

        if (!clientExpectedTotalMinorUnits.HasValue)
        {
            throw new DomainException("Client expected total minor units is required.");
        }

        if (clientExpectedTotalMinorUnits.Value < 0)
        {
            throw new DomainException("Invalid client expected total minor units.");
        }

        if (clientExpectedTotalMinorUnits.Value != grandTotal)
        {
            throw OrderPricingException.OrderTotalChanged(clientExpectedTotalMinorUnits.Value, grandTotal);
        }

        return new PricedOrderResult(
            totalNetSubtotal,
            totalTax,
            serviceCharge,
            grandTotal,
            branch.Currency,
            effectivePolicy.PricesIncludeTax,
            effectivePolicy.DefaultTaxRateBps,
            effectivePolicy.ServiceChargeRateBps,
            pricedLineItems,
            effectivePolicy.PricingPolicyFingerprint);
    }
}
