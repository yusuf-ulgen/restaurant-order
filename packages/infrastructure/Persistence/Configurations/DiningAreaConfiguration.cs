using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class DiningAreaConfiguration : IEntityTypeConfiguration<DiningArea>
{
    public void Configure(EntityTypeBuilder<DiningArea> builder)
    {
        builder.ToTable("dining_areas", "tenancy");

        builder.HasKey(da => da.Id);

        builder.Property(da => da.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DiningAreaId(value))
            .IsRequired();

        builder.Property(da => da.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(da => da.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(da => da.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(da => da.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(da => da.AreaType)
            .HasColumnName("area_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(da => da.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(da => da.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(da => da.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(da => da.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(da => da.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique slug/code per branch in tenant
        builder.HasIndex(da => new { da.TenantId, da.BranchId, da.Code })
            .IsUnique()
            .HasDatabaseName("ix_dining_areas_tenant_id_branch_id_code");

        builder.HasIndex(da => new { da.TenantId, da.BranchId, da.SortOrder })
            .HasDatabaseName("ix_dining_areas_tenant_id_branch_id_sort_order");

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(da => new { da.TenantId, da.BranchId })
            .HasConstraintName("fk_dining_areas_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(da => da.TenantId)
            .HasConstraintName("fk_dining_areas_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
