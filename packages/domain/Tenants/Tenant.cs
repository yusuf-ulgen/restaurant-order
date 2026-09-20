using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Tenants;

/// <summary>
/// Top-level restaurant organization aggregate root.
/// Enforces name validation, normalized slug uniqueness, and strict lifecycle transitions.
/// </summary>
public class Tenant
{
    public TenantId Id { get; private set; }
    public string Name { get; private set; } = null!;
    public Slug Slug { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private Tenant()
    {
    }

    private Tenant(TenantId id, string name, Slug slug, DateTime createdAtUtc)
    {
        Id = id;
        SetName(name);
        Slug = slug;
        Status = TenantStatus.Active;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static Tenant Create(string name, string slug, TenantId? id = null)
    {
        var tenantId = id ?? TenantId.New();
        var slugVo = new Slug(slug);
        return new Tenant(tenantId, name, slugVo, DateTime.UtcNow);
    }

    public void UpdateName(string newName)
    {
        EnsureNotClosed();
        SetName(newName);
        Touch();
    }

    public void Suspend(string? reason = null)
    {
        if (Status == TenantStatus.Closed)
        {
            throw new DomainException("Cannot suspend a closed tenant. Closed is a terminal state.");
        }

        if (Status == TenantStatus.Suspended)
        {
            return;
        }

        Status = TenantStatus.Suspended;
        Touch();
    }

    public void Activate()
    {
        if (Status == TenantStatus.Closed)
        {
            throw new DomainException("Cannot activate a permanently closed tenant.");
        }

        if (Status == TenantStatus.Active)
        {
            return;
        }

        Status = TenantStatus.Active;
        Touch();
    }

    public void Close(string? reason = null)
    {
        if (Status == TenantStatus.Closed)
        {
            return;
        }

        Status = TenantStatus.Closed;
        Touch();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tenant name cannot be null, empty, or whitespace.");
        }

        Name = name.Trim();
    }

    private void EnsureNotClosed()
    {
        if (Status == TenantStatus.Closed)
        {
            throw new DomainException("Modifications are not allowed on a closed tenant.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
