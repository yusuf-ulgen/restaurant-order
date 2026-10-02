using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branding;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BrandAppearanceConfiguration : IEntityTypeConfiguration<BrandAppearance>
{
    public void Configure(EntityTypeBuilder<BrandAppearance> builder)
    {
        builder.ToTable("brand_appearances", "tenancy");

        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BrandAppearanceId(value))
            .IsRequired();

        builder.Property(ba => ba.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(ba => ba.BrandId)
            .HasColumnName("brand_id")
            .HasConversion(id => id.Value, value => new BrandId(value))
            .IsRequired();

        builder.Property(ba => ba.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(ba => ba.LogoUrl)
            .HasColumnName("logo_url")
            .HasMaxLength(500)
            .HasConversion(
                url => url.HasValue ? url.Value.Value : null,
                value => !string.IsNullOrEmpty(value) ? new AssetUrl(value) : null)
            .IsRequired(false);

        builder.Property(ba => ba.FaviconUrl)
            .HasColumnName("favicon_url")
            .HasMaxLength(500)
            .HasConversion(
                url => url.HasValue ? url.Value.Value : null,
                value => !string.IsNullOrEmpty(value) ? new AssetUrl(value) : null)
            .IsRequired(false);

        builder.Property(ba => ba.PrimaryColor)
            .HasColumnName("primary_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.PrimaryHoverColor)
            .HasColumnName("primary_hover_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.SecondaryColor)
            .HasColumnName("secondary_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.AccentColor)
            .HasColumnName("accent_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.SurfaceColor)
            .HasColumnName("surface_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.BackgroundColor)
            .HasColumnName("background_color")
            .HasMaxLength(7)
            .HasConversion(c => c.Value, value => new ColorHex(value))
            .IsRequired();

        builder.Property(ba => ba.FooterText)
            .HasColumnName("footer_text")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(ba => ba.DefaultShellTitle)
            .HasColumnName("default_shell_title")
            .HasMaxLength(150)
            .IsRequired(false);

        builder.Property(ba => ba.DefaultShellSubtitle)
            .HasColumnName("default_shell_subtitle")
            .HasMaxLength(250)
            .IsRequired(false);

        builder.Property(ba => ba.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(ba => ba.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(ba => ba.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // 1:1 relationship per brand inside a tenant
        builder.HasIndex(ba => new { ba.TenantId, ba.BrandId })
            .IsUnique()
            .HasDatabaseName("ix_brand_appearances_tenant_id_brand_id");

        // Composite foreign key to brands(tenant_id, id) ensuring strict tenant isolation
        builder.HasOne<Brand>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(ba => new { ba.TenantId, ba.BrandId })
            .HasConstraintName("fk_brand_appearances_brands_tenant_id_brand_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(ba => ba.TenantId)
            .HasConstraintName("fk_brand_appearances_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
