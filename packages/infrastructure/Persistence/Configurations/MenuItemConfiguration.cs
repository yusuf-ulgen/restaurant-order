using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items", "tenancy");

        builder.HasKey(m => m.Id);
        builder.HasAlternateKey(m => new { m.TenantId, m.MenuId, m.Id });

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(m => m.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(m => m.MenuId)
            .HasColumnName("menu_id")
            .HasConversion(id => id.Value, value => new MenuId(value))
            .IsRequired();

        builder.Property(m => m.CategoryId)
            .HasColumnName("category_id")
            .HasConversion(id => id.Value, value => new MenuCategoryId(value))
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Slug)
            .HasColumnName("slug")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.ShortDescription)
            .HasColumnName("short_description")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(m => m.FullDescription)
            .HasColumnName("full_description")
            .HasMaxLength(2000)
            .IsRequired(false);

        builder.Property(m => m.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(m => m.BasePriceMinorUnits)
            .HasColumnName("base_price_minor_units")
            .HasConversion(p => p.MinorUnits, value => PriceAmount.FromMinorUnits(value))
            .IsRequired();

        builder.Property(m => m.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(m => m.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique slug per menu in tenant
        builder.HasIndex(m => new { m.TenantId, m.MenuId, m.Slug })
            .IsUnique()
            .HasDatabaseName("ix_menu_items_tenant_id_menu_id_slug");

        builder.HasIndex(m => new { m.TenantId, m.CategoryId, m.SortOrder })
            .HasDatabaseName("ix_menu_items_tenant_id_category_id_sort_order");

        // Composite FK to menus
        builder.HasOne<Menu>()
            .WithMany()
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .HasForeignKey(m => new { m.TenantId, m.MenuId })
            .HasConstraintName("fk_menu_items_menus_tenant_id_menu_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to menu_categories
        builder.HasOne<MenuCategory>()
            .WithMany()
            .HasPrincipalKey(mc => new { mc.TenantId, mc.MenuId, mc.Id })
            .HasForeignKey(m => new { m.TenantId, m.MenuId, m.CategoryId })
            .HasConstraintName("fk_menu_items_categories_tenant_id_menu_id_category_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to branches
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId })
            .HasConstraintName("fk_menu_items_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // FK to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .HasConstraintName("fk_menu_items_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        // 1-to-many relationship with ItemVariant
        builder.HasMany(m => m.Variants)
            .WithOne()
            .HasForeignKey(v => v.MenuItemId)
            .HasConstraintName("fk_item_variants_menu_items_menu_item_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
