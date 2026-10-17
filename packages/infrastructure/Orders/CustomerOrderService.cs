using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed class CustomerOrderService : ICustomerOrderService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IOrderPricingEngine _pricingEngine;

    public CustomerOrderService(
        RestaurantOrderDbContext dbContext,
        IOrderPricingEngine pricingEngine)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
    }

    public async Task<CreateCustomerOrderResult> CreateOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateCustomerOrderRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null || request.Items == null || request.Items.Count == 0)
        {
            throw new DomainException("Cannot submit an empty order. An order must contain at least one item.");
        }

        var sanitizedKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new DomainException("Idempotency-Key header is required.")
            : idempotencyKey.Trim();

        if (sanitizedKey.Length > Order.MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {Order.MaxIdempotencyKeyLength} characters.");
        }

        // Validate item quantity bounds
        foreach (var item in request.Items)
        {
            if (item.Quantity < OrderItem.MinQuantity || item.Quantity > OrderItem.MaxQuantity)
            {
                throw new DomainException($"Quantity must be between {OrderItem.MinQuantity} and {OrderItem.MaxQuantity}. Received: {item.Quantity}.");
            }
        }

        var fingerprint = ComputeRequestFingerprint(sessionId, request);

        // Begin transaction with advisory locking to ensure round sequence consistency
        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        if (_dbContext.Database.IsNpgsql())
        {
            var lockKey = $"order_session_{sessionId.Value:D}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
                cancellationToken);
        }

        // 1. Verify dining session state
        var session = await _dbContext.DiningSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, cancellationToken);

        if (session == null || session.Status != DiningSessionStatus.Active)
        {
            throw OrderPricingException.SessionNotActive(sessionId.Value);
        }

        // 2. Idempotency Check
        var existingOrder = await _dbContext.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.IdempotencyKey == sanitizedKey, cancellationToken);

        if (existingOrder != null)
        {
            if (existingOrder.DiningSessionId == sessionId &&
                string.Equals(existingOrder.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                // Identical replay: return existing order
                await tx.RollbackAsync(cancellationToken);
                return new CreateCustomerOrderResult(MapToDto(existingOrder), IsReplay: true);
            }

            throw new IdempotencyConflictException("The provided Idempotency-Key has already been used for a different order payload or session.");
        }

        // 3. Authoritative Pricing & Validation
        var clientDrafts = request.Items.Select(i => new ClientOrderItemDraft(
            new MenuItemId(i.MenuItemId),
            i.ItemVariantId.HasValue ? new ItemVariantId(i.ItemVariantId.Value) : null,
            i.Quantity,
            i.CustomerNote,
            i.SelectedModifiers?.Select(m => new ClientSelectedModifierDraft(m.ModifierGroupId, m.ModifierOptionId)).ToList(),
            i.ClientExpectedUnitPriceMinorUnits)).ToList();

        var pricedResult = await _pricingEngine.PriceAndValidateOrderAsync(
            tenantId,
            branchId,
            sessionId,
            clientDrafts,
            cancellationToken);

        // 4. Determine next round number within session
        var currentMaxRound = await _dbContext.Orders
            .Where(o => o.TenantId == tenantId && o.DiningSessionId == sessionId)
            .MaxAsync(o => (int?)o.RoundNumber, cancellationToken) ?? 0;

        var nextRound = currentMaxRound + 1;
        var nowUtc = DateTimeOffset.UtcNow;
        var orderId = OrderId.New();

        // 5. Build domain OrderItem snapshots
        var domainItems = new List<OrderItem>();
        foreach (var pricedLine in pricedResult.LineItems)
        {
            var lineModifiers = pricedLine.Modifiers.Select(m => OrderItemModifier.Create(
                OrderItemId.New(),
                m.ModifierGroupId,
                m.ModifierGroupName,
                m.ModifierOptionId,
                m.ModifierOptionName,
                m.PriceDeltaMinorUnits)).ToList();

            var orderItem = OrderItem.Create(
                orderId,
                tenantId,
                pricedLine.MenuItemId,
                pricedLine.ItemVariantId,
                pricedLine.ItemName,
                pricedLine.VariantName,
                pricedLine.Quantity,
                pricedLine.UnitPriceMinorUnits,
                pricedLine.TaxRateBasisPoints,
                pricedLine.PreparationStationId,
                pricedLine.CustomerNote,
                lineModifiers,
                nowUtc);

            domainItems.Add(orderItem);
        }

        // 6. Build Order aggregate with authoritative pricing snapshots
        var order = Order.CreateSubmittedWithTotals(
            tenantId,
            branchId,
            sessionId,
            session.TableId,
            nextRound,
            OrderSource.Customer,
            pricedResult.Currency,
            sanitizedKey,
            domainItems,
            pricedResult.SubtotalMinorUnits,
            pricedResult.TaxMinorUnits,
            pricedResult.ServiceChargeMinorUnits,
            pricedResult.TotalMinorUnits,
            nowUtc,
            fingerprint);

        _dbContext.Orders.Add(order);

        // 7. Audit entry (redacted customer notes for security log safety)
        var auditDetails = new
        {
            roundNumber = nextRound,
            itemCount = domainItems.Count,
            totalMinorUnits = order.TotalMinorUnits,
            currency = order.Currency.Code,
            hasNotes = domainItems.Any(i => !string.IsNullOrWhiteSpace(i.CustomerNote)),
            itemsWithNotesCount = domainItems.Count(i => !string.IsNullOrWhiteSpace(i.CustomerNote))
        };

        var audit = OrderAuditEntry.Record(
            tenantId,
            order.Id,
            sessionId,
            actor.SubjectId,
            actorRole: "Customer",
            action: "OrderSubmitted",
            previousStatus: null,
            newStatus: OrderStatus.Submitted,
            reason: "Customer QR order submitted",
            detailsJson: JsonSerializer.Serialize(auditDetails),
            nowUtc: nowUtc);

        _dbContext.OrderAuditEntries.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new CreateCustomerOrderResult(MapToDto(order), IsReplay: false);
    }

    public async Task<IReadOnlyList<CustomerOrderResponseDto>> GetOrdersAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .Where(o => o.TenantId == tenantId && o.BranchId == branchId && o.DiningSessionId == sessionId)
            .OrderBy(o => o.RoundNumber)
            .ToListAsync(cancellationToken);

        return orders.Select(MapToDto).ToList();
    }

    public async Task<CustomerOrderResponseDto?> GetOrderByIdAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(
                o => o.TenantId == tenantId &&
                     o.BranchId == branchId &&
                     o.DiningSessionId == sessionId &&
                     o.Id == orderId,
                cancellationToken);

        return order == null ? null : MapToDto(order);
    }

    private static string ComputeRequestFingerprint(DiningSessionId sessionId, CreateCustomerOrderRequest request)
    {
        var sortedItems = request.Items
            .OrderBy(i => i.MenuItemId)
            .ThenBy(i => i.ItemVariantId ?? Guid.Empty)
            .ThenBy(i => i.Quantity)
            .ThenBy(i => i.CustomerNote ?? string.Empty)
            .Select(i => new
        {
            mId = i.MenuItemId,
            vId = i.ItemVariantId,
            qty = i.Quantity,
            note = i.CustomerNote?.Trim(),
            exp = i.ClientExpectedUnitPriceMinorUnits,
            mods = i.SelectedModifiers?
                .OrderBy(m => m.ModifierGroupId)
                .ThenBy(m => m.ModifierOptionId)
                .Select(m => new { g = m.ModifierGroupId, o = m.ModifierOptionId })
                .ToList()
        }).ToList();

        var payload = JsonSerializer.Serialize(new { s = sessionId.Value, items = sortedItems });
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..Order.MaxFingerprintLength];
    }

    private static CustomerOrderResponseDto MapToDto(Order order)
    {
        var itemDtos = order.Items.Select(i => new CustomerOrderItemDto(
            i.Id.Value,
            i.MenuItemId.Value,
            i.ItemVariantId?.Value,
            i.ItemName,
            i.VariantName,
            i.Quantity,
            i.CustomerNote,
            i.UnitPriceMinorUnits,
            i.ModifierTotalMinorUnits,
            i.LineTotalMinorUnits,
            i.TaxRateBasisPoints,
            i.Status.ToString(),
            i.Modifiers.Select(m => new CustomerOrderItemModifierDto(
                m.Id,
                m.ModifierGroupId,
                m.ModifierGroupName,
                m.ModifierOptionId,
                m.ModifierOptionName,
                m.PriceDeltaMinorUnits)).ToList())).ToList();

        return new CustomerOrderResponseDto(
            order.Id.Value,
            order.TenantId.Value,
            order.BranchId.Value,
            order.DiningSessionId.Value,
            order.TableId.Value,
            order.RoundNumber,
            order.OrderSource.ToString(),
            order.Status.ToString(),
            order.Currency.Code,
            order.SubtotalMinorUnits,
            order.TaxMinorUnits,
            order.ServiceChargeMinorUnits,
            order.TotalMinorUnits,
            order.IdempotencyKey,
            order.ConcurrencyToken,
            order.CreatedAtUtc,
            order.SubmittedAtUtc,
            itemDtos);
    }
}
