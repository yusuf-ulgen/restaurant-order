using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Orders;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class OrderItemModifierConfiguration : IEntityTypeConfiguration<OrderItemModifier>
{
    public void Configure(EntityTypeBuilder<OrderItemModifier> builder)
    {
        builder.ToTable("order_item_modifiers", "tenancy", t =>
        {
            t.HasCheckConstraint("ck_order_item_modifiers_price_delta", "price_delta_minor_units >= 0");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(m => m.OrderItemId)
            .HasColumnName("order_item_id")
            .HasConversion(id => id.Value, value => new OrderItemId(value))
            .IsRequired();

        builder.Property(m => m.ModifierGroupId)
            .HasColumnName("modifier_group_id")
            .IsRequired();

        builder.Property(m => m.ModifierGroupName)
            .HasColumnName("modifier_group_name")
            .HasMaxLength(OrderItemModifier.MaxNameLength)
            .IsRequired();

        builder.Property(m => m.ModifierOptionId)
            .HasColumnName("modifier_option_id")
            .IsRequired();

        builder.Property(m => m.ModifierOptionName)
            .HasColumnName("modifier_option_name")
            .HasMaxLength(OrderItemModifier.MaxNameLength)
            .IsRequired();

        builder.Property(m => m.PriceDeltaMinorUnits)
            .HasColumnName("price_delta_minor_units")
            .IsRequired();

        builder.HasIndex(m => m.OrderItemId)
            .HasDatabaseName("ix_order_item_modifiers_item_id");
    }
}
