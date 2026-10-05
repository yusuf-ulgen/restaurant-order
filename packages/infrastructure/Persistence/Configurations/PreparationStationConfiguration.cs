using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class PreparationStationConfiguration : IEntityTypeConfiguration<PreparationStation>
{
    public void Configure(EntityTypeBuilder<PreparationStation> builder)
    {
        builder.ToTable("preparation_stations", "tenancy");

        builder.HasKey(ps => ps.Id);
        builder.HasAlternateKey(ps => new { ps.TenantId, ps.BranchId, ps.Id });

        builder.Property(ps => ps.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PreparationStationId(value))
            .IsRequired();

        builder.Property(ps => ps.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(ps => ps.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(ps => ps.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(ps => ps.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(ps => ps.StationType)
            .HasColumnName("station_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(ps => ps.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(ps => ps.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(ps => ps.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(ps => ps.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(ps => ps.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique fixed code per branch in tenant
        builder.HasIndex(ps => new { ps.TenantId, ps.BranchId, ps.Code })
            .IsUnique()
            .HasDatabaseName("ix_preparation_stations_tenant_id_branch_id_code");

        builder.HasIndex(ps => new { ps.TenantId, ps.BranchId, ps.SortOrder })
            .HasDatabaseName("ix_preparation_stations_tenant_id_branch_id_sort_order");

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(ps => new { ps.TenantId, ps.BranchId })
            .HasConstraintName("fk_preparation_stations_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(ps => ps.TenantId)
            .HasConstraintName("fk_preparation_stations_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
