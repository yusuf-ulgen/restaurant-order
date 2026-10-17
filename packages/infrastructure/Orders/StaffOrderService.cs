using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Application.RestaurantConfig;
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

public sealed partial class StaffOrderService : IStaffOrderService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IOrderPricingEngine _pricingEngine;
    private readonly IPermissionRegistry _permissionRegistry;
    private readonly IPinHasher _pinHasher;

    public StaffOrderService(
        RestaurantOrderDbContext dbContext,
        IOrderPricingEngine pricingEngine,
        IPermissionRegistry permissionRegistry,
        IPinHasher pinHasher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
        _permissionRegistry = permissionRegistry ?? throw new ArgumentNullException(nameof(permissionRegistry));
        _pinHasher = pinHasher ?? throw new ArgumentNullException(nameof(pinHasher));
    }

    public async Task<CreateStaffOrderResult> CreateStaffOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateStaffOrderRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

        if (!_permissionRegistry.IsPermitted(actor, Permissions.OrdersStaffCreate))
        {
            throw new InvalidAuthorizationScopeException("Missing required permission 'orders.staff.create'.");
        }

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

        foreach (var item in request.Items)
        {
            if (item.Quantity < OrderItem.MinQuantity || item.Quantity > OrderItem.MaxQuantity)
            {
                throw new DomainException($"Quantity must be between {OrderItem.MinQuantity} and {OrderItem.MaxQuantity}. Received: {item.Quantity}.");
            }
        }

        var fingerprint = ComputeRequestFingerprint(sessionId, request);

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
                await tx.RollbackAsync(cancellationToken);
                return new CreateStaffOrderResult(MapToDto(existingOrder), IsReplay: true);
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

        // 4. Determine next round number
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

        // 6. Build Order aggregate with Staff source
        var order = Order.CreateSubmittedWithTotals(
            tenantId,
            branchId,
            sessionId,
            session.TableId,
            nextRound,
            OrderSource.Staff,
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

        // 7. Audit entry (customer notes strictly masked)
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
            actorRole: actor.Role.ToString(),
            action: "StaffOrderCreated",
            previousStatus: null,
            newStatus: OrderStatus.Submitted,
            reason: "Staff order created",
            detailsJson: JsonSerializer.Serialize(auditDetails),
            nowUtc: nowUtc);

        _dbContext.OrderAuditEntries.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new CreateStaffOrderResult(MapToDto(order), IsReplay: false);
    }

    public async Task<IReadOnlyList<StaffOrderResponseDto>> GetSessionOrdersAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

        var sessionExists = await _dbContext.DiningSessions
            .AsNoTracking()
            .AnyAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, cancellationToken);

        if (!sessionExists)
        {
            throw new ResourceNotFoundException("Dining session not found.");
        }

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .Where(o => o.TenantId == tenantId && o.BranchId == branchId && o.DiningSessionId == sessionId)
            .OrderBy(o => o.RoundNumber)
            .ToListAsync(cancellationToken);

        return orders.Select(MapToDto).ToList();
    }

    public async Task<StaffOrderResponseDto> GetOrderByIdAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

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

        if (order == null)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        return MapToDto(order);
    }

    private static void ValidateStaffActor(AuthenticatedPrincipal actor, BranchId branchId)
    {
        if (actor == null)
        {
            throw new InvalidAuthorizationScopeException("Authentication required.");
        }

        if (actor.Role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Customer cannot access staff order endpoints.");
        }

        if (!actor.Scope.TenantId.HasValue || actor.Scope.TenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidAuthorizationScopeException("Actor is missing required tenant scope.");
        }

        if (actor.Role is AuthRole.Waiter or AuthRole.Cashier or AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId)
            {
                throw new InvalidAuthorizationScopeException("Staff member is not assigned to the requested branch.");
            }
        }
    }

    private static string ComputeRequestFingerprint(DiningSessionId sessionId, CreateStaffOrderRequest request)
    {
        var sb = new StringBuilder();
        sb.Append($"session:{sessionId.Value:N}|items:");
        foreach (var item in request.Items.OrderBy(i => i.MenuItemId).ThenBy(i => i.ItemVariantId ?? Guid.Empty))
        {
            sb.Append($"[{item.MenuItemId:N}:{item.ItemVariantId?.ToString("N") ?? "none"}:{item.Quantity}:");
            if (item.SelectedModifiers != null)
            {
                foreach (var mod in item.SelectedModifiers.OrderBy(m => m.ModifierGroupId).ThenBy(m => m.ModifierOptionId))
                {
                    sb.Append($"{mod.ModifierGroupId:N}>{mod.ModifierOptionId:N};");
                }
            }
            sb.Append(']');
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static StaffOrderResponseDto MapToDto(Order order)
    {
        var items = order.Items.Select(i => new StaffOrderItemDto(
            i.Id.Value,
            i.MenuItemId.Value,
            i.ItemVariantId?.Value,
            i.ItemName,
            i.VariantName,
            i.Quantity,
            i.CustomerNote,
            i.PreparationStationId,
            i.UnitPriceMinorUnits,
            i.ModifierTotalMinorUnits,
            i.LineTotalMinorUnits,
            i.TaxRateBasisPoints,
            i.Status.ToString(),
            i.CreatedAtUtc,
            i.UpdatedAtUtc,
            i.Modifiers.Select(m => new StaffOrderItemModifierDto(
                m.Id,
                m.ModifierGroupId,
                m.ModifierGroupName,
                m.ModifierOptionId,
                m.ModifierOptionName,
                m.PriceDeltaMinorUnits)).ToList())).ToList();

        return new StaffOrderResponseDto(
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
            order.UpdatedAtUtc,
            order.CancellationReason,
            order.CancelledByActorId,
            items);
    }
}
