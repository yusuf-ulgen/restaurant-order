using System.Security.Cryptography;
using System.Text;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Order aggregate root representing a customer or staff placed round of ordering
/// within an active dining session.
/// </summary>
public sealed partial class Order
{
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxFingerprintLength = 64;
    public const int MaxReasonLength = 500;

    private readonly List<OrderItem> _items = new();

    public OrderId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public DiningSessionId DiningSessionId { get; private set; }
    public RestaurantTableId TableId { get; private set; }
    public int RoundNumber { get; private set; }
    public OrderSource OrderSource { get; private set; }
    public Currency Currency { get; private set; }
    public OrderStatus Status { get; private set; }
    public long SubtotalMinorUnits { get; private set; }
    public long TaxMinorUnits { get; private set; }
    public long ServiceChargeMinorUnits { get; private set; }
    public long TotalMinorUnits { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? CancelledByActorId { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    // Parameterless constructor for EF Core materialization
    private Order()
    {
    }

    /// <summary>
    /// Creates a newly submitted Order aggregate root with all line item snapshots and checked calculations.
    /// Empty order submission is strictly prohibited.
    /// </summary>
    public static Order CreateSubmitted(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        RestaurantTableId tableId,
        int roundNumber,
        OrderSource orderSource,
        Currency currency,
        string idempotencyKey,
        IReadOnlyList<OrderItem> items,
        long serviceChargeMinorUnits = 0,
        DateTimeOffset? nowUtc = null,
        string? requestFingerprint = null)
    {
        if (roundNumber <= 0)
        {
            throw new DomainException($"Round number must be greater than zero. Received: {roundNumber}.");
        }

        if (items == null || items.Count == 0)
        {
            throw new DomainException("Cannot submit an empty order. An order must contain at least one item.");
        }

        if (serviceChargeMinorUnits < 0)
        {
            throw new DomainException("Service charge minor units cannot be negative.");
        }

        var sanitizedKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new DomainException("Idempotency key is required for order submission.")
            : idempotencyKey.Trim();

        if (sanitizedKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        long calculatedSubtotal = 0;
        long calculatedTax = 0;

        foreach (var item in items)
        {
            if (item.TenantId != tenantId)
            {
                throw new DomainException("All order items must belong to the same tenant as the order.");
            }

            checked
            {
                calculatedSubtotal += item.LineTotalMinorUnits;
                // Tax snapshot calculation per line item: (LineTotal * basisPoints) / 10000
                var lineTax = (item.LineTotalMinorUnits * item.TaxRateBasisPoints) / 10000L;
                calculatedTax += lineTax;
            }
        }

        long calculatedTotal;
        checked
        {
            calculatedTotal = calculatedSubtotal + calculatedTax + serviceChargeMinorUnits;
        }

        return CreateSubmittedWithTotals(
            tenantId,
            branchId,
            diningSessionId,
            tableId,
            roundNumber,
            orderSource,
            currency,
            sanitizedKey,
            items,
            calculatedSubtotal,
            calculatedTax,
            serviceChargeMinorUnits,
            calculatedTotal,
            nowUtc,
            requestFingerprint);
    }

    /// <summary>
    /// Creates a newly submitted Order with authoritative pricing snapshots calculated by the pricing engine.
    /// </summary>
    public static Order CreateSubmittedWithTotals(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        RestaurantTableId tableId,
        int roundNumber,
        OrderSource orderSource,
        Currency currency,
        string idempotencyKey,
        IReadOnlyList<OrderItem> items,
        long subtotalMinorUnits,
        long taxMinorUnits,
        long serviceChargeMinorUnits,
        long totalMinorUnits,
        DateTimeOffset? nowUtc = null,
        string? requestFingerprint = null)
    {
        if (roundNumber <= 0)
        {
            throw new DomainException($"Round number must be greater than zero. Received: {roundNumber}.");
        }

        if (items == null || items.Count == 0)
        {
            throw new DomainException("Cannot submit an empty order. An order must contain at least one item.");
        }

        if (subtotalMinorUnits < 0 || taxMinorUnits < 0 || serviceChargeMinorUnits < 0 || totalMinorUnits < 0)
        {
            throw new DomainException("Financial snapshot totals cannot be negative.");
        }

        var sanitizedKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new DomainException("Idempotency key is required for order submission.")
            : idempotencyKey.Trim();

        if (sanitizedKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        foreach (var item in items)
        {
            if (item.TenantId != tenantId)
            {
                throw new DomainException("All order items must belong to the same tenant as the order.");
            }
        }

        var orderId = OrderId.New();
        var creationTime = nowUtc ?? DateTimeOffset.UtcNow;

        var computedFingerprint = !string.IsNullOrWhiteSpace(requestFingerprint)
            ? requestFingerprint.Trim()
            : ComputeFingerprint(tenantId, branchId, diningSessionId, tableId, roundNumber, orderSource, currency, items.Count, totalMinorUnits, sanitizedKey);

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            BranchId = branchId,
            DiningSessionId = diningSessionId,
            TableId = tableId,
            RoundNumber = roundNumber,
            OrderSource = orderSource,
            Currency = currency,
            Status = OrderStatus.Submitted,
            SubtotalMinorUnits = subtotalMinorUnits,
            TaxMinorUnits = taxMinorUnits,
            ServiceChargeMinorUnits = serviceChargeMinorUnits,
            TotalMinorUnits = totalMinorUnits,
            IdempotencyKey = sanitizedKey,
            RequestFingerprint = computedFingerprint,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = creationTime,
            SubmittedAtUtc = creationTime,
            UpdatedAtUtc = null
        };

        foreach (var item in items)
        {
            if (item.Status == OrderItemStatus.Draft)
            {
                item.MarkSubmitted(creationTime);
            }

            order._items.Add(item);
        }

        return order;
    }

    public void StartPreparation(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderStatus.Submitted)
        {
            throw new DomainException($"Cannot start preparation for Order in status '{Status}'. Only Submitted orders can transition to InPreparation.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.InPreparation;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;

        foreach (var item in _items)
        {
            if (item.Status == OrderItemStatus.Submitted)
            {
                item.StartPreparation(timestamp);
            }
        }
    }

    public void MarkReady(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderStatus.InPreparation)
        {
            throw new DomainException($"Cannot mark Order as Ready from status '{Status}'. Only InPreparation orders can transition to Ready.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Ready;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;

        foreach (var item in _items)
        {
            if (item.Status == OrderItemStatus.InPreparation)
            {
                item.MarkReady(timestamp);
            }
        }
    }

    public void MarkServed(DateTimeOffset? nowUtc = null)
    {
        if (Status != OrderStatus.Ready)
        {
            throw new DomainException($"Cannot mark Order as Served from status '{Status}'. Only Ready orders can transition to Served.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Served;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;

        foreach (var item in _items)
        {
            if (item.Status == OrderItemStatus.Ready)
            {
                item.MarkServed(timestamp);
            }
        }
    }

    /// <summary>
    /// Cancels the order by an authorized staff member. Customers cannot directly cancel an order.
    /// Orders that are already served cannot be cancelled.
    /// </summary>
    public void CancelByStaff(Guid staffActorId, string staffRole, string reason, DateTimeOffset? nowUtc = null)
    {
        if (staffActorId == Guid.Empty)
        {
            throw new DomainException("Staff actor ID is required to cancel an order.");
        }

        if (string.IsNullOrWhiteSpace(staffRole) || staffRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Customer cannot directly cancel an order. Only authorized staff can cancel orders.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancellation reason is required.");
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new DomainException($"Cancellation reason cannot exceed {MaxReasonLength} characters.");
        }

        if (Status == OrderStatus.Served)
        {
            throw new DomainException("Cannot cancel an order that has already been served.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException("Order is already cancelled.");
        }

        var timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Cancelled;
        CancellationReason = trimmedReason;
        CancelledByActorId = staffActorId;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = timestamp;

        foreach (var item in _items)
        {
            if (item.Status != OrderItemStatus.Served && item.Status != OrderItemStatus.Cancelled)
            {
                item.CancelByStaff(trimmedReason, timestamp);
            }
        }
    }

    public static string ComputeFingerprint(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        RestaurantTableId tableId,
        int roundNumber,
        OrderSource orderSource,
        Currency currency,
        int itemCount,
        long totalMinorUnits,
        string idempotencyKey)
    {
        var canonical = $"tenant:{tenantId.Value:N}|branch:{branchId.Value:N}|session:{diningSessionId.Value:N}|table:{tableId.Value:N}|round:{roundNumber}|source:{orderSource}|currency:{currency.Code}|items:{itemCount}|total:{totalMinorUnits}|key:{idempotencyKey.Trim()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hashBytes);
    }

    public static bool MatchesFingerprint(string? storedFingerprint, string? computedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(storedFingerprint) || string.IsNullOrWhiteSpace(computedFingerprint))
        {
            return false;
        }

        var storedBytes = Encoding.UTF8.GetBytes(storedFingerprint);
        var computedBytes = Encoding.UTF8.GetBytes(computedFingerprint);

        return CryptographicOperations.FixedTimeEquals(storedBytes, computedBytes);
    }
}
