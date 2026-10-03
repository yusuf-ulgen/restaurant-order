using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchFeatureFlagsConfiguration : IEntityTypeConfiguration<BranchFeatureFlags>
{
    public void Configure(EntityTypeBuilder<BranchFeatureFlags> builder)
    {
        builder.ToTable("branch_feature_flags", "tenancy");

        builder.HasKey(bf => new { bf.TenantId, bf.BranchId });

        builder.Property(bf => bf.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(bf => bf.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(bf => bf.OverridesJson)
            .HasColumnName("overrides_json")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(bf => bf.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(bf => bf.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(bf => bf.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(bf => new { bf.TenantId, bf.BranchId })
            .HasConstraintName("fk_branch_feature_flags_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(bf => bf.TenantId)
            .HasConstraintName("fk_branch_feature_flags_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
