using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branding;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchThemeOverrideConfiguration : IEntityTypeConfiguration<BranchThemeOverride>
{
    public void Configure(EntityTypeBuilder<BranchThemeOverride> builder)
    {
        builder.ToTable("branch_theme_overrides", "tenancy");

        builder.HasKey(bto => bto.Id);

        builder.Property(bto => bto.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BranchThemeOverrideId(value))
            .IsRequired();

        builder.Property(bto => bto.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(bto => bto.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(bto => bto.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(bto => bto.LogoUrl)
            .HasColumnName("logo_url")
            .HasMaxLength(500)
            .HasConversion(
                url => url.HasValue ? url.Value.Value : null,
                value => !string.IsNullOrEmpty(value) ? new AssetUrl(value) : null)
            .IsRequired(false);

        builder.Property(bto => bto.HeaderSubtitle)
            .HasColumnName("header_subtitle")
            .HasMaxLength(250)
            .IsRequired(false);

        builder.Property(bto => bto.FooterBranchInfo)
            .HasColumnName("footer_branch_info")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(bto => bto.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(bto => bto.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(bto => bto.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // 1:1 relationship per branch inside a tenant
        builder.HasIndex(bto => new { bto.TenantId, bto.BranchId })
            .IsUnique()
            .HasDatabaseName("ix_branch_theme_overrides_tenant_id_branch_id");

        // Composite foreign key to branches(tenant_id, id) ensuring strict tenant isolation
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(br => new { br.TenantId, br.Id })
            .HasForeignKey(bto => new { bto.TenantId, bto.BranchId })
            .HasConstraintName("fk_branch_theme_overrides_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(bto => bto.TenantId)
            .HasConstraintName("fk_branch_theme_overrides_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
