using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Brands;

/// <summary>
/// Brand entity belonging to a Tenant organization.
/// Cannot be moved outside its tenant once created.
/// </summary>
public class Brand
{
    public BrandId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public Slug Slug { get; private set; }
    public BrandStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private Brand()
    {
    }

    private Brand(BrandId id, TenantId tenantId, string name, Slug slug, DateTime createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        SetName(name);
        Slug = slug;
        Status = BrandStatus.Active;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static Brand Create(TenantId tenantId, string name, string slug, BrandId? id = null)
    {
        var brandId = id ?? BrandId.New();
        var slugVo = new Slug(slug);
        return new Brand(brandId, tenantId, name, slugVo, DateTime.UtcNow);
    }

    public void UpdateName(string newName)
    {
        SetName(newName);
        Touch();
    }

    public void Deactivate()
    {
        if (Status == BrandStatus.Inactive)
        {
            return;
        }

        Status = BrandStatus.Inactive;
        Touch();
    }

    public void Activate()
    {
        if (Status == BrandStatus.Active)
        {
            return;
        }

        Status = BrandStatus.Active;
        Touch();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Brand name cannot be null, empty, or whitespace.");
        }

        Name = name.Trim();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
