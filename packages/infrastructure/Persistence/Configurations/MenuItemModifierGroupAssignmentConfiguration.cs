using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class MenuItemModifierGroupAssignmentConfiguration : IEntityTypeConfiguration<MenuItemModifierGroupAssignment>
{
    public void Configure(EntityTypeBuilder<MenuItemModifierGroupAssignment> builder)
    {
        builder.ToTable("menu_item_modifier_group_assignments", "tenancy");

        builder.HasKey(a => new { a.TenantId, a.MenuItemId, a.ModifierGroupId });

        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(a => a.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(a => a.MenuId)
            .HasColumnName("menu_id")
            .HasConversion(id => id.Value, value => new MenuId(value))
            .IsRequired();

        builder.Property(a => a.MenuItemId)
            .HasColumnName("menu_item_id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(a => a.ModifierGroupId)
            .HasColumnName("modifier_group_id")
            .HasConversion(id => id.Value, value => new ModifierGroupId(value))
            .IsRequired();

        builder.Property(a => a.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.MenuItemId, a.SortOrder })
            .HasDatabaseName("ix_item_modifier_assignments_tenant_item_sort_order");

        builder.HasOne(a => a.MenuItem)
            .WithMany(m => m.ModifierGroupAssignments)
            .HasPrincipalKey(m => new { m.TenantId, m.BranchId, m.Id })
            .HasForeignKey(a => new { a.TenantId, a.BranchId, a.MenuItemId })
            .HasConstraintName("fk_item_modifier_assignments_menu_items_tenant_branch_item")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ModifierGroup)
            .WithMany()
            .HasPrincipalKey(mg => new { mg.TenantId, mg.BranchId, mg.Id })
            .HasForeignKey(a => new { a.TenantId, a.BranchId, a.ModifierGroupId })
            .HasConstraintName("fk_item_modifier_assignments_groups_tenant_branch_group")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .HasConstraintName("fk_item_modifier_assignments_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
