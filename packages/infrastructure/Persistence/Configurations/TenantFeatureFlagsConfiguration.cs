using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class TenantFeatureFlagsConfiguration : IEntityTypeConfiguration<TenantFeatureFlags>
{
    public void Configure(EntityTypeBuilder<TenantFeatureFlags> builder)
    {
        builder.ToTable("tenant_feature_flags", "tenancy");

        builder.HasKey(tf => tf.TenantId);

        builder.Property(tf => tf.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(tf => tf.FlagsJson)
            .HasColumnName("flags_json")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(tf => tf.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(tf => tf.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(tf => tf.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(tf => tf.TenantId)
            .HasConstraintName("fk_tenant_feature_flags_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
