using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories", "tenancy");

        builder.HasKey(mc => mc.Id);
        builder.HasAlternateKey(mc => new { mc.TenantId, mc.MenuId, mc.Id });

        builder.Property(mc => mc.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MenuCategoryId(value))
            .IsRequired();

        builder.Property(mc => mc.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(mc => mc.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(mc => mc.MenuId)
            .HasColumnName("menu_id")
            .HasConversion(id => id.Value, value => new MenuId(value))
            .IsRequired();

        builder.Property(mc => mc.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(mc => mc.Slug)
            .HasColumnName("slug")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(mc => mc.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(mc => mc.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(mc => mc.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(mc => mc.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(mc => mc.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(mc => mc.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique slug per menu in tenant
        builder.HasIndex(mc => new { mc.TenantId, mc.MenuId, mc.Slug })
            .IsUnique()
            .HasDatabaseName("ix_menu_categories_tenant_id_menu_id_slug");

        builder.HasIndex(mc => new { mc.TenantId, mc.MenuId, mc.SortOrder })
            .HasDatabaseName("ix_menu_categories_tenant_id_menu_id_sort_order");

        // Composite foreign key to menus(tenant_id, id)
        builder.HasOne<Menu>()
            .WithMany()
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .HasForeignKey(mc => new { mc.TenantId, mc.MenuId })
            .HasConstraintName("fk_menu_categories_menus_tenant_id_menu_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(mc => new { mc.TenantId, mc.BranchId })
            .HasConstraintName("fk_menu_categories_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(mc => mc.TenantId)
            .HasConstraintName("fk_menu_categories_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
