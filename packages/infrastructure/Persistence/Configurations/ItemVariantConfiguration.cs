using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class ItemVariantConfiguration : IEntityTypeConfiguration<ItemVariant>
{
    public void Configure(EntityTypeBuilder<ItemVariant> builder)
    {
        builder.ToTable("item_variants", "tenancy");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ItemVariantId(value))
            .IsRequired();

        builder.Property(v => v.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(v => v.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(v => v.MenuId)
            .HasColumnName("menu_id")
            .HasConversion(id => id.Value, value => new MenuId(value))
            .IsRequired();

        builder.Property(v => v.MenuItemId)
            .HasColumnName("menu_item_id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(v => v.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(v => v.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(v => v.AbsolutePriceMinorUnits)
            .HasColumnName("absolute_price_minor_units")
            .HasConversion(p => p.MinorUnits, value => PriceAmount.FromMinorUnits(value))
            .IsRequired();

        builder.Property(v => v.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(v => v.IsDefault)
            .HasColumnName("is_default")
            .IsRequired();

        builder.Property(v => v.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(v => v.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(v => v.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(v => v.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique variant code per item in tenant
        builder.HasIndex(v => new { v.TenantId, v.MenuItemId, v.Code })
            .IsUnique()
            .HasDatabaseName("ix_item_variants_tenant_id_menu_item_id_code");

        // Partial unique index: at most one active default variant per item
        builder.HasIndex(v => new { v.TenantId, v.MenuItemId })
            .IsUnique()
            .HasFilter("is_default = true AND is_active = true")
            .HasDatabaseName("ix_item_variants_single_active_default");

        builder.HasIndex(v => new { v.TenantId, v.MenuItemId, v.SortOrder })
            .HasDatabaseName("ix_item_variants_tenant_id_menu_item_id_sort_order");

        // Composite FK to menu_items
        builder.HasOne<MenuItem>()
            .WithMany(m => m.Variants)
            .HasPrincipalKey(m => new { m.TenantId, m.MenuId, m.Id })
            .HasForeignKey(v => new { v.TenantId, v.MenuId, v.MenuItemId })
            .HasConstraintName("fk_item_variants_menu_items_tenant_id_menu_id_item_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to branches
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(v => new { v.TenantId, v.BranchId })
            .HasConstraintName("fk_item_variants_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to menus
        builder.HasOne<Menu>()
            .WithMany()
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .HasForeignKey(v => new { v.TenantId, v.MenuId })
            .HasConstraintName("fk_item_variants_menus_tenant_id_menu_id")
            .OnDelete(DeleteBehavior.Cascade);

        // FK to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(v => v.TenantId)
            .HasConstraintName("fk_item_variants_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
