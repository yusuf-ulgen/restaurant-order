using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", "tenancy", t =>
        {
            t.HasCheckConstraint("ck_order_items_quantity", "quantity >= 1 AND quantity <= 99");
            t.HasCheckConstraint("ck_order_items_unit_price", "unit_price_minor_units >= 0");
            t.HasCheckConstraint("ck_order_items_modifier_total", "modifier_total_minor_units >= 0");
            t.HasCheckConstraint("ck_order_items_line_total", "line_total_minor_units >= 0");
            t.HasCheckConstraint("ck_order_items_tax_rate", "tax_rate_basis_points >= 0 AND tax_rate_basis_points <= 10000");
            t.HasCheckConstraint("ck_order_items_status", "status IN (1, 2, 3, 4, 5, 6)");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrderItemId(value))
            .IsRequired();

        builder.Property(i => i.OrderId)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .IsRequired();

        builder.Property(i => i.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(i => i.MenuItemId)
            .HasColumnName("menu_item_id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(i => i.ItemVariantId)
            .HasColumnName("item_variant_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ItemVariantId(value.Value) : null)
            .IsRequired(false);

        builder.Property(i => i.ItemName)
            .HasColumnName("item_name")
            .HasMaxLength(OrderItem.MaxNameLength)
            .IsRequired();

        builder.Property(i => i.VariantName)
            .HasColumnName("variant_name")
            .HasMaxLength(OrderItem.MaxVariantNameLength)
            .IsRequired(false);

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(i => i.CustomerNote)
            .HasColumnName("customer_note")
            .HasMaxLength(OrderItem.MaxCustomerNoteLength)
            .IsRequired(false);

        builder.Property(i => i.PreparationStationId)
            .HasColumnName("preparation_station_id")
            .IsRequired(false);

        builder.Property(i => i.UnitPriceMinorUnits)
            .HasColumnName("unit_price_minor_units")
            .IsRequired();

        builder.Property(i => i.ModifierTotalMinorUnits)
            .HasColumnName("modifier_total_minor_units")
            .IsRequired();

        builder.Property(i => i.LineTotalMinorUnits)
            .HasColumnName("line_total_minor_units")
            .IsRequired();

        builder.Property(i => i.TaxRateBasisPoints)
            .HasColumnName("tax_rate_basis_points")
            .IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(i => i.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(i => i.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired(false);

        // Historical snapshot preservation: Restrict deletion of referenced catalog menu items
        builder.HasOne<MenuItem>()
            .WithMany()
            .HasForeignKey(i => i.MenuItemId)
            .HasPrincipalKey(mi => mi.Id)
            .HasConstraintName("fk_order_items_menu_item")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ItemVariant>()
            .WithMany()
            .HasForeignKey(i => i.ItemVariantId)
            .HasPrincipalKey(iv => iv.Id)
            .HasConstraintName("fk_order_items_item_variant")
            .OnDelete(DeleteBehavior.Restrict);

        // Modifiers relationship
        builder.HasMany(i => i.Modifiers)
            .WithOne()
            .HasForeignKey(m => m.OrderItemId)
            .HasPrincipalKey(i => i.Id)
            .HasConstraintName("fk_order_item_modifiers_item")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Modifiers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Operational query indexes
        builder.HasIndex(i => new { i.TenantId, i.OrderId })
            .HasDatabaseName("ix_order_items_tenant_order");

        builder.HasIndex(i => new { i.TenantId, i.Status })
            .HasDatabaseName("ix_order_items_tenant_status");

        builder.HasIndex(i => new { i.TenantId, i.PreparationStationId })
            .HasDatabaseName("ix_order_items_tenant_station");
    }
}
