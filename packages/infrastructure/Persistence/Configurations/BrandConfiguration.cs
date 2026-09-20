using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands", "tenancy", b =>
        {
            b.HasCheckConstraint("ck_brands_status", "status IN ('Active', 'Inactive')");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BrandId(value))
            .IsRequired();

        builder.Property(b => b.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(b => b.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(b => b.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64)
            .HasConversion(slug => slug.Value, value => new Slug(value))
            .IsRequired();

        // Brand slug unique within a tenant
        builder.HasIndex(b => new { b.TenantId, b.Slug })
            .IsUnique()
            .HasDatabaseName("ix_brands_tenant_id_slug");

        // Composite alternate key on (TenantId, Id) to allow composite foreign key reference from branches
        builder.HasAlternateKey(b => new { b.TenantId, b.Id })
            .HasName("ak_brands_tenant_id_id");

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(b => b.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(b => b.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Foreign key to tenants with Restrict behavior to prevent accidental cascading deletion
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .HasConstraintName("fk_brands_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
