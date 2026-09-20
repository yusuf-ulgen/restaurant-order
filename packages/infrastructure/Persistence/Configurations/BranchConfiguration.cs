using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", "tenancy", br =>
        {
            br.HasCheckConstraint("ck_branches_status", "status IN ('Active', 'Suspended', 'Closed')");
        });

        builder.HasKey(br => br.Id);

        builder.Property(br => br.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(br => br.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(br => br.BrandId)
            .HasColumnName("brand_id")
            .HasConversion(id => id.Value, value => new BrandId(value))
            .IsRequired();

        builder.Property(br => br.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(br => br.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64)
            .HasConversion(slug => slug.Value, value => new Slug(value))
            .IsRequired();

        // Branch slug unique within tenant: guarantees unique /t/{tenant}/{branch} routing across brands
        builder.HasIndex(br => new { br.TenantId, br.Slug })
            .IsUnique()
            .HasDatabaseName("ix_branches_tenant_id_slug");

        builder.Property(br => br.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(50)
            .HasConversion(tz => tz.Id, value => new Timezone(value))
            .IsRequired();

        builder.Property(br => br.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasConversion(c => c.Code, value => new Currency(value))
            .IsRequired();

        builder.Property(br => br.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(br => br.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(br => br.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(br => br.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Foreign key to tenants with Restrict behavior to prevent cascade loss
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(br => br.TenantId)
            .HasConstraintName("fk_branches_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite foreign key to brands(tenant_id, id) enforcing cross-tenant mismatch prevention
        builder.HasOne<Brand>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(br => new { br.TenantId, br.BrandId })
            .HasConstraintName("fk_branches_brands_tenant_id_brand_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(br => new { br.TenantId, br.BrandId })
            .HasDatabaseName("ix_branches_tenant_id_brand_id");
    }
}
