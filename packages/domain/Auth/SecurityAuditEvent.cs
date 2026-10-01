using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an append-only security audit event.
/// Records critical identity, authentication, session, and role state changes.
/// Modifying or deleting rows is strictly prohibited at both domain and database levels.
/// </summary>
public sealed class SecurityAuditEvent
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string EventType { get; private set; }
    public UserId? UserId { get; private set; }
    public BranchId? BranchId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? DetailsJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private SecurityAuditEvent()
    {
        // Required by EF Core
        EventType = string.Empty;
    }

    public static SecurityAuditEvent Create(
        TenantId tenantId,
        string eventType,
        DateTimeOffset nowUtc,
        UserId? userId = null,
        BranchId? branchId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? detailsJson = null,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("AuditEvent eventType cannot be empty.");
        }

        return new SecurityAuditEvent
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            EventType = eventType.Trim(),
            UserId = userId,
            BranchId = branchId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DetailsJson = detailsJson,
            CreatedAtUtc = nowUtc
        };
    }
}
